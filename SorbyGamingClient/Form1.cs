using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using QRCoder;
using SocketIOClient;

namespace SorbyGamingClient
{
    // Online: PC'en viser en QR-kode, og serveren starter sessionen.
    // Offline: PC'en viser spørgeskemaet selv, gemmer sessionen lokalt og
    // sender den til serveren, når der er internet igen.
    public partial class Form1 : Form
    {
        private const string ServerUrl = "https://sorby-esport.onrender.com";

        private static readonly TimeSpan OfflineStartupDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
        private const int ConnectionCheckIntervalMs = 15000;

        // GitHub tillader 60 opslag i timen pr. offentlig IP, og PC'erne til
        // et event deler typisk én IP. Derfor tjekkes kun én gang i timen.
        private const int UpdateCheckIntervalMs = 60 * 60 * 1000;

        private string? PcId;
        private string? QrUrl;

        private const int TestDurationSeconds = 30;

        private SocketIO? socket;
        private bool connecting;
        private DateTime lastConnectAttempt = DateTime.MinValue;
        private bool isOnline;

        private System.Windows.Forms.Timer? sessionTimer;
        private System.Windows.Forms.Timer? connectionTimer;
        private System.Windows.Forms.Timer? startupTimer;
        private System.Windows.Forms.Timer? updateTimer;
        private readonly UpdateService updateService = new UpdateService();
        private readonly bool resumeAfterUpdate;
        private int remainingSeconds;
        private bool inSession;
        private static readonly HttpClient HttpClient = new HttpClient();

        private PcSetupForm? setupForm;

        private bool lockScreenReady;
        private bool showQr;
        private QRCodeData? qrData;
        private OfflineSurveyPanel? surveyPanel;
        private SessionTimerOverlay? timerOverlay;

        // Har eventet ingen baggrund, vises den manuelle baggrund bag QR-koden,
        // og så bruges den lokale QR-placering.
        private bool eventHasBackground;
        private bool manualBackgroundShown;

        // Baggrunden tegnes selv (i stedet for BackgroundImage), så den kan
        // fylde skærmen eller vises helt med sløret kant.
        private Image? backgroundSource;
        private string? backgroundFit;
        private Bitmap? composedBackground;
        private ManualSettings manualSettings = LocalStore.LoadManual();

        private CachedEvent? cachedEvent = LocalStore.LoadEvent();
        private OfflineSession? currentOfflineSession;

        public Form1(string[] args)
        {
            InitializeComponent();

            Console.WriteLine($"Sørby Gaming version {updateService.CurrentVersion}");

            // Efter en opdatering starter PC'en igen som samme PC-nummer.
            string? savedPcId = LocalStore.LoadPcId();

            if (args.Contains(UpdateService.ResumeArgument) && !string.IsNullOrWhiteSpace(savedPcId))
            {
                PcId = savedPcId;
                resumeAfterUpdate = true;
                return;
            }

            ShowPcSetup();
        }

        // ---------------------------------------------------------
        // PC OPSÆTNING
        // ---------------------------------------------------------

        private void ShowPcSetup()
        {
            setupForm = new PcSetupForm(updateService.CurrentVersion, LocalStore.LoadPcId());
            setupForm.StartRequested += SetupForm_StartRequested;

            DialogResult setupResult = setupForm.ShowDialog(this);

            // Hvis opsætningen lukkes med Alt+F4 eller vinduets luk-knap,
            // skal den skjulte hovedformular også afsluttes.
            if (setupResult != DialogResult.OK && !IsDisposed)
            {
                Close();
            }
        }

