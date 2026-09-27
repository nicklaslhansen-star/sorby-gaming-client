using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SorbyGamingClient
{
    // Sender offline-sessioner til serveren. En session fjernes først fra den
    // lokale kø, når serveren har bekræftet den.
    internal static class SessionSync
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static readonly SemaphoreSlim SyncLock = new SemaphoreSlim(1, 1);

        public static async Task SyncAsync(
            string serverUrl,
            string pcId,
            string? runningSessionId
        )
        {
            if (!await SyncLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                // Den session, der kører lige nu, sendes først, når den er slut.
                List<OfflineSession> pending = LocalStore.LoadSessions()
                    .Where(session => session.Id != runningSessionId)
                    .Take(500)
                    .ToList();

                if (pending.Count == 0)
                {
                    return;
                }

                using HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
                    $"{serverUrl}/api/pc/sessions/sync",
                    new { pcId, sessions = pending },
                    LocalStore.JsonOptions
                );

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Synkronisering afvist: {(int)response.StatusCode}");
                    return;
                }

                SyncResult? result = await response.Content.ReadFromJsonAsync<SyncResult>(LocalStore.JsonOptions);

                if (result == null)
                {
                    return;
                }

                // Afviste sessioner er ugyldige og vil aldrig blive godkendt.
                LocalStore.RemoveSessions(result.Accepted.Concat(result.Rejected));

                Console.WriteLine(
                    $"{result.Accepted.Count} offline-session(er) sendt til serveren."
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke synkronisere offline-sessioner: {ex.Message}");
            }
            finally
            {
                SyncLock.Release();
            }
        }

        private sealed class SyncResult
        {
            public List<string> Accepted { get; set; } = new List<string>();
            public List<string> Rejected { get; set; } = new List<string>();
        }
    }
}
