using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SorbyGamingClient
{
    // Et spil, der er installeret på denne PC.
    internal sealed class InstalledGame
    {
        public string Platform { get; init; } = "";
        public string Name { get; init; } = "";

        // Hvordan spillet startes (fx steam://rungameid/730 eller en .exe).
        public string LaunchTarget { get; init; } = "";
        public string LaunchArguments { get; init; } = "";

        // Spillets egne mapper: tilladt under sessionen og lukkes ved slut.
        public List<string> GameFolders { get; init; } = new List<string>();

        // Platformens mapper (launcher osv.): tilladt, men lukkes ikke.
        public List<string> PlatformFolders { get; init; } = new List<string>();

        public ProcessStartInfo StartInfo() => new ProcessStartInfo(LaunchTarget, LaunchArguments) { UseShellExecute = true };
    }

    // Et af eventets spil, som findes på denne PC.
    internal sealed class PlayableGame
    {
        public string Name { get; init; } = "";
        public InstalledGame Installed { get; init; } = new InstalledGame();
    }

    // Finder installerede spil på Steam, Epic Games, Riot, Minecraft og Roblox
    // ved at læse platformenes egne lister. Kræver ikke internet.
    internal static class GameLibrary
    {
        public static List<InstalledGame> Scan()
        {
            List<InstalledGame> games = new List<InstalledGame>();

            void Try(string platform, Action<List<InstalledGame>> scan)
            {
                try
                {
                    scan(games);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Kunne ikke søge efter {platform}-spil: {ex.Message}");
                }
            }

            Try("Steam", ScanSteam);
            Try("Epic", ScanEpic);
            Try("Riot", ScanRiot);
            Try("Minecraft", ScanMinecraft);
            Try("Roblox", ScanRoblox);
            return games;
        }

        // Eventets spil, der findes på denne PC, i eventets rækkefølge.
        public static List<PlayableGame> Match(IEnumerable<EventGame>? eventGames, List<InstalledGame>? installed = null)
        {
            List<PlayableGame> result = new List<PlayableGame>();

            if (eventGames == null)
            {
                return result;
            }

            installed ??= Scan();

            foreach (EventGame wanted in eventGames)
            {
                foreach (InstalledGame game in FindMatches(wanted, installed))
                {
                    if (result.Any(item => item.Installed == game))
                    {
                        continue;
                    }

                    // Hele platforme (Minecraft, Roblox) bruger det installerede navn.
                    bool wholePlatform = IsWholePlatform(wanted.Platform);
                    result.Add(new PlayableGame { Name = wholePlatform ? game.Name : wanted.Name, Installed = game });
                }
            }

            return result;
        }

        public static List<InstalledGame> FindMatches(EventGame wanted, List<InstalledGame> installed)
        {
            string platform = (wanted.Platform ?? "").ToLowerInvariant();
            List<InstalledGame> candidates = installed.Where(game => game.Platform == platform).ToList();

            if (IsWholePlatform(platform))
            {
                return candidates;
            }

            string name = Normalize(wanted.Name);

            if (name.Length == 0)
            {
                return new List<InstalledGame>();
            }

            // Præcist navn først; ellers et navn, der indeholder det andet.
            List<InstalledGame> exact = candidates.Where(game => Normalize(game.Name) == name).ToList();

            if (exact.Count > 0)
            {
                return exact.Take(1).ToList();
            }

            return candidates
                .Where(game =>
                {
                    string other = Normalize(game.Name);
                    return Math.Min(other.Length, name.Length) >= 5 && (other.Contains(name) || name.Contains(other));
                })
                .OrderBy(game => Math.Abs(Normalize(game.Name).Length - name.Length))
                .Take(1)
                .ToList();
        }

        private static bool IsWholePlatform(string? platform) => platform is "minecraft" or "roblox";

        private static string Normalize(string? value) =>
            Regex.Replace((value ?? "").ToLowerInvariant().Replace("&", "and"), "[^a-z0-9æøå]", "");

        // ---------------------------------------------------------
        // STEAM
        // ---------------------------------------------------------

        private static void ScanSteam(List<InstalledGame> games)
        {
            string? steamRoot = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;

            if (string.IsNullOrWhiteSpace(steamRoot) || !Directory.Exists(steamRoot))
            {
                return;
            }

            steamRoot = Path.GetFullPath(steamRoot);
            List<string> libraries = new List<string> { steamRoot };
            string libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");

            if (File.Exists(libraryFile))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(libraryFile), "\"path\"\\s+\"(.+?)\""))
                {
                    libraries.Add(Path.GetFullPath(match.Groups[1].Value.Replace(@"\\", @"\")));
                }
            }

            foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string steamApps = Path.Combine(library, "steamapps");

                if (!Directory.Exists(steamApps))
                {
                    continue;
                }

                foreach (string manifest in Directory.GetFiles(steamApps, "appmanifest_*.acf"))
                {
                    string text = File.ReadAllText(manifest);
                    string? appId = VdfValue(text, "appid");
                    string? name = VdfValue(text, "name");
                    string? installDir = VdfValue(text, "installdir");

                    if (appId == null || name == null || installDir == null)
                    {
                        continue;
                    }

                    string folder = Path.Combine(steamApps, "common", installDir);

                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    games.Add(new InstalledGame
                    {
                        Platform = "steam",
                        Name = name,
                        LaunchTarget = $"steam://rungameid/{appId}",
                        GameFolders = new List<string> { folder },
                        PlatformFolders = new List<string> { steamRoot }
                    });
                }
            }
        }

        private static string? VdfValue(string text, string key)
        {
            Match match = Regex.Match(text, $"\"{key}\"\\s+\"(.*?)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        // ---------------------------------------------------------
        // EPIC GAMES
        // ---------------------------------------------------------

        private static void ScanEpic(List<InstalledGame> games)
        {
            string manifests = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (!Directory.Exists(manifests))
            {
                return;
            }

            List<string> launcherFolders = new List<string>();
            string launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games", "Launcher");

            if (Directory.Exists(launcher))
            {
                launcherFolders.Add(launcher);
            }

            foreach (string file in Directory.GetFiles(manifests, "*.item"))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                JsonElement root = document.RootElement;

                string Text(string property) =>
                    root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

                bool isGame = root.TryGetProperty("AppCategories", out JsonElement categories) &&
                              categories.ValueKind == JsonValueKind.Array &&
                              categories.EnumerateArray().Any(item => item.GetString() == "games");
                string folder = Text("InstallLocation");

                if (!isGame || folder.Length == 0 || !Directory.Exists(folder))
                {
                    continue;
                }

                string id = Uri.EscapeDataString($"{Text("CatalogNamespace")}:{Text("CatalogItemId")}:{Text("AppName")}");

                games.Add(new InstalledGame
                {
                    Platform = "epic",
                    Name = Text("DisplayName"),
                    LaunchTarget = $"com.epicgames.launcher://apps/{id}?action=launch&silent=true",
                    GameFolders = new List<string> { folder },
                    PlatformFolders = launcherFolders
                });
            }
        }

        // ---------------------------------------------------------
        // RIOT GAMES (League of Legends, Teamfight Tactics, VALORANT ...)
        // ---------------------------------------------------------

        private static readonly (string Product, string Name, string Folder)[] RiotProducts =
        {
            ("league_of_legends", "League of Legends", "League of Legends"),
            ("teamfighttactics", "Teamfight Tactics", "Teamfight Tactics"),
            ("valorant", "VALORANT", "VALORANT"),
            ("bacon", "Legends of Runeterra", "LoR")
        };

        private static void ScanRiot(List<InstalledGame> games)
        {
            string installs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games", "RiotClientInstalls.json");

            if (!File.Exists(installs))
            {
                return;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(installs));
            string? client = document.RootElement.TryGetProperty("rc_default", out JsonElement value) ? value.GetString() : null;

            if (string.IsNullOrWhiteSpace(client) || !File.Exists(client))
            {
                return;
            }

            client = Path.GetFullPath(client);
            string clientFolder = Path.GetDirectoryName(client)!;
            string riotRoot = Path.GetDirectoryName(clientFolder) ?? clientFolder;
            bool hasLeague = false;

            foreach ((string product, string name, string folderName) in RiotProducts)
            {
                string folder = Path.Combine(riotRoot, folderName);

                if (!Directory.Exists(folder))
                {
                    continue;
                }

                hasLeague |= product == "league_of_legends";
                games.Add(RiotGame(name, product, client, folder, clientFolder));
            }

            // TFT kan også spilles gennem League of Legends-klienten.
            if (hasLeague && !games.Any(game => game.Platform == "riot" && game.Name == "Teamfight Tactics"))
            {
                games.Add(RiotGame("Teamfight Tactics", "league_of_legends", client,
                    Path.Combine(riotRoot, "League of Legends"), clientFolder));
            }
        }

        private static InstalledGame RiotGame(string name, string product, string client, string folder, string clientFolder) => new InstalledGame
        {
            Platform = "riot",
            Name = name,
            LaunchTarget = client,
            LaunchArguments = $"--launch-product={product} --launch-patchline=live",
            GameFolders = new List<string> { folder },
            PlatformFolders = new List<string> { clientFolder }
        };

        // ---------------------------------------------------------
        // MINECRAFT (Bedrock fra Microsoft Store / Xbox og Java-launcheren)
        // ---------------------------------------------------------

        private static void ScanMinecraft(List<InstalledGame> games)
        {
            string? bedrock = PackageRoot("Microsoft.MinecraftUWP_");
            string xboxFolder = @"C:\XboxGames\Minecraft for Windows";

            if (bedrock != null || Directory.Exists(xboxFolder))
            {
                games.Add(new InstalledGame
                {
                    Platform = "minecraft",
                    Name = "Minecraft",
                    LaunchTarget = "minecraft:",
                    GameFolders = new[] { bedrock, xboxFolder }.Where(folder => folder != null && Directory.Exists(folder)).Select(folder => folder!).ToList()
                });
            }

            // Java Edition via Minecraft Launcher (Store-udgave eller klassisk installation).
            string? launcherPackage = PackageRoot("Microsoft.4297127D64EC6_");
            string classicLauncher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Minecraft Launcher", "MinecraftLauncher.exe");
            string dotMinecraft = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
            string localPackages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");

            List<string> javaFolders = new List<string>();

            if (Directory.Exists(dotMinecraft))
            {
                javaFolders.Add(dotMinecraft);
            }

            if (Directory.Exists(localPackages))
            {
                javaFolders.AddRange(Directory.GetDirectories(localPackages, "Microsoft.4297127D64EC6_*"));
            }

            if (launcherPackage != null)
            {
                javaFolders.Add(launcherPackage);
                games.Add(new InstalledGame
                {
                    Platform = "minecraft",
                    Name = "Minecraft: Java Edition",
                    LaunchTarget = "explorer.exe",
                    LaunchArguments = $"shell:AppsFolder\\{Path.GetFileName(PackageFamily(launcherPackage))}!Minecraft",
                    GameFolders = javaFolders
                });
            }
            else if (File.Exists(classicLauncher))
            {
                javaFolders.Add(Path.GetDirectoryName(classicLauncher)!);
                games.Add(new InstalledGame
                {
                    Platform = "minecraft",
                    Name = "Minecraft: Java Edition",
                    LaunchTarget = classicLauncher,
                    GameFolders = javaFolders
                });
            }
        }

        // ---------------------------------------------------------
        // ROBLOX
        // ---------------------------------------------------------

        private static void ScanRoblox(List<InstalledGame> games)
        {
            string robloxFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");
            string versions = Path.Combine(robloxFolder, "Versions");

            string? player = Directory.Exists(versions)
                ? Directory.GetFiles(versions, "RobloxPlayerBeta.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;

            if (player != null)
            {
                games.Add(new InstalledGame
                {
                    Platform = "roblox",
                    Name = "Roblox",
                    LaunchTarget = player,
                    GameFolders = new List<string> { robloxFolder }
                });
                return;
            }

            // Roblox fra Microsoft Store.
            string? storeRoblox = PackageRoot("ROBLOXCORPORATION.ROBLOX_");

            if (storeRoblox != null)
            {
                games.Add(new InstalledGame
                {
                    Platform = "roblox",
                    Name = "Roblox",
                    LaunchTarget = "explorer.exe",
                    LaunchArguments = $"shell:AppsFolder\\{PackageFamily(storeRoblox)}!App",
                    GameFolders = new List<string> { storeRoblox }
                });
            }
        }

        // ---------------------------------------------------------
        // MICROSOFT STORE-APPS
        // ---------------------------------------------------------

        // Mappen for en installeret Store-app, fundet i Windows' egen app-liste.
        private static string? PackageRoot(string namePrefix)
        {
            using RegistryKey? packages = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");

            if (packages == null)
            {
                return null;
            }

            foreach (string name in packages.GetSubKeyNames().Where(item => item.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)))
            {
                using RegistryKey? package = packages.OpenSubKey(name);

                if (package?.GetValue("PackageRootFolder") is string root && Directory.Exists(root))
                {
                    return root;
                }
            }

            return null;
        }

        // "Microsoft.MinecraftUWP_1.26.6501.0_x64__8wekyb3d8bbwe" -> "Microsoft.MinecraftUWP_8wekyb3d8bbwe"
        private static string PackageFamily(string packageRoot)
        {
            string fullName = Path.GetFileName(packageRoot.TrimEnd('\\'));
            string[] parts = fullName.Split('_');
            return parts.Length >= 5 ? $"{parts[0]}_{parts[^1]}" : fullName;
        }
    }
}
