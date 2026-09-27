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

        private Form? setupForm;
        private TextBox? adminBox;
        private TextBox? pcBox;
        private Button? startButton;

        private bool lockScreenReady;
        private PictureBox? qrPictureBox;
        private OfflineSurveyPanel? surveyPanel;

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
            setupForm = new Form
            {
                Text = "Sørby Gaming - PC opsætning",
                Size = new Size(450, 300),
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                BackColor = Color.FromArgb(30, 30, 30)
            };

            Label title = new Label
            {
                Text = "SØRBY GAMING",
                ForeColor = Color.White,
                Font = new Font("Arial", 22, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(120, 25)
            };

            Label instruction = new Label
            {
                Text = "Indtast admin-koden:",
                ForeColor = Color.White,
                Font = new Font("Arial", 12),
                AutoSize = true,
                Location = new Point(125, 70)
            };

            adminBox = new TextBox
            {
                Location = new Point(100, 100),
                Width = 250,
                UseSystemPasswordChar = true,
                Font = new Font("Arial", 14)
            };

            Label pcSetupLabel = new Label
            {
                Text = "PC-nummer:",
                ForeColor = Color.White,
                Font = new Font("Arial", 12),
                AutoSize = true,
                Location = new Point(100, 140)
            };

            pcBox = new TextBox
            {
                Location = new Point(200, 137),
                Width = 150,
                Font = new Font("Arial", 14),
                Text = "01"
            };

            startButton = new Button
            {
                Text = "START",
                Location = new Point(145, 190),
                Width = 160,
                Height = 35
            };

            startButton.Click += StartButton_Click;

            setupForm.Controls.Add(title);
            setupForm.Controls.Add(instruction);
            setupForm.Controls.Add(adminBox);
            setupForm.Controls.Add(pcSetupLabel);
            setupForm.Controls.Add(pcBox);
            setupForm.Controls.Add(startButton);

            setupForm.AcceptButton = startButton;

            DialogResult setupResult = setupForm.ShowDialog(this);

            // Hvis opsætningen lukkes med Alt+F4 eller vinduets luk-knap,
            // skal den skjulte hovedformular også afsluttes.
            if (setupResult != DialogResult.OK && !IsDisposed)
            {
                Close();
            }
        }

        private async void StartButton_Click(object? sender, EventArgs e)
        {
            if (adminBox == null || pcBox == null || setupForm == null || startButton == null)
            {
                return;
            }

            string enteredPcId = pcBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(enteredPcId))
            {
                MessageBox.Show(setupForm, "Indtast et PC-nummer.", "Sørby Gaming",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            startButton.Enabled = false;
            string? adminError = await AdminCodeVerifier.VerifyAsync(ServerUrl, adminBox.Text, enteredPcId);
            startButton.Enabled = true;

            if (adminError != null && !OfferFirstOfflineCode(adminBox.Text))
            {
                MessageBox.Show(setupForm, adminError, "Sørby Gaming",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);

                adminBox.Clear();
                adminBox.Focus();
                return;
            }

            PcId = enteredPcId;
            LocalStore.SavePcId(enteredPcId);

            startButton.Enabled = false;
            adminBox.Enabled = false;
            pcBox.Enabled = false;

            StartConnection();
        }

        // En PC, der aldrig har været online, har ingen gemt admin-kode.
        // Så kan den indtastede kode gøres til PC'ens offline-kode. Den
        // erstattes automatisk af serverens kode ved næste online-login.
        private bool OfferFirstOfflineCode(string code)
        {
            if (!AdminCodeVerifier.LastCheckWasOffline || AdminCodeVerifier.HasOfflineCode || setupForm == null)
            {
                return false;
            }

            if (code.Length < 4)
            {
                MessageBox.Show(setupForm, "Offline-koden skal være mindst 4 tegn.", "Sørby Gaming",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            DialogResult answer = MessageBox.Show(
                setupForm,
                "Der er ingen internetforbindelse, og der er ikke gemt en admin-kode på denne PC.\n\n" +
                "Vil du bruge den indtastede kode som admin-kode på denne PC, indtil den kommer online?",
                "Sørby Gaming",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (answer != DialogResult.Yes)
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

                    MessageBox.Show(setupForm,
                        $"PC {PcId} er allerede registreret.\n\nVælg et andet PC-nummer.",
                        "Sørby Gaming", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    if (adminBox != null) adminBox.Enabled = true;
                    if (startButton != null) startButton.Enabled = true;
                    if (pcBox != null)
                    {
                        pcBox.Enabled = true;
                        pcBox.Focus();
                        pcBox.SelectAll();
                    }
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
            BackgroundImageLayout = ImageLayout.Zoom;

            // Al tekst står på eventets baggrundsbillede,
            // så skærmen viser kun QR-koden.
            qrPictureBox = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                Size = new Size(300, 300)
            };

            surveyPanel = new OfflineSurveyPanel { Visible = false };
            surveyPanel.StartRequested += StartOfflineSession;

            Controls.Add(qrPictureBox);
            Controls.Add(surveyPanel);

            Resize += (sender, e) => PositionOverlay();
            surveyPanel.SizeChanged += (sender, e) => PositionOverlay();
        }

        // Online med QR-kode: vis QR. Ellers: vis spørgeskemaet på skærmen.
        private void RefreshLockScreen()
        {
            if (!lockScreenReady || qrPictureBox == null || surveyPanel == null || inSession)
            {
                return;
            }

            bool showQr = isOnline && QrUrl != null;

            qrPictureBox.Visible = showQr;
            surveyPanel.Visible = !showQr;

            if (!showQr)
            {
                surveyPanel.Build(
                    cachedEvent?.Name,
                    cachedEvent?.Questions.Count > 0 ? cachedEvent.Questions : LocalStore.DefaultQuestions(),
                    OfflineDurationSeconds() / 60
                );

                SetBackgroundImage(LocalStore.LoadOfflineBackground());
            }

            PositionOverlay();
        }

        private void PositionOverlay()
        {
            foreach (Control? control in new Control?[] { qrPictureBox, surveyPanel })
            {
                if (control == null)
                {
                    continue;
                }

                control.Left = (ClientSize.Width - control.Width) / 2;
                control.Top = (ClientSize.Height - control.Height) / 2;
            }
        }

        // ---------------------------------------------------------
        // BAGGRUND OG QR-KODE
        // ---------------------------------------------------------

        private async Task SetOnlineBackgroundAsync(string? backgroundImageUrl)
        {
            if (string.IsNullOrWhiteSpace(backgroundImageUrl))
            {
                LocalStore.SaveEventBackground(null);

                if (isOnline)
                {
                    SetBackgroundImage(null);
                }

                return;
            }

            try
            {
                byte[] imageBytes = await HttpClient.GetByteArrayAsync(backgroundImageUrl);

                if (cachedEvent != null)
                {
                    LocalStore.SaveEventBackground(imageBytes);
                }

                if (isOnline)
                {
                    SetBackgroundImage(imageBytes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke hente eventbaggrund: {ex.Message}");
            }
        }

        private void SetBackgroundImage(byte[]? imageBytes)
        {
            Image? oldImage = BackgroundImage;

            try
            {
                if (imageBytes == null)
                {
                    BackgroundImage = null;
                }
                else
                {
                    using MemoryStream stream = new MemoryStream(imageBytes);
                    using Image temporaryImage = Image.FromStream(stream);
                    BackgroundImage = new Bitmap(temporaryImage);
                }

                oldImage?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke vise baggrund: {ex.Message}");
            }
        }

        private void UpdateQrCode(string qrUrl)
        {
            QrUrl = qrUrl;

            if (qrPictureBox == null)
            {
                return;
            }

            try
            {
                using QRCodeGenerator qrGenerator = new QRCodeGenerator();
                using QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrUrl, QRCodeGenerator.ECCLevel.Q);
                PngByteQRCode qrCode = new PngByteQRCode(qrCodeData);

                using MemoryStream stream = new MemoryStream(qrCode.GetGraphic(20));
                using Image tempImage = Image.FromStream(stream);

                Image? oldImage = qrPictureBox.Image;
                qrPictureBox.Image = new Bitmap(tempImage);
                oldImage?.Dispose();

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

            if (remainingSeconds <= 0)
            {
                await EndSession();
            }
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
            using Form loginForm = new Form
            {
                Text = "Sørby Gaming - Admin",
                Size = new Size(450, 260),
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                BackColor = Color.FromArgb(30, 30, 30)
            };

            Label title = new Label
            {
                Text = "ADMIN",
                ForeColor = Color.White,
                Font = new Font("Arial", 22, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(170, 25)
            };

            Label instruction = new Label
            {
                Text = "Indtast admin-koden:",
                ForeColor = Color.White,
                Font = new Font("Arial", 12),
                AutoSize = true,
                Location = new Point(125, 70)
            };

            TextBox passwordBox = new TextBox
            {
                Location = new Point(100, 105),
                Width = 250,
                UseSystemPasswordChar = true,
                Font = new Font("Arial", 14)
            };

            Button stopButton = new Button
            {
                Text = "STOP EVENT",
                Location = new Point(45, 155),
                Width = 160,
                Height = 35
            };

            Button settingsButton = new Button
            {
                Text = "OFFLINE-INDSTILLINGER",
                Location = new Point(220, 155),
                Width = 180,
                Height = 35
            };

            bool openSettings = false;

            async Task VerifyAndClose(bool settings)
            {
                stopButton.Enabled = false;
                settingsButton.Enabled = false;
                string? adminError = await AdminCodeVerifier.VerifyAsync(ServerUrl, passwordBox.Text, PcId);
                stopButton.Enabled = true;
                settingsButton.Enabled = true;

                if (adminError != null)
                {
                    MessageBox.Show(loginForm, adminError, "Sørby Gaming",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    passwordBox.Clear();
                    passwordBox.Focus();
                    return;
                }

                openSettings = settings;
                loginForm.DialogResult = DialogResult.OK;
                loginForm.Close();
            }

            stopButton.Click += async (sender, e) => await VerifyAndClose(false);
            settingsButton.Click += async (sender, e) => await VerifyAndClose(true);

            loginForm.Controls.Add(title);
            loginForm.Controls.Add(instruction);
            loginForm.Controls.Add(passwordBox);
            loginForm.Controls.Add(stopButton);
            loginForm.Controls.Add(settingsButton);

            loginForm.AcceptButton = stopButton;
            passwordBox.Focus();

            if (loginForm.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            if (!openSettings)
            {
                Application.Exit();
                return;
            }

            using OfflineSettingsForm settingsForm = new OfflineSettingsForm(cachedEvent);
            settingsForm.ShowDialog(this);

            // Baggrund kan være ændret, selv om dialogen blev lukket uden at gemme.
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

            if (qrPictureBox != null)
            {
                qrPictureBox.Image?.Dispose();
                qrPictureBox.Image = null;
            }

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
