using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    // Under en session må kun eventets spil bruges. Har eventet ét spil,
    // startes det automatisk; har det flere, vælger spilleren i spilmenuen.
    // Kommer et andet program forrest (skrivebord, browser, Stifinder ...),
    // lægger spilmenuen sig hen over det. Når sessionen slutter, lukkes spillet.
    //
    // Det er en "blød" lås: den forhindrer almindelig brug af andre
    // programmer, men er ikke en sikkerhedsgrænse som Windows' egen kiosk-tilstand.
    internal sealed class GameGuard : IDisposable
    {
        // Spillet får lidt tid til at starte, før vagten reagerer.
        private static readonly TimeSpan LaunchGrace = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan SwitchGrace = TimeSpan.FromSeconds(4);

        // "Starter spillet"-skærmen fjernes, når spillets vindue har været
        // fremme et øjeblik. Et stort vindue (selve spillet) venter kort; et
        // lille vindue (fx en splash-skærm eller anti-cheat) venter længere.
        private static readonly TimeSpan LargeWindowSettle = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan SmallWindowSettle = TimeSpan.FromSeconds(20);
        // Starter spillet slet ikke, fjernes skærmen alligevel efter denne tid.
        private static readonly TimeSpan StartingTimeout = TimeSpan.FromSeconds(120);
        // Har spillet været lukket så længe, kommer spilmenuen frem igen.
        private static readonly TimeSpan ClosedBeforeMenu = TimeSpan.FromSeconds(3);

        // Hjælpeprogrammer, som spil ofte åbner (launchere, anti-cheat, crash-rapporter).
        private static readonly HashSet<string> HelperProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "steam", "steamwebhelper", "steamservice", "gameoverlayui", "gameoverlayui64",
            "epicgameslauncher", "epicwebhelper", "easyanticheat", "easyanticheat_eos", "easyanticheat_eos_setup",
            "beservice", "beservice_x64", "battleye launcher", "unitycrashhandler64", "unitycrashhandler32",
            "crashreportclient", "riotclientservices", "riotclientux", "eadesktop", "eabackgroundservice",
            "ubisoftconnect", "upc", "battle.net"
        };

        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
        private List<PlayableGame> games = new List<PlayableGame>();
        private List<string> gameFolders = new List<string>();
        private List<string> platformFolders = new List<string>();
        private GameMenuForm? menu;
        private DateTime graceUntil;
        private bool running;
        private PlayableGame? starting;
        private DateTime startingUntil;
        private DateTime windowSeenSince = DateTime.MinValue;
        // Spillet, der sidst blev startet, og hvornår det sidst kørte.
        private PlayableGame? currentGame;
        private DateTime currentGameSeen;
        private int? eventId;

        // Sættes, mens admin-menuen er åben, så vagten ikke dækker den.
        public bool Paused { get; set; }

        public bool IsRunning => running;

        // Er spilmenuen eller "Starter spillet"-skærmen fremme?
        public bool MenuVisible => menu != null && menu.Visible;

        public GameGuard()
        {
            timer.Tick += (sender, e) => Check();
        }

        // Returnerer false, hvis ingen af eventets spil findes på denne PC.
        public bool Start(List<PlayableGame> playable, int? eventId = null)
        {
            Stop(closeGames: false);
            this.eventId = eventId;

            if (playable.Count == 0)
            {
                return false;
            }

            games = playable;
            gameFolders = games
                .SelectMany(game => game.Installed.GameFolders)
                .Where(IsSafeGameFolder)
                .Select(EnsureTrailingSlash)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            platformFolders = games
                .SelectMany(game => game.Installed.PlatformFolders)
                .Where(IsSafeGameFolder)
                .Select(EnsureTrailingSlash)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            running = true;

            // Programmet kobler sig aldrig på spillenes tråde (kun egne vinduer).
            List<string> folders = gameFolders.ToList();
            NativeWindowHelper.NoAttachProcess = processId =>
            {
                string? path = ImagePath(processId);
                return path != null && folders.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
            };

            if (games.Count == 1)
            {
                Launch(games[0]);
            }
            else
            {
                ShowMenu();
            }

            timer.Start();
            return true;
        }

        public void Stop(bool closeGames)
        {
            bool wasRunning = running;
            running = false;
            starting = null;
            currentGame = null;
            timer.Stop();
            ActivityLog.End("game");
            NativeWindowHelper.NoAttachProcess = null;

            if (menu != null)
            {
                menu.GameChosen -= Launch;
                menu.ForceClose();
                menu.Dispose();
                menu = null;
            }

            if (closeGames && wasRunning)
            {
                CloseGames(gameFolders.ToList());
            }
        }

        // ---------------------------------------------------------
        // VAGTEN
        // ---------------------------------------------------------

        private void Check()
        {
            if (!running || Paused)
            {
                return;
            }

            if (starting != null)
            {
                CheckStarting();
                return;
            }

            if (CheckGameClosed())
            {
                return;
            }

            IntPtr foreground = GetForegroundWindow();

            if (foreground == IntPtr.Zero)
            {
                return;
            }

            uint processId = ProcessIdOf(foreground);
            bool ownWindow = processId == (uint)Environment.ProcessId;

            if (menu != null && menu.Visible)
            {
                // Spilleren har selv skiftet tilbage til spillet (fx Alt+Tab).
                if (!ownWindow && IsAllowed(processId))
                {
                    menu.Hide();
                    graceUntil = DateTime.Now + SwitchGrace;
                    return;
                }

                if (!ownWindow)
                {
                    menu.Cover();
                }

                return;
            }

            if (ownWindow || DateTime.Now < graceUntil || IsAllowed(processId))
            {
                return;
            }

            Console.WriteLine($"Ikke tilladt program forrest ({ImagePath(processId) ?? "ukendt"}) - viser spilmenuen.");
            ShowMenu();
        }

        private bool IsAllowed(uint processId)
        {
            string? path = ImagePath(processId);

            // Kan stien ikke læses (beskyttet systemproces), gribes der ikke ind.
            if (path == null)
            {
                return true;
            }

            if (gameFolders.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Platformens egne programmer (Steam, Epic-launcheren, Riot Client)
            // er tilladt, men ikke andre spil, der ligger i platformens mappe.
            bool otherGame = path.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase);

            if (!otherGame && platformFolders.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return HelperProcesses.Contains(Path.GetFileNameWithoutExtension(path));
        }

        private void ShowMenu()
        {
            EnsureMenu().ShowChoices();
            menu!.Cover();
        }

        private GameMenuForm EnsureMenu()
        {
            if (menu == null)
            {
                menu = new GameMenuForm(games);
                menu.GameChosen += Launch;
            }

            return menu;
        }

        // Mens spillet starter, ligger "Starter spillet"-skærmen over alt.
        // Den tager ikke fokus fra spillet, men dækker skærmen, så der ikke
        // kan klikkes rundt. Når spillets vindue er klar, fjernes den.
        private void CheckStarting()
        {
            PlayableGame game = starting!;
            (IntPtr window, bool large) = FindGameWindow(game.Installed.GameFolders);
            DateTime now = DateTime.Now;

            if (window == IntPtr.Zero)
            {
                windowSeenSince = DateTime.MinValue;
            }
            else
            {
                if (windowSeenSince == DateTime.MinValue)
                {
                    windowSeenSince = now;
                }

                if (now - windowSeenSince >= (large ? LargeWindowSettle : SmallWindowSettle))
                {
                    Console.WriteLine($"{game.Name} er startet - fjerner startskærmen.");
                    FinishStarting(window);
                    return;
                }
            }

            if (now > startingUntil)
            {
                Console.WriteLine($"{game.Name} viste ikke et vindue i tide - fjerner startskærmen.");
                FinishStarting(IntPtr.Zero);
                return;
            }

            menu?.KeepOnTop();
        }

        // Lukker spilleren spillet (eller går det ned), kommer spilmenuen frem,
        // så der kan vælges et andet spil. Imens spillet kører, måles tiden i det.
        private bool CheckGameClosed()
        {
            if (currentGame == null)
            {
                return false;
            }

            if (IsAnyRunning(currentGame.Installed.GameFolders))
            {
                currentGameSeen = DateTime.Now;
                ActivityLog.Begin("game", eventId, currentGame.Name, currentGame.Installed.Platform);
                return false;
            }

            if (DateTime.Now - currentGameSeen < ClosedBeforeMenu)
            {
                return false;
            }

            Console.WriteLine($"{currentGame.Name} er lukket - viser spilmenuen.");
            currentGame = null;
            ActivityLog.End("game");
            ShowMenu();
            return true;
        }

        private static bool IsAnyRunning(List<string> folders)
        {
            List<string> prefixes = folders.Select(EnsureTrailingSlash).ToList();
            bool found = false;

            foreach (Process process in Process.GetProcesses())
            {
                if (!found && process.Id != Environment.ProcessId)
                {
                    string? path = ImagePath((uint)process.Id);
                    found = path != null && prefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                }

                process.Dispose();
            }

            return found;
        }

        private void SetCurrentGame(PlayableGame game)
        {
            currentGame = game;
            currentGameSeen = DateTime.Now;
            ActivityLog.Begin("game", eventId, game.Name, game.Installed.Platform);
        }

        private void FinishStarting(IntPtr window)
        {
            if (starting != null)
            {
                SetCurrentGame(starting);
            }

            starting = null;
            windowSeenSince = DateTime.MinValue;
            graceUntil = DateTime.Now + SwitchGrace;
            menu?.ShowChoices();
            menu?.Hide();

            if (window != IntPtr.Zero)
            {
                if (IsIconic(window))
                {
                    ShowWindow(window, SW_RESTORE);
                }

                NativeWindowHelper.ForceForeground(window);
            }
        }

        // Spillets synlige hovedvindue, og om det fylder det meste af skærmen.
        private static (IntPtr Window, bool Large) FindGameWindow(List<string> folders)
        {
            List<string> prefixes = folders.Select(EnsureTrailingSlash).ToList();
            IntPtr best = IntPtr.Zero;
            bool bestLarge = false;

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    string? path = ImagePath((uint)process.Id);

                    if (path == null || !prefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    IntPtr window = process.MainWindowHandle;

                    if (window == IntPtr.Zero || !IsWindowVisible(window) || !GetWindowRect(window, out RECT rect))
                    {
                        continue;
                    }

                    Rectangle screen = Screen.FromHandle(window).Bounds;
                    bool large = rect.Right - rect.Left >= screen.Width * 0.6 && rect.Bottom - rect.Top >= screen.Height * 0.6;

                    if (best == IntPtr.Zero || (large && !bestLarge))
                    {
                        best = window;
                        bestLarge = large;
                    }
                }
                catch
                {
                    // Processen er lukket imens - spring den over.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return (best, bestLarge);
        }

        // ---------------------------------------------------------
        // START OG LUK SPIL
        // ---------------------------------------------------------

        private void Launch(PlayableGame game)
        {
            graceUntil = DateTime.Now + LaunchGrace;

            try
            {
                Process? existing = FindRunning(game.Installed.GameFolders);

                if (existing != null)
                {
                    Console.WriteLine($"{game.Name} kører allerede - hentes frem.");
                    SetCurrentGame(game);
                    menu?.Hide();
                    graceUntil = DateTime.Now + SwitchGrace;
                    BringToFront(existing);
                    return;
                }

                // Startskærmen kommer frem, før spillet startes, så skrivebordet
                // aldrig er fremme imens.
                EnsureMenu().ShowLoading(game.Name);
                menu!.Cover();
                starting = game;
                startingUntil = DateTime.Now + StartingTimeout;
                windowSeenSince = DateTime.MinValue;

                Console.WriteLine($"Starter {game.Name}: {game.Installed.LaunchTarget} {game.Installed.LaunchArguments}");
                Process.Start(game.Installed.StartInfo());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke starte {game.Name}: {ex.Message}");
                starting = null;
                graceUntil = DateTime.MinValue;
                ShowMenu();
                menu?.ShowError($"{game.Name} kunne ikke startes.");
            }
        }

        // En kørende proces fra spillets mapper, der har et vindue.
        private static Process? FindRunning(List<string> folders)
        {
            List<string> prefixes = folders.Select(EnsureTrailingSlash).ToList();
            Process? found = null;

            foreach (Process process in Process.GetProcesses())
            {
                if (found == null && process.Id != Environment.ProcessId)
                {
                    string? path = ImagePath((uint)process.Id);

                    if (path != null && prefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
                        process.MainWindowHandle != IntPtr.Zero)
                    {
                        found = process;
                        continue;
                    }
                }

                process.Dispose();
            }

            return found;
        }

        private static void BringToFront(Process process)
        {
            IntPtr window = process.MainWindowHandle;

            if (window == IntPtr.Zero)
            {
                return;
            }

            if (IsIconic(window))
            {
                ShowWindow(window, SW_RESTORE);
            }

            NativeWindowHelper.ForceForeground(window);
        }

        // Lukker spillene (og programmer fra spillenes mapper) i baggrunden.
        // Først bedes spillet lukke pænt (som når man trykker på krydset), så
        // det kan gemme og melde sig af. Kun hvis det stadig kører efter et par
        // sekunder, stoppes det. Anti-cheat-programmer stoppes aldrig.
        private static void CloseGames(List<string> folders)
        {
            int ownId = Environment.ProcessId;

            _ = Task.Run(async () =>
            {
                List<Process> games = new List<Process>();

                foreach (Process process in Process.GetProcesses())
                {
                    string? path = process.Id == ownId ? null : ImagePath((uint)process.Id);

                    if (path != null && folders.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) &&
                        !IsAntiCheat(path))
                    {
                        games.Add(process);
                    }
                    else
                    {
                        process.Dispose();
                    }
                }

                foreach (Process process in games)
                {
                    try
                    {
                        if (process.CloseMainWindow())
                        {
                            Console.WriteLine($"Beder {process.ProcessName} om at lukke.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Kunne ikke bede et spil om at lukke: {ex.Message}");
                    }
                }

                DateTime deadline = DateTime.Now + GracefulClose;

                while (DateTime.Now < deadline && games.Any(IsAlive))
                {
                    await Task.Delay(250);
                }

                foreach (Process process in games)
                {
                    try
                    {
                        if (IsAlive(process))
                        {
                            Console.WriteLine($"Lukker {process.ProcessName}");
                            process.Kill(entireProcessTree: false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Kunne ikke lukke et spil: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            });
        }

        private static readonly TimeSpan GracefulClose = TimeSpan.FromSeconds(5);

        private static bool IsAlive(Process process)
        {
            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        // Anti-cheat ligger ofte i spillets mappe; den skal have lov at lukke sig selv.
        private static bool IsAntiCheat(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name.Contains("anticheat", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("BE", StringComparison.Ordinal) && name.Contains("Service", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("EAC", StringComparison.OrdinalIgnoreCase) ||
                path.Contains(@"\EasyAntiCheat", StringComparison.OrdinalIgnoreCase) ||
                path.Contains(@"\BattlEye", StringComparison.OrdinalIgnoreCase);
        }

        // Mapper, hvor alt indhold trygt kan regnes for "spillet". Ligger en
        // .exe direkte i fx C:\ eller C:\Windows, bruges kun selve filen.
        private static bool IsSafeGameFolder(string folder)
        {
            string full = EnsureTrailingSlash(Path.GetFullPath(folder));
            string? root = Path.GetPathRoot(full);

            if (root == null || string.Equals(full, EnsureTrailingSlash(root), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string windows = EnsureTrailingSlash(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

            if (full.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] broadFolders =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            };

            return !broadFolders
                .Where(item => !string.IsNullOrEmpty(item))
                .Any(item => string.Equals(full, EnsureTrailingSlash(item), StringComparison.OrdinalIgnoreCase));
        }

        private static string EnsureTrailingSlash(string folder) =>
            folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;

        public void Dispose()
        {
            Stop(closeGames: false);
            timer.Dispose();
        }

        // ---------------------------------------------------------
        // WINDOWS
        // ---------------------------------------------------------

        private const int SW_RESTORE = 9;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private static uint ProcessIdOf(IntPtr window)
        {
            GetWindowThreadProcessId(window, out uint processId);
            return processId;
        }

        // Fuld sti til programmet bag en proces. Virker også for processer,
        // der kører som administrator, fordi der kun bedes om begrænset adgang.
        private static string? ImagePath(uint processId)
        {
            if (processId == 0)
            {
                return null;
            }

            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);

            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                StringBuilder buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