        private async void SetupForm_StartRequested(object? sender, EventArgs e)
        {
            if (setupForm == null)
            {
                return;
            }

            string enteredPcId = setupForm.PcNumber;
            string code = setupForm.AdminCode;

            setupForm.SetBusy("Tjekker admin-koden...");
            string? adminError = await AdminCodeVerifier.VerifyAsync(ServerUrl, code, enteredPcId);

            if (adminError != null && !TryAdoptOfflineCode(code, out string? offlineError))
            {
                setupForm.RejectAdminCode(offlineError ?? adminError);
                return;
            }

            PcId = enteredPcId;
            LocalStore.SavePcId(enteredPcId);

            setupForm.SetBusy("Forbinder til serveren...");
            StartConnection();
        }

        // En PC, der aldrig har været online, har ingen gemt admin-kode.
        // Så kan den indtastede kode gøres til PC'ens offline-kode. Den
        // erstattes automatisk af serverens kode ved næste online-login.
        private bool TryAdoptOfflineCode(string code, out string? error)
        {
            error = null;

            if (!AdminCodeVerifier.LastCheckWasOffline || AdminCodeVerifier.HasOfflineCode || setupForm == null)
            {
                return false;
            }

            if (code.Length < 4)
            {
                error = "Offline-koden skal være mindst 4 tegn.";
                return false;
            }

            bool useCode = SorbyDialog.Confirm(
                setupForm,
                "Ingen internetforbindelse",
                "Der er ikke gemt en admin-kode på denne PC. Vil du bruge den indtastede kode som admin-kode, indtil PC'en kommer online?",
                "Brug koden",
                "Nej"
            );

            if (!useCode)
            {
                return false;
            }

            AdminCodeVerifier.SetOfflineCode(code);
            return true;
        }

        // ---------------------------------------------------------
        // FORBINDELSE TIL SERVER
        // ---------------------------------------------------------

        private void StartConnection()
        {
            connectionTimer = new System.Windows.Forms.Timer { Interval = ConnectionCheckIntervalMs };
            connectionTimer.Tick += ConnectionTimer_Tick;
            connectionTimer.Start();

            // Kan serveren ikke nås hurtigt, starter PC'en i offline-tilstand.
            startupTimer = new System.Windows.Forms.Timer { Interval = (int)OfflineStartupDelay.TotalMilliseconds };
            startupTimer.Tick += (sender, e) =>
            {
                startupTimer.Stop();
                FinishStartup();
            };
            startupTimer.Start();

            _ = TryConnectAsync();
        }

        private void ConnectionTimer_Tick(object? sender, EventArgs e)
        {
            if (!isOnline)
            {
                _ = TryConnectAsync();
            }
            else if (PcId != null)
            {
                _ = SessionSync.SyncAsync(ServerUrl, PcId, currentOfflineSession?.Id);
            }
        }

