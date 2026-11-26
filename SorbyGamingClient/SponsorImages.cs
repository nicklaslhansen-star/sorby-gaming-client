using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SorbyGamingClient
{
    // Sponsorbillederne hentes fra serveren og gemmes lokalt, så bannerne
    // også vises uden internet. Adressen skifter (?v=), når et billede skiftes,
    // så en fil pr. adresse er altid den rigtige udgave.
    internal static class SponsorImages
    {
        private static readonly string Folder = Path.Combine(LocalStore.Folder, "sponsors");
        private static readonly SemaphoreSlim SyncLock = new SemaphoreSlim(1, 1);

        public static IEnumerable<SponsorItem> All(SponsorSetup? setup) =>
            setup == null
                ? Enumerable.Empty<SponsorItem>()
                : setup.QrLeft.Concat(setup.QrRight).Concat(setup.Game).Where(item => !string.IsNullOrWhiteSpace(item.Url));

        // Henter de billeder, der mangler, og sletter dem, der ikke bruges mere.
        public static async Task SyncAsync(SponsorSetup? setup, HttpClient http)
        {
            await SyncLock.WaitAsync();

            try
            {
                Directory.CreateDirectory(Folder);
                HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (SponsorItem item in All(setup))
                {
                    string path = PathFor(item);
                    wanted.Add(Path.GetFileName(path));

                    if (File.Exists(path))
                    {
                        continue;
                    }

                    try
                    {
                        byte[] bytes = await http.GetByteArrayAsync(item.Url);
                        string temp = path + ".tmp";
                        await File.WriteAllBytesAsync(temp, bytes);
                        File.Move(temp, path, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Kunne ikke hente sponsor {item.Name}: {ex.Message}");
                    }
                }

                foreach (string file in Directory.GetFiles(Folder))
                {
                    if (!wanted.Contains(Path.GetFileName(file)))
                    {
                        try { File.Delete(file); } catch { /* bruges måske lige nu */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke opdatere sponsorer: {ex.Message}");
            }
            finally
            {
                SyncLock.Release();
            }
        }

        // Billedet, hvis det er hentet. Strømmen holdes åben, så en GIF kan animeres.
        public static Image? Load(SponsorItem item)
        {
            try
            {
                string path = PathFor(item);

                if (!File.Exists(path))
                {
                    return null;
                }

                MemoryStream stream = new MemoryStream(File.ReadAllBytes(path));
                return Image.FromStream(stream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunne ikke vise sponsor {item.Name}: {ex.Message}");
                return null;
            }
        }

        public static List<Image> LoadAll(IEnumerable<SponsorItem>? items) =>
            LoadWithItems(items).Select(pair => pair.Image).ToList();

        // Billederne sammen med sponsoren, de hører til (til målingen af eksponering).
        public static List<(SponsorItem Item, Image Image)> LoadWithItems(IEnumerable<SponsorItem>? items)
        {
            List<(SponsorItem, Image)> result = new List<(SponsorItem, Image)>();

            foreach (SponsorItem item in items ?? Enumerable.Empty<SponsorItem>())
            {
                Image? image = Load(item);

                if (image != null)
                {
                    result.Add((item, image));
                }
            }

            return result;
        }

        public static void DisposeAll(List<Image> images, EventHandler? frameChanged = null)
        {
            foreach (Image image in images)
            {
                if (frameChanged != null && ImageAnimator.CanAnimate(image))
                {
                    ImageAnimator.StopAnimate(image, frameChanged);
                }

                image.Dispose();
            }

            images.Clear();
        }

        public static bool IsAnimated(Image image) => ImageAnimator.CanAnimate(image);

        // Billedet tegnet så stort som muligt inden for feltet, uden at beskære.
        public static Rectangle Fit(Image image, Rectangle area)
        {
            if (image.Width <= 0 || image.Height <= 0)
            {
                return area;
            }

            float scale = Math.Min(area.Width / (float)image.Width, area.Height / (float)image.Height);
            int width = Math.Max(1, (int)Math.Round(image.Width * scale));
            int height = Math.Max(1, (int)Math.Round(image.Height * scale));
            return new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
        }

        private static string PathFor(SponsorItem item)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(item.Url));
            return Path.Combine(Folder, $"{item.Id}-{Convert.ToHexString(hash, 0, 8)}.img");
        }
    }
}
