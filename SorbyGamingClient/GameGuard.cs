using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private List<EventGame> games = new List<EventGame>();
        private List<string> gameFolders = new List<string>();
        private GameMenuForm? menu;
        private DateTime graceUntil;
        private bool running;

        // Sættes, mens admin-menuen er åben, så vagten ikke dækker den.
        public bool Paused { get; set; }

        public bool IsRunning => running;

        public GameGuard()
        {
            timer.Tick += (sender, e) => Check();
        }

        // Returnerer false, hvis eventet ikke har spil, der findes på denne PC.
        public bool Start(List<EventGame> eventGames)
        {
            Stop(closeGames: false);

            games = eventGames;
            List<EventGame> available = games.Where(game => File.Exists(game.Path)).ToList();

            foreach (EventGame missing in games.Except(available))
            {
                Console.WriteLine($"Spillet {missing.Name} findes ikke på denne PC: {missing.Path}");
            }

            if (available.Count == 0)
            {
                // Uden et spil, der kan startes, låses PC'en ikke til spil.
                return false;
            }

            gameFolders = available
                .Select(game => Path.GetDirectoryName(game.Path))
                .Where(folder => folder != null && IsSafeGameFolder(folder))
                .Select(folder => EnsureTrailingSlash(folder!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            running = true;

            if (available.Count == 1)
            {
                Launch(available[0]);
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
            timer.Stop();

            if (menu != null)
            {
                menu.GameChosen -= Launch;
                menu.ForceClose();
                menu.Dispose();
                menu = null;
            }

            if (closeGames && wasRunning)
            {
                CloseGames(games.Select(game => game.Path).ToList(), gameFolders.ToList());
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

            if (games.Any(game => string.Equals(game.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (gameFolders.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return HelperProcesses.Contains(Path.GetFileNameWithoutExtension(path));
        }

        private void ShowMenu()
        {
            if (menu == null)
            {
                menu = new GameMenuForm(games);
                menu.GameChosen += Launch;
            }

            menu.Cover();
        }

        // ---------------------------------------------------------
        // START OG LUK SPIL
        // ---------------------------------------------------------

        private void Launch(EventGame game)
        {
            menu?.Hide();
            graceUntil = DateTime.Now + LaunchGrace;

            try
            {
                Process? existing = FindRunning(game.Path);

                if (existing != null)
                {
                    Console.WriteLine($"{game.Name} kører allerede - hentes frem.");
                    graceUntil = DateTime.Now + SwitchGrace;
                    BringToFront(existing);
                    return;
                }

                Console.WriteLine($"Starter {game.Name}: {game.Path}");
                Process.Start(new ProcessStartInfo(game.Path)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(game.Path) ?? ""
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke starte {game.Name}: {ex.Message}");
                graceUntil = DateTime.MinValue;
                ShowMenu();
                menu?.ShowError($"{game.Name} kunne ikke startes.");
            }
        }

        private static Process? FindRunning(string exePath)
        {
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)))
            {
                if (string.Equals(ImagePath((uint)process.Id), exePath, StringComparison.OrdinalIgnoreCase))
                {
                    return process;
                }

                process.Dispose();
            }

            return null;
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
        private static void CloseGames(List<string> exePaths, List<string> folders)
        {
            int ownId = Environment.ProcessId;

            _ = Task.Run(() =>
            {
                foreach (Process process in Process.GetProcesses())
                {
                    try
                    {
                        if (process.Id == ownId)
                        {
                            continue;
                        }

                        string? path = ImagePath((uint)process.Id);

                        if (path == null)
                        {
                            continue;
                        }

                        bool isGame = exePaths.Any(exe => string.Equals(exe, path, StringComparison.OrdinalIgnoreCase)) ||
                                      folders.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase));

                        if (isGame)
                        {
                            Console.WriteLine($"Lukker {path}");
                            process.Kill(entireProcessTree: true);
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
