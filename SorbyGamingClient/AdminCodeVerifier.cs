using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SorbyGamingClient
{
    // Kontrollerer PC-admin-koden.
    //
    // Online: serveren sammenligner med PC_ADMIN_CODE. Ved korrekt kode gemmes
    // et hash af koden lokalt på PC'en (aldrig selve koden).
    //
    // Offline: hvis serveren ikke kan nås, sammenlignes med det senest gemte
    // hash. PC'en skal derfor have været logget ind online mindst én gang.
    internal static class AdminCodeVerifier
    {
        private const int MaxOfflineAttempts = 10;
        private static readonly TimeSpan OfflineLockDuration = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan OnlineTimeout = TimeSpan.FromSeconds(5);
        private const int HashIterations = 100_000;

        private static readonly HttpClient HttpClient = new HttpClient();

        private static readonly string OfflineHashPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SorbyGaming",
            "offline-admin.json"
        );

        // Sand hvis seneste kontrol ikke kunne nå serveren.
        public static bool LastCheckWasOffline { get; private set; }

        public static bool HasOfflineCode => File.Exists(OfflineHashPath);

        private static int offlineFailures;
        private static DateTime offlineLockedUntil = DateTime.MinValue;

        // Returnerer null ved korrekt kode, ellers en fejlbesked.
        public static async Task<string?> VerifyAsync(
            string serverUrl,
            string code,
            string? pcId
        )
        {
            HttpResponseMessage? response = null;
            LastCheckWasOffline = false;

            try
            {
                using CancellationTokenSource timeout =
                    new CancellationTokenSource(OnlineTimeout);

                using StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { code, pcId }),
                    Encoding.UTF8,
                    "application/json"
                );

                response = await HttpClient.PostAsync(
                    $"{serverUrl}/api/pc/admin-check",
                    content,
                    timeout.Token
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Serveren kunne ikke nås ({ex.Message}). Bruger offline-kode."
                );

                LastCheckWasOffline = true;
                return VerifyOffline(code);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    SetOfflineCode(code);
                    return null;
                }

                // Serveren kører, men koden er ikke sat op endnu.
                if ((int)response.StatusCode == 503)
                {
                    LastCheckWasOffline = true;
                    return VerifyOffline(code);
                }

                return await ReadMessageAsync(response) ?? "Forkert admin-kode.";
            }
        }

        private static string? VerifyOffline(string code)
        {
            if (offlineLockedUntil > DateTime.Now)
            {
                return "For mange forkerte forsøg. Prøv igen om lidt.";
            }

            OfflineHash? stored = LoadOfflineHash();

            if (stored == null)
            {
                return "Ingen forbindelse til serveren, og der er ikke gemt " +
                    "en offline-kode på denne PC endnu.\n\n" +
                    "Log ind med admin-koden én gang, mens PC'en er online.";
            }

            byte[] hash = HashCode(code, Convert.FromBase64String(stored.Salt), stored.Iterations);

            if (CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(stored.Hash)))
            {
                offlineFailures = 0;
                return null;
            }

            offlineFailures++;

            if (offlineFailures >= MaxOfflineAttempts)
            {
                offlineFailures = 0;
                offlineLockedUntil = DateTime.Now + OfflineLockDuration;
            }

            return "Forkert admin-kode.";
        }

        // Gemmer koden som offline-kode. Kaldes automatisk ved korrekt
        // online-login og ved første opstart på en PC uden internet.
        public static void SetOfflineCode(string code)
        {
            try
            {
                byte[] salt = RandomNumberGenerator.GetBytes(16);

                OfflineHash data = new OfflineHash
                {
                    Salt = Convert.ToBase64String(salt),
                    Hash = Convert.ToBase64String(HashCode(code, salt, HashIterations)),
                    Iterations = HashIterations
                };

                Directory.CreateDirectory(Path.GetDirectoryName(OfflineHashPath)!);
                File.WriteAllText(OfflineHashPath, JsonSerializer.Serialize(data));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke gemme offline-kode: {ex.Message}");
            }
        }

        private static OfflineHash? LoadOfflineHash()
        {
            try
            {
                return File.Exists(OfflineHashPath)
                    ? JsonSerializer.Deserialize<OfflineHash>(File.ReadAllText(OfflineHashPath))
                    : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke læse offline-kode: {ex.Message}");
                return null;
            }
        }

        private static byte[] HashCode(string code, byte[] salt, int iterations)
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(code),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                32
            );
        }

        private static async Task<string?> ReadMessageAsync(HttpResponseMessage response)
        {
            try
            {
                using JsonDocument json =
                    JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                return json.RootElement.TryGetProperty("message", out JsonElement message)
                    ? message.GetString()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private sealed class OfflineHash
        {
            public string Salt { get; set; } = "";
            public string Hash { get; set; } = "";
            public int Iterations { get; set; }
        }
    }
}