        private async Task TryConnectAsync()
        {
            if (connecting || PcId == null || DateTime.Now - lastConnectAttempt < ConnectTimeout)
            {
                return;
            }

            connecting = true;
            lastConnectAttempt = DateTime.Now;

            SocketIO? oldSocket = socket;
            SocketIO newSocket = CreateSocket();
            socket = newSocket;
            DisposeSocket(oldSocket);

            try
            {
                Task connectTask = newSocket.ConnectAsync();
                Task finished = await Task.WhenAny(connectTask, Task.Delay(ConnectTimeout));

                if (finished != connectTask)
                {
                    Console.WriteLine("Serveren svarede ikke i tide.");
                }
                else
                {
                    await connectTask;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke forbinde til serveren: {ex.Message}");

                if (!lockScreenReady)
                {
                    FinishStartup();
                }
            }
            finally
            {
                connecting = false;
            }
        }

        private SocketIO CreateSocket()
        {
            SocketIO client = new SocketIO(new Uri(ServerUrl), new SocketIOOptions
            {
                Reconnection = false,
                ConnectionTimeout = ConnectTimeout
            });

            client.OnConnected += async (sender, e) =>
            {
                Console.WriteLine("Forbundet til serveren.");

                if (PcId != null)
                {
                    await client.EmitAsync("register-pc", new object[] { PcId });
                }
            };

            client.OnDisconnected += (sender, e) =>
            {
                Console.WriteLine("Forbindelsen til serveren er afbrudt.");
                RunOnUi(client, GoOffline);
            };

            // -------------------------------------------------
            // PC REGISTRERET
            // -------------------------------------------------

            client.On("registered", response =>
            {
                ServerMessage message = ServerMessage.Read(() => response.GetValue<JsonElement>(0));
                Console.WriteLine($"PC {PcId} registreret på serveren.");

                RunOnUi(client, () =>
                {
                    isOnline = true;
                    ApplyServerMessage(message);
                    FinishStartup();
                    RefreshLockScreen();

                    if (PcId != null)
                    {
                        _ = SessionSync.SyncAsync(ServerUrl, PcId, currentOfflineSession?.Id);
                    }
                });

                return Task.CompletedTask;
            });

            // -------------------------------------------------
            // REGISTRERING AFVIST
            // -------------------------------------------------

            client.On("registration-failed", response =>
            {
                Console.WriteLine("Registrering afvist af serveren.");

                RunOnUi(client, () =>
                {
                    if (lockScreenReady)
                    {
                        // PC'en kører videre offline og prøver igen senere.
                        return;
                    }

                    startupTimer?.Stop();
                    connectionTimer?.Stop();
                    DisposeSocket(socket);
                    socket = null;

                    setupForm?.RejectPcNumber($"PC {PcId} er allerede i brug på en anden computer. Vælg et andet PC-nummer.");
                });

                return Task.CompletedTask;
            });

            // -------------------------------------------------
            // SESSION START
            // -------------------------------------------------

            client.On("start-session", response =>
            {
                Console.WriteLine("Session modtaget fra serveren.");

                int durationSeconds = TestDurationSeconds;

                try
                {
                    JsonElement data = response.GetValue<JsonElement>(0);

                    if (data.TryGetProperty("duration", out JsonElement durationElement))
                    {
                        durationSeconds = durationElement.GetInt32();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Kunne ikke læse sessionens varighed: {ex.Message}");
                }

                RunOnUi(client, () =>
                {
                    if (!inSession)
                    {
                        StartSession(durationSeconds);
                    }
                });

                return Task.CompletedTask;
            });

            // -------------------------------------------------
            // NY QR EFTER SESSION / EVENT ÆNDRET
            // -------------------------------------------------

            client.On("new-qr", response =>
            {
                Console.WriteLine("Ny QR-kode modtaget.");
                ServerMessage message = ServerMessage.Read(() => response.GetValue<JsonElement>(0));
                RunOnUi(client, () =>
                {
                    ApplyServerMessage(message);
                    RefreshLockScreen();
                });
                return Task.CompletedTask;
            });

            client.On("event-updated", response =>
            {
                Console.WriteLine("Eventet er opdateret.");
                ServerMessage message = ServerMessage.Read(() => response.GetValue<JsonElement>(0));
                RunOnUi(client, () =>
                {
                    ApplyServerMessage(message);
                    RefreshLockScreen();
                });
                return Task.CompletedTask;
            });

            return client;
        }

        // Kører handlingen på UI-tråden, men kun hvis beskeden kommer fra den
        // aktuelle forbindelse (ikke fra en gammel, afbrudt forbindelse).
        private void RunOnUi(SocketIO source, Action action)
        {
            if (IsDisposed)
            {
                return;
            }

            BeginInvoke(new Action(() =>
            {
                if (source == socket)
                {
                    action();
                }
            }));
        }

        private static void DisposeSocket(SocketIO? oldSocket)
        {
            if (oldSocket == null)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await oldSocket.DisconnectAsync();
                }
                catch
                {
                    // Ignorer fejl ved lukning af en gammel forbindelse.
                }

                oldSocket.Dispose();
            });
        }

        private void GoOffline()
        {
            if (!isOnline)
            {
                return;
            }

            isOnline = false;
            RefreshLockScreen();
        }

