using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using QRCoder;

namespace SorbyGamingClient
{
    // Tegner QR-koden direkte som vektorer på et hvidt, afrundet kort med
    // blød skygge. Modulerne tegnes på hele pixels, så koden altid er skarp
    // og let at scanne; kun de tre hjørne-"øjne" er afrundede.
    internal static class QrRenderer
    {
        public const int DefaultSizePixels = 300;

        private static readonly Color DarkColor = Color.FromArgb(11, 16, 32);

        public static QRCodeData Create(string text)
        {
            using QRCodeGenerator generator = new QRCodeGenerator();
            return generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        }

        // "cover" fylder skærmen og beskærer; "contain" viser hele billedet.
        // Ukendt eller manglende værdi betyder "cover".
        public static bool IsCover(string? fit) => fit != "contain";

        // Billedets placering på skærmen. Ved "cover" kan det gå ud over kanten.
        public static Rectangle ImageRect(Size area, Size image, string? fit)
        {
            if (image.Width <= 0 || image.Height <= 0)
            {
                return new Rectangle(Point.Empty, area);
            }

            float scaleX = (float)area.Width / image.Width;
            float scaleY = (float)area.Height / image.Height;
            float scale = IsCover(fit) ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            int width = (int)Math.Round(image.Width * scale);
            int height = (int)Math.Round(image.Height * scale);
            return new Rectangle((area.Width - width) / 2, (area.Height - height) / 2, width, height);
        }

        // Placerer QR-koden i forhold til billedet (area) og holder den inden
        // for den synlige del af skærmen (screen). Uden layout står den i midten.
        public static Rectangle Place(Rectangle area, Rectangle screen, QrLayout? layout, int defaultSize)
        {
            Rectangle visible = Rectangle.Intersect(area, screen);

            if (visible.Width <= 0 || visible.Height <= 0)
            {
                visible = screen;
            }

            int maxSize = Math.Max(1, Math.Min(visible.Width, visible.Height));

            if (layout == null)
            {
                int size = Math.Min(defaultSize, maxSize);
                return new Rectangle(visible.X + (visible.Width - size) / 2, visible.Y + (visible.Height - size) / 2, size, size);
            }

            int qrSize = Math.Clamp((int)Math.Round(layout.Size * area.Width), 1, maxSize);
            int left = (int)Math.Round(area.X + layout.X * area.Width - qrSize / 2.0);
            int top = (int)Math.Round(area.Y + layout.Y * area.Height - qrSize / 2.0);

            left = Math.Clamp(left, visible.Left, visible.Right - qrSize);
            top = Math.Clamp(top, visible.Top, visible.Bottom - qrSize);
            return new Rectangle(left, top, qrSize, qrSize);
        }

        // Tegner baggrunden i skærmstørrelse. Ved "contain" udfyldes kanterne
        // med en sløret, mørkere udgave af billedet i stedet for sorte bjælker.
        public static Bitmap ComposeBackground(Image image, Size size, string? fit)
        {
            Bitmap result = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));

            using Graphics graphics = Graphics.FromImage(result);
            using ImageAttributes attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);

            graphics.Clear(Color.FromArgb(20, 20, 20));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (!IsCover(fit))
            {
                // Sløring: skalér ned til et lille billede og op igen.
                int smallWidth = 24;
                int smallHeight = Math.Max(1, (int)Math.Round(smallWidth * image.Height / (double)image.Width));

                using Bitmap small = new Bitmap(smallWidth, smallHeight);
                using (Graphics smallGraphics = Graphics.FromImage(small))
                {
                    smallGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    smallGraphics.DrawImage(image, 0, 0, smallWidth, smallHeight);
                }

                Rectangle cover = ImageRect(size, image.Size, "cover");
                cover.Inflate(cover.Width / 12, cover.Height / 12);
                graphics.DrawImage(small, cover, 0, 0, smallWidth, smallHeight, GraphicsUnit.Pixel, attributes);

                using SolidBrush dim = new SolidBrush(Color.FromArgb(130, 0, 0, 0));
                graphics.FillRectangle(dim, 0, 0, size.Width, size.Height);
            }

            graphics.DrawImage(image, ImageRect(size, image.Size, fit), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            return result;
        }

        public static void DrawCard(Graphics graphics, QRCodeData data, Rectangle card)
        {
            int count = data.ModuleMatrix.Count;

            if (count == 0 || card.Width < count)
            {
                return;
            }

            int module = card.Width / count;
            int offsetX = card.X + (card.Width - module * count) / 2;
            int offsetY = card.Y + (card.Height - module * count) / 2;
            float radius = Math.Min(card.Width * 0.07f, module * 3f);

            SmoothingMode oldSmoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            DrawShadow(graphics, card, radius);

            using (GraphicsPath cardPath = RoundedRect(card, radius))
            {
                graphics.FillPath(Brushes.White, cardPath);
            }

            // Den stille zone (hvid kant) er en del af matricen. Øverste
            // venstre øje starter der, hvor diagonalen første gang er mørk.
            int quiet = 0;
            while (quiet < count && !data.ModuleMatrix[quiet][quiet])
            {
                quiet++;
            }

            int inner = count - quiet * 2;

            bool InEye(int row, int col)
            {
                int r = row - quiet;
                int c = col - quiet;
                return (r < 7 && c < 7) || (r < 7 && c >= inner - 7) || (r >= inner - 7 && c < 7);
            }

            using SolidBrush dark = new SolidBrush(DarkColor);

            graphics.SmoothingMode = SmoothingMode.None;

            // Sammenhængende mørke moduler i en række tegnes som ét rektangel.
            for (int row = 0; row < count; row++)
            {
                int col = 0;

                while (col < count)
                {
                    if (!data.ModuleMatrix[row][col] || InEye(row, col))
                    {
                        col++;
                        continue;
                    }

                    int start = col;

                    while (col < count && data.ModuleMatrix[row][col] && !InEye(row, col))
                    {
                        col++;
                    }

                    graphics.FillRectangle(dark, offsetX + start * module, offsetY + row * module, (col - start) * module, module);
                }
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            foreach ((int row, int col) in new[] { (quiet, quiet), (quiet, quiet + inner - 7), (quiet + inner - 7, quiet) })
            {
                float x = offsetX + col * module;
                float y = offsetY + row * module;
                FillRounded(graphics, dark, new RectangleF(x, y, module * 7, module * 7), module * 2.1f);
                FillRounded(graphics, Brushes.White, new RectangleF(x + module, y + module, module * 5, module * 5), module * 1.4f);
                FillRounded(graphics, dark, new RectangleF(x + module * 2, y + module * 2, module * 3, module * 3), module * 0.9f);
            }

            graphics.SmoothingMode = oldSmoothing;
        }

        private static void DrawShadow(Graphics graphics, Rectangle card, float radius)
        {
            int spread = Math.Max(8, card.Width / 16);

            for (int i = spread; i > 0; i -= 2)
            {
                int alpha = (int)(46 * Math.Pow(1 - (double)i / spread, 2)) + 2;
                Rectangle shadow = card;
                shadow.Inflate(i, i);
                shadow.Offset(0, spread / 3);

                using SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                using GraphicsPath path = RoundedRect(shadow, radius + i);
                graphics.FillPath(brush, path);
            }
        }

        private static void FillRounded(Graphics graphics, Brush brush, RectangleF rect, float radius)
        {
            using GraphicsPath path = RoundedRect(rect, radius);
            graphics.FillPath(brush, path);
        }

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            float diameter = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
