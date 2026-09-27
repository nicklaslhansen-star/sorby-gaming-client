using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SorbyGamingClient
{
    // Alt, PC'en skal bruge for at køre et event uden internet, gemmes i
    // C:\ProgramData\SorbyGaming. Filerne overlever genstart af PC'en.
    internal static class LocalStore
    {
        public static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SorbyGaming"
        );

        private static readonly string EventPath = Path.Combine(Folder, "event.json");
        private static readonly string EventBackgroundPath = Path.Combine(Folder, "event-background.img");
        private static readonly string ManualPath = Path.Combine(Folder, "manual-settings.json");
        private static readonly string ManualBackgroundPath = Path.Combine(Folder, "manual-background.img");
        private static readonly string SessionsPath = Path.Combine(Folder, "offline-sessions.json");
        private static readonly string PcPath = Path.Combine(Folder, "pc.json");

        private static readonly object SessionsLock = new object();

        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public const int DefaultDurationSeconds = 10 * 60;

        // Samme standardspørgsmål som serveren bruger.
        public static List<SurveyQuestion> DefaultQuestions() => new List<SurveyQuestion>
        {
            new SurveyQuestion
            {
                Id = "age",
                Label = "Hvor gammel er du?",
                Type = "select",
                Required = true,
                Options = new List<SurveyOption>
                {
                    new SurveyOption { Value = "under-10", Label = "Under 10 år" },
                    new SurveyOption { Value = "10-12", Label = "10–12 år" },
                    new SurveyOption { Value = "13-15", Label = "13–15 år" },
                    new SurveyOption { Value = "16-17", Label = "16–17 år" },
                    new SurveyOption { Value = "18+", Label = "18+ år" }
                }
            },
            new SurveyQuestion
            {
                Id = "member",
                Label = "Er du medlem af Sørby Esport?",
                Type = "select",
                Required = true,
                Options = new List<SurveyOption>
                {
                    new SurveyOption { Value = "yes", Label = "Ja" },
                    new SurveyOption { Value = "no", Label = "Nej" }
                }
            }
        };

        // ---------------------------------------------------------
        // PC-NUMMER (bruges ved genstart efter opdatering)
        // ---------------------------------------------------------

        public static string? LoadPcId() => Read<PcInfo>(PcPath)?.PcId;

        public static void SavePcId(string pcId) => Write(PcPath, new PcInfo { PcId = pcId });

        // ---------------------------------------------------------
        // EVENT FRA SERVEREN
        // ---------------------------------------------------------

        public static CachedEvent? LoadEvent() => Read<CachedEvent>(EventPath);

        public static void SaveEvent(CachedEvent? cachedEvent)
        {
            if (cachedEvent == null)
            {
                Delete(EventPath);
                Delete(EventBackgroundPath);
                return;
            }

            Write(EventPath, cachedEvent);
        }

        public static void SaveEventBackground(byte[]? imageBytes)
        {
            if (imageBytes == null)
            {
                Delete(EventBackgroundPath);
                return;
            }

            WriteBytes(EventBackgroundPath, imageBytes);
        }

        // ---------------------------------------------------------
        // MANUELLE INDSTILLINGER (sat i admin-menuen på PC'en)
        // ---------------------------------------------------------

        public static ManualSettings LoadManual() => Read<ManualSettings>(ManualPath) ?? new ManualSettings();

        public static void SaveManual(ManualSettings settings) => Write(ManualPath, settings);

        public static void SetManualBackground(string sourceFile)
        {
            WriteBytes(ManualBackgroundPath, File.ReadAllBytes(sourceFile));
        }

        public static void ClearManualBackground() => Delete(ManualBackgroundPath);

        public static bool HasManualBackground => File.Exists(ManualBackgroundPath);

        public static byte[]? LoadManualBackground() => ReadBytes(ManualBackgroundPath);

        // Manuelt billede vinder over eventets billede, når PC'en er offline.
        public static byte[]? LoadOfflineBackground()
        {
            return ReadBytes(ManualBackgroundPath) ?? ReadBytes(EventBackgroundPath);
        }

        // Sørby-standardbaggrunden (indbygget i programmet). Bruges, når
        // hverken eventet eller PC'en har sit eget billede.
        private static byte[]? defaultBackground;

        public static byte[]? LoadDefaultBackground()
        {
            if (defaultBackground != null)
            {
                return defaultBackground;
            }

            using Stream? stream = typeof(LocalStore).Assembly.GetManifestResourceStream("default-background.jpg");

            if (stream == null)
            {
                return null;
            }

            using MemoryStream memory = new MemoryStream();
            stream.CopyTo(memory);
            return defaultBackground = memory.ToArray();
        }

        // QR-koden står i det hvide felt midt på standardbaggrunden.
        public static QrLayout DefaultBackgroundQrLayout => new QrLayout { X = 0.5, Y = 0.6047, Size = 0.195 };

        // ---------------------------------------------------------
        // OFFLINE-SESSIONER (kø til serveren)
        // ---------------------------------------------------------

        public static List<OfflineSession> LoadSessions()
        {
            lock (SessionsLock)
            {
                return Read<List<OfflineSession>>(SessionsPath) ?? new List<OfflineSession>();
            }
        }

        public static void SaveSession(OfflineSession session)
        {
            lock (SessionsLock)
            {
                List<OfflineSession> sessions = Read<List<OfflineSession>>(SessionsPath) ?? new List<OfflineSession>();
                sessions.RemoveAll(item => item.Id == session.Id);
                sessions.Add(session);
                Write(SessionsPath, sessions);
            }
        }

        public static void RemoveSessions(IEnumerable<string> ids)
        {
            HashSet<string> remove = new HashSet<string>(ids);

            if (remove.Count == 0)
            {
                return;
            }

            lock (SessionsLock)
            {
                List<OfflineSession> sessions = Read<List<OfflineSession>>(SessionsPath) ?? new List<OfflineSession>();
                Write(SessionsPath, sessions.Where(item => !remove.Contains(item.Id)).ToList());
            }
        }

        // ---------------------------------------------------------
        // FILHJÆLP
        // ---------------------------------------------------------

        private static T? Read<T>(string path) where T : class
        {
            try
            {
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                    : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke læse {path}: {ex.Message}");
                return null;
            }
        }

        private static byte[]? ReadBytes(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke læse {path}: {ex.Message}");
                return null;
            }
        }

        private static void Write<T>(string path, T value)
        {
            WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
        }

        // Skriver til en midlertidig fil først, så en genstart midt i
        // skrivningen ikke efterlader en ødelagt fil.
        private static void WriteBytes(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Folder);
            string temporaryPath = path + ".tmp";
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, path, overwrite: true);
        }

        private static void Delete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke slette {path}: {ex.Message}");
            }
        }
    }

    internal sealed class PcInfo
    {
        public string PcId { get; set; } = "";
    }

    internal sealed class CachedEvent
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int DurationSeconds { get; set; } = LocalStore.DefaultDurationSeconds;
        public List<SurveyQuestion> Questions { get; set; } = new List<SurveyQuestion>();

        // Ældre servere sender ikke placeringen; så står QR-koden i midten.
        public QrLayout? QrLayout { get; set; }

        // "cover" (fyld skærmen) eller "contain" (vis hele billedet).
        public string? BackgroundFit { get; set; }
    }

    // QR-kodens placering på baggrunden: X og Y er midten og Size er bredden,
    // alle som brøkdel (0-1) af baggrundsbilledets bredde/højde.
    internal sealed class QrLayout
    {
        public double X { get; set; } = 0.5;
        public double Y { get; set; } = 0.5;
        public double Size { get; set; } = 0.16;
    }

    internal sealed class SurveyQuestion
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "select";
        public bool Required { get; set; }
        public List<SurveyOption> Options { get; set; } = new List<SurveyOption>();
    }

    internal sealed class SurveyOption
    {
        public string Value { get; set; } = "";
        public string Label { get; set; } = "";

        public override string ToString() => Label;
    }

    internal sealed class ManualSettings
    {
        // null = brug eventets tid (eller standarden på 10 minutter).
        public int? DurationMinutes { get; set; }

        // Placering af QR-koden på den manuelle baggrund. null = midten.
        public QrLayout? QrLayout { get; set; }

        public string? BackgroundFit { get; set; }
    }

    internal sealed class OfflineSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int? EventId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public int DurationSeconds { get; set; }
        public Dictionary<string, string?> Answers { get; set; } = new Dictionary<string, string?>();
    }
}