        // Gemmer eventet fra serveren, så PC'en kan køre det uden internet.
        private void ApplyServerMessage(ServerMessage message)
        {
            if (message.QrUrl != null)
            {
                UpdateQrCode(message.QrUrl);
            }

            if (message.HasEvent)
            {
                cachedEvent = message.Event;
                LocalStore.SaveEvent(cachedEvent);
            }

            if (message.HasBackground)
            {
                _ = SetOnlineBackgroundAsync(message.BackgroundImageUrl);
            }
        }

        // ---------------------------------------------------------
        // FULLSCREEN
        // ---------------------------------------------------------

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (!string.IsNullOrEmpty(PcId))
            {
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.PrimaryScreen!.Bounds;
                WindowState = FormWindowState.Maximized;
                TopMost = true;
                BringToFront();
                Activate();
            }

            if (resumeAfterUpdate)
            {
                FinishStartup();
                StartConnection();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.F4))
            {
                // QR-skærmen skal blive åben under eventet.
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------------------------------------------------
        // OPRET LÅSESKÆRM
        // ---------------------------------------------------------

        private void FinishStartup()
        {
            if (lockScreenReady || PcId == null)
            {
                return;
            }

            lockScreenReady = true;
            startupTimer?.Stop();

            if (setupForm != null)
            {
                setupForm.DialogResult = DialogResult.OK;
                setupForm.Close();
            }

            SetupWindow();
            SetupHotkey();
            RefreshLockScreen();
            StartUpdateChecks();
        }

        private void SetupWindow()
        {
            Text = "Sørby Gaming";
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(20, 20, 20);
            DoubleBuffered = true;
            ResizeRedraw = true;

            // Al tekst står på eventets baggrundsbillede, så skærmen viser
            // kun QR-koden. Den tegnes i OnPaint det sted, eventet angiver.
            surveyPanel = new OfflineSurveyPanel { Visible = false };
            surveyPanel.StartRequested += StartOfflineSession;

            Controls.Add(surveyPanel);

            Resize += (sender, e) => PositionOverlay();
            surveyPanel.SizeChanged += (sender, e) => PositionOverlay();
        }

        // Online med QR-kode: vis QR. Ellers: vis spørgeskemaet på skærmen.
        private void RefreshLockScreen()
        {
            if (!lockScreenReady || surveyPanel == null || inSession)
            {
                return;
            }

            showQr = isOnline && QrUrl != null;
            surveyPanel.Visible = !showQr;

            if (showQr && !eventHasBackground)
            {
                byte[]? manualBackground = LocalStore.LoadManualBackground();
                SetBackgroundImage(manualBackground, manualSettings.BackgroundFit);
                manualBackgroundShown = manualBackground != null;
            }

            if (!showQr)
            {
                surveyPanel.Build(
                    cachedEvent?.Name,
                    cachedEvent?.Questions.Count > 0 ? cachedEvent.Questions : LocalStore.DefaultQuestions(),
                    OfflineDurationSeconds() / 60
                );

                SetBackgroundImage(
                    LocalStore.LoadOfflineBackground(),
                    LocalStore.HasManualBackground ? manualSettings.BackgroundFit : cachedEvent?.BackgroundFit);
                manualBackgroundShown = false;
            }

            PositionOverlay();
            Invalidate();
        }

        private void PositionOverlay()
        {
            if (surveyPanel != null)
            {
                surveyPanel.Left = (ClientSize.Width - surveyPanel.Width) / 2;
                surveyPanel.Top = (ClientSize.Height - surveyPanel.Height) / 2;
            }
        }

        // QR-koden placeres i forhold til selve billedet (ikke skærmen), så den
        // står samme sted på baggrunden uanset skærmens format.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (!showQr || qrData == null || inSession)
            {
                return;
            }

            Rectangle area = backgroundSource == null
                ? ClientRectangle
                : QrRenderer.ImageRect(ClientSize, backgroundSource.Size, backgroundFit);

            QrLayout? layout = manualBackgroundShown ? manualSettings.QrLayout : cachedEvent?.QrLayout;

            QrRenderer.DrawCard(e.Graphics, qrData,
                QrRenderer.Place(area, ClientRectangle, layout, LogicalToDeviceUnits(QrRenderer.DefaultSizePixels)));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (backgroundSource == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                base.OnPaintBackground(e);
                return;
            }

            if (composedBackground == null || composedBackground.Size != ClientSize)
            {
                composedBackground?.Dispose();
                composedBackground = QrRenderer.ComposeBackground(backgroundSource, ClientSize, backgroundFit);
            }

            e.Graphics.DrawImageUnscaled(composedBackground, 0, 0);
        }

        // ---------------------------------------------------------
        // BAGGRUND OG QR-KODE
        // ---------------------------------------------------------

        private async Task SetOnlineBackgroundAsync(string? backgroundImageUrl)
        {
            if (string.IsNullOrWhiteSpace(backgroundImageUrl))
            {
                eventHasBackground = false;
                LocalStore.SaveEventBackground(null);

                if (isOnline)
                {
                    SetBackgroundImage(null, null);
                    manualBackgroundShown = false;
                    RefreshLockScreen();
                }

                return;
            }

            eventHasBackground = true;

            try
            {
                byte[] imageBytes = await HttpClient.GetByteArrayAsync(backgroundImageUrl);

                if (cachedEvent != null)
                {
                    LocalStore.SaveEventBackground(imageBytes);
                }

                if (isOnline)
                {
                    SetBackgroundImage(imageBytes, cachedEvent?.BackgroundFit);
                    manualBackgroundShown = false;
                    Invalidate();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke hente eventbaggrund: {ex.Message}");
            }
        }

        private void SetBackgroundImage(byte[]? imageBytes, string? fit)
        {
            Image? newImage = null;

            try
            {
                if (imageBytes != null)
                {
                    using MemoryStream stream = new MemoryStream(imageBytes);
                    using Image temporaryImage = Image.FromStream(stream);
                    newImage = new Bitmap(temporaryImage);
                }
            }
            catch (Exception ex)
            {
                // Fx WebP, som Windows Forms ikke kan læse. Web-panelet sender JPG.
                Console.WriteLine($"Kunne ikke vise baggrund: {ex.Message}");
                return;
            }

            backgroundSource?.Dispose();
            backgroundSource = newImage;
            backgroundFit = fit;
            composedBackground?.Dispose();
            composedBackground = null;
            Invalidate();
        }

        private void UpdateQrCode(string qrUrl)
        {
            QrUrl = qrUrl;

            try
            {
                QRCodeData? oldData = qrData;
                qrData = QrRenderer.Create(qrUrl);
                oldData?.Dispose();
                Invalidate();

                Console.WriteLine($"QR-kode opdateret: {qrUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke generere QR-koden: {ex.Message}");
            }
        }

        // ---------------------------------------------------------
        // AUTOMATISK OPDATERING
        // ---------------------------------------------------------

        private void StartUpdateChecks()
        {
            // Spred tjekkene, så PC'erne ikke spørger GitHub på samme tid.
            int jitterMs = Random.Shared.Next(0, 5 * 60 * 1000);

            updateTimer = new System.Windows.Forms.Timer { Interval = UpdateCheckIntervalMs + jitterMs };
            updateTimer.Tick += async (sender, e) => await CheckForUpdateAsync();
            updateTimer.Start();

            _ = CheckForUpdateAsync();
        }

        private async Task CheckForUpdateAsync()
        {
            await updateService.CheckAndDownloadAsync();
            TryApplyUpdate();
        }

        // Installerer kun, når PC'en står låst og ingen session kører.
        private void TryApplyUpdate()
        {
            if (!updateService.HasPendingUpdate || !lockScreenReady || inSession)
            {
                return;
            }

            Console.WriteLine("Installerer opdatering og genstarter.");
            updateService.ApplyAndRestart();
        }

        // ---------------------------------------------------------
        // OFFLINE-SESSION
        // ---------------------------------------------------------

        // Manuel tid (admin-menuen) > eventets tid > standard 10 minutter.
        private int OfflineDurationSeconds()
        {
            int? manualMinutes = LocalStore.LoadManual().DurationMinutes;

            if (manualMinutes is > 0)
            {
                return manualMinutes.Value * 60;
            }

            return cachedEvent?.DurationSeconds > 0
                ? cachedEvent.DurationSeconds
                : LocalStore.DefaultDurationSeconds;
        }

        private void StartOfflineSession(Dictionary<string, string?> answers)
        {
            if (inSession)
            {
                return;
            }

            int durationSeconds = OfflineDurationSeconds();

            currentOfflineSession = new OfflineSession
            {
                EventId = cachedEvent?.Id,
                StartedAt = DateTime.UtcNow,
                DurationSeconds = durationSeconds,
                Answers = answers
            };

            // Gemmes allerede ved start, så sessionen ikke går tabt,
            // hvis PC'en bliver slukket undervejs.
            LocalStore.SaveSession(currentOfflineSession);

            StartSession(durationSeconds);
        }

        // ---------------------------------------------------------
        // START SESSION
        // ---------------------------------------------------------

        private void StartSession(int durationSeconds)
        {
            Console.WriteLine($"Session startet i {durationSeconds} sekunder.");

            if (sessionTimer != null)
            {
                sessionTimer.Stop();
                sessionTimer.Dispose();
                sessionTimer = null;
            }

            inSession = true;
            remainingSeconds = durationSeconds;

            Hide();

            CloseTimerOverlay();
            timerOverlay = new SessionTimerOverlay(durationSeconds);
            timerOverlay.Show();

            sessionTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            sessionTimer.Tick += SessionTimer_Tick;
            sessionTimer.Start();

            Console.WriteLine("PC er nu åben.");
        }

        // ---------------------------------------------------------
        // SESSION TIMER
        // ---------------------------------------------------------

        private async void SessionTimer_Tick(object? sender, EventArgs e)
        {
            remainingSeconds--;
            timerOverlay?.SetRemaining(remainingSeconds);

            if (remainingSeconds <= 0)
            {
                await EndSession();
            }
        }

        private void CloseTimerOverlay()
        {
            timerOverlay?.Close();
            timerOverlay?.Dispose();
            timerOverlay = null;
        }

        // ---------------------------------------------------------
        // SESSION SLUT
        // ---------------------------------------------------------

        private async Task EndSession()
        {
            if (sessionTimer != null)
            {
                sessionTimer.Stop();
                sessionTimer.Dispose();
                sessionTimer = null;
            }

            Console.WriteLine("Session afsluttet.");

            CloseTimerOverlay();
            inSession = false;

            Show();
            WindowState = FormWindowState.Normal;
            Bounds = Screen.PrimaryScreen!.Bounds;
            WindowState = FormWindowState.Maximized;
            TopMost = true;
            BringToFront();
            Activate();

            if (currentOfflineSession != null)
            {
                currentOfflineSession.EndedAt = DateTime.UtcNow;
                LocalStore.SaveSession(currentOfflineSession);
                currentOfflineSession = null;

                RefreshLockScreen();

                if (isOnline && PcId != null)
                {
                    await SessionSync.SyncAsync(ServerUrl, PcId, null);
                }

                TryApplyUpdate();
                return;
            }

            RefreshLockScreen();

            if (socket != null && isOnline)
            {
                try
                {
                    await socket.EmitAsync("session-ended", new object[] { PcId! });
                    Console.WriteLine("Serveren er informeret om, at sessionen er slut.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Kunne ikke informere serveren: {ex.Message}");
                }
            }

            TryApplyUpdate();
        }

        // ---------------------------------------------------------
        // ADMIN HOTKEY
        // ---------------------------------------------------------

        private void SetupHotkey()
        {
            KeyPreview = true;
            KeyDown += Form1_KeyDown;
        }

        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.S)
            {
                ShowAdminLogin();
            }
        }

        // ---------------------------------------------------------
        // ADMIN LOGIN
        // ---------------------------------------------------------

        private void ShowAdminLogin()
        {
            AdminChoice choice;

            using (AdminMenuForm menu = new AdminMenuForm(PcId, isOnline, code => AdminCodeVerifier.VerifyAsync(ServerUrl, code, PcId)))
            {
                menu.ShowDialog(this);
                choice = menu.Choice;
            }

            if (choice == AdminChoice.StopEvent)
            {
                Application.Exit();
                return;
            }

            if (choice != AdminChoice.Settings)
            {
                return;
            }

            using OfflineSettingsForm settingsForm = new OfflineSettingsForm(cachedEvent);
            settingsForm.ShowDialog(this);

            // Baggrund kan være ændret, selv om dialogen blev lukket uden at gemme.
            manualSettings = LocalStore.LoadManual();
            RefreshLockScreen();
        }

        // ---------------------------------------------------------
        // LUK PROGRAM
        // ---------------------------------------------------------

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            sessionTimer?.Stop();
            sessionTimer?.Dispose();
            sessionTimer = null;
            CloseTimerOverlay();

            connectionTimer?.Stop();
            connectionTimer?.Dispose();
            startupTimer?.Stop();
            startupTimer?.Dispose();
            updateTimer?.Stop();
            updateTimer?.Dispose();

            // En igangværende offline-session gemmes som afsluttet nu.
            if (currentOfflineSession != null)
            {
                currentOfflineSession.EndedAt = DateTime.UtcNow;
                LocalStore.SaveSession(currentOfflineSession);
                currentOfflineSession = null;
            }

            qrData?.Dispose();
            qrData = null;
            composedBackground?.Dispose();
            composedBackground = null;
            backgroundSource?.Dispose();
            backgroundSource = null;

            SocketIO? oldSocket = socket;
            socket = null;
            DisposeSocket(oldSocket);

            base.OnFormClosed(e);
        }

        // ---------------------------------------------------------
        // BESKEDER FRA SERVEREN
        // ---------------------------------------------------------

        private sealed class ServerMessage
        {
            public string? QrUrl { get; private set; }
            public bool HasBackground { get; private set; }
            public string? BackgroundImageUrl { get; private set; }
            public bool HasEvent { get; private set; }
            public CachedEvent? Event { get; private set; }

            public static ServerMessage Read(Func<JsonElement> getData)
            {
                ServerMessage message = new ServerMessage();

                try
                {
                    JsonElement data = getData();

                    if (data.TryGetProperty("qrUrl", out JsonElement qrElement) &&
                        qrElement.ValueKind == JsonValueKind.String)
                    {
                        message.QrUrl = qrElement.GetString();
                    }

                    if (data.TryGetProperty("backgroundImageUrl", out JsonElement backgroundElement))
                    {
                        message.HasBackground = true;
                        message.BackgroundImageUrl = backgroundElement.ValueKind == JsonValueKind.String
                            ? backgroundElement.GetString()
                            : null;
                    }

                    // Ældre servere sender ikke "event"; så beholdes det gemte event.
                    if (data.TryGetProperty("event", out JsonElement eventElement))
                    {
                        message.HasEvent = true;
                        message.Event = eventElement.ValueKind == JsonValueKind.Object
                            ? eventElement.Deserialize<CachedEvent>(LocalStore.JsonOptions)
                            : null;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Kunne ikke læse besked fra serveren: {ex.Message}");
                }

                return message;
            }
        }
    }
}
