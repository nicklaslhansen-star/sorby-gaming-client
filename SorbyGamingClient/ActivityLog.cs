using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SorbyGamingClient
{
    // Måler, hvordan PC'en bruges, til eventets statistik:
    //   "qr"   - tid med QR-skærmen vist (PC'en venter på en spiller)
    //   "game" - tid i hvert spil under en session
    //   "sponsor-qr"   - tid en sponsor er vist på QR-skærmen (standen)
    //   "sponsor-game" - tid/antal gange in-game-banneret er vist for en spiller
    // Ingen personoplysninger: kun tidspunkter, spil og event.
    //
    // Målingerne gemmes lokalt og sendes til serveren, når der er forbindelse.
    // En åben måling gemmes hvert minut, så et strømsvigt højst koster et minut.
    // Ældre servere kender ikke målingerne; så bliver de liggende lokalt.
    internal static class ActivityLog
    {
        private const int MaxStored = 5000;
        private static readonly TimeSpan CheckpointInterval = TimeSpan.FromMinutes(1);

        private static readonly string FilePath = Path.Combine(LocalStore.Folder, "activity.json");
        private static readonly object FileLock = new object();
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly SemaphoreSlim SyncLock = new SemaphoreSlim(1, 1);

        // Åbne målinger pr. slags ("qr", "game").
        private static readonly Dictionary<string, ActivityRecord> Open = new Dictionary<string, ActivityRecord>();
        private static DateTime lastCheckpoint = DateTime.MinValue;

        public static void Begin(string kind, int? eventId, string? game = null, string? platform = null)
        {
            lock (FileLock)
            {
                BeginLocked(kind, kind, eventId, game, platform, null);
            }
        }

        // En sponsor vises i in-game-banneret (måles, til End(SponsorKey(...))).
        public static string SponsorKey(string kind, int sponsorId) => $"{kind}:{sponsorId}";

        public static void BeginSponsor(string kind, int? eventId, int sponsorId)
        {
            lock (FileLock)
            {
                BeginLocked(SponsorKey(kind, sponsorId), kind, eventId, null, null, sponsorId);
            }
        }

        // Præcis disse sponsorer er synlige lige nu (fx på QR-skærmen). En
        // sponsor i begge bannere tæller kun én gang.
        public static void SetVisibleSponsors(string kind, int? eventId, IEnumerable<int> sponsorIds)
        {
            lock (FileLock)
            {
                HashSet<int> ids = sponsorIds.ToHashSet();
                HashSet<string> wanted = ids.Select(id => SponsorKey(kind, id)).ToHashSet();

                foreach (string key in Open.Keys.Where(key => key.StartsWith(kind + ":", StringComparison.Ordinal) && !wanted.Contains(key)).ToList())
                {
                    EndLocked(key);
                }

                foreach (int id in ids)
                {
                    BeginLocked(SponsorKey(kind, id), kind, eventId, null, null, id);
                }
            }
        }

        private static void BeginLocked(string key, string kind, int? eventId, string? game, string? platform, int? sponsorId)
        {
            if (Open.TryGetValue(key, out ActivityRecord? current) &&
                current.EventId == eventId && current.Game == game && current.SponsorId == sponsorId)
            {
                return;
            }

            EndLocked(key);
            DateTime now = DateTime.UtcNow;
            Open[key] = new ActivityRecord
            {
                Kind = kind,
                EventId = eventId,
                Game = game,
                Platform = platform,
                SponsorId = sponsorId,
                StartedAt = now,
                EndedAt = now
            };
        }

        public static void End(string kind)
        {
            lock (FileLock)
            {
                EndLocked(kind);
            }
        }

        public static void EndAll()
        {
            lock (FileLock)
            {
                foreach (string kind in Open.Keys.ToList())
                {
                    EndLocked(kind);
                }
            }
        }

        // Kaldes jævnligt (fx hvert sekund); gemmer højst én gang i minuttet.
        public static void Checkpoint()
        {
            lock (FileLock)
            {
                if (Open.Count == 0 || DateTime.UtcNow - lastCheckpoint < CheckpointInterval)
                {
                    return;
                }

                lastCheckpoint = DateTime.UtcNow;
                List<ActivityRecord> stored = Load();

                foreach (ActivityRecord record in Open.Values)
                {
                    record.EndedAt = DateTime.UtcNow;
                    stored.RemoveAll(item => item.Id == record.Id);
                    stored.Add(record.Copy());
                }

                Save(stored);
            }
        }

        private static void EndLocked(string kind)
        {
            if (!Open.Remove(kind, out ActivityRecord? record))
            {
                return;
            }

            record.EndedAt = DateTime.UtcNow;
            List<ActivityRecord> stored = Load();
            stored.RemoveAll(item => item.Id == record.Id);

            if (record.EndedAt - record.StartedAt >= TimeSpan.FromSeconds(1))
            {
                stored.Add(record);
            }

            Save(stored);
        }

        public static async Task SyncAsync(string serverUrl, string pcId)
        {
            if (!await SyncLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                List<ActivityRecord> pending;

                lock (FileLock)
                {
                    HashSet<string> openIds = Open.Values.Select(item => item.Id).ToHashSet();
                    pending = Load().Where(item => !openIds.Contains(item.Id)).Take(500).ToList();
                }

                if (pending.Count == 0)
                {
                    return;
                }

                using HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
                    $"{serverUrl}/api/pc/activity/sync",
                    new { pcId, items = pending },
                    LocalStore.JsonOptions);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return; // Ældre server - prøv igen senere.
                }

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Aktivitet afvist: {(int)response.StatusCode}");
                    return;
                }

                SyncResult? result = await response.Content.ReadFromJsonAsync<SyncResult>(LocalStore.JsonOptions);

                if (result == null)
                {
                    return;
                }

                HashSet<string> done = result.Accepted.Concat(result.Rejected).ToHashSet();

                lock (FileLock)
                {
                    Save(Load().Where(item => !done.Contains(item.Id)).ToList());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke sende aktivitet: {ex.Message}");
            }
            finally
            {
                SyncLock.Release();
            }
        }

        private static List<ActivityRecord> Load()
        {
            try
            {
                return File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<List<ActivityRecord>>(File.ReadAllText(FilePath), LocalStore.JsonOptions) ?? new List<ActivityRecord>()
                    : new List<ActivityRecord>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke læse aktivitet: {ex.Message}");
                return new List<ActivityRecord>();
            }
        }

        private static void Save(List<ActivityRecord> records)
        {
            try
            {
                // Er PC'en offline meget længe, gemmes kun de nyeste målinger.
                if (records.Count > MaxStored)
                {
                    records = records.OrderBy(item => item.StartedAt).Skip(records.Count - MaxStored).ToList();
                }

                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(records, LocalStore.JsonOptions));
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke gemme aktivitet: {ex.Message}");
            }
        }

        private sealed class SyncResult
        {
            public List<string> Accepted { get; set; } = new List<string>();
            public List<string> Rejected { get; set; } = new List<string>();
        }
    }

    internal sealed class ActivityRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Kind { get; set; } = "";
        public int? EventId { get; set; }
        public string? Game { get; set; }
        public string? Platform { get; set; }
        public int? SponsorId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }

        public ActivityRecord Copy() => (ActivityRecord)MemberwiseClone();
    }
}
