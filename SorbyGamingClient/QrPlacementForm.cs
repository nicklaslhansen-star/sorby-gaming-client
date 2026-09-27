using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using QRCoder;

namespace SorbyGamingClient
{
    // Træk QR-koden rundt på den lokale baggrund, og brug scrollhjulet eller
    // skyderen til at ændre størrelsen. Samme format som web-panelet bruger.
    internal sealed class QrPlacementForm : SorbyForm
    {
        private const double MinSize = 0.03;
        private const double MaxSize = 0.9;

        private readonly PlacementCanvas canvas;
        private readonly ModernSlider sizeSlider;

        public QrLayout Result => canvas.Placement;

        public QrPlacementForm(byte[]? backgroundBytes, string fit, QrLayout? layout)
        {
            Title = "Placér QR-kode";
            Subtitle = "Træk QR-koden på plads. Scroll eller brug skyderen for at ændre størrelsen.";
            Width = S(980);

            int left = Padding24;
            int width = Width - Padding24 * 2;

            CardPanel canvasCard = new CardPanel
            {
                Location = new Point(left, HeaderHeight + S(4)),
                Size = new Size(width, S(500))
            };

            canvas = new PlacementCanvas(LoadImage(backgroundBytes), fit, layout ?? new QrLayout())
            {
                Location = new Point(S(12), S(12)),
                Size = new Size(width - S(24), S(476)),
                BackColor = Theme.Surface
            };
            canvasCard.Controls.Add(canvas);

            int rowTop = canvasCard.Bottom + S(20);

            TextLabel sizeLabel = new TextLabel("Størrelse", 10, true, Theme.Muted) { Location = new Point(left, rowTop + S(12)) };

            sizeSlider = new ModernSlider
            {
                Minimum = MinSize,
                Maximum = MaxSize,
                Location = new Point(left + S(84), rowTop + S(6)),
                Size = new Size(S(300), S(32)),
                Value = canvas.Placement.Size
            };

            sizeSlider.ValueChanged += (sender, e) => canvas.SetSize(sizeSlider.Value);
            canvas.LayoutChanged += () => sizeSlider.Value = canvas.Placement.Size;

            ModernButton centerButton = new ModernButton("Midt på", ButtonStyle.Secondary)
            {
                Icon = "\uE7B5",
                Location = new Point(sizeSlider.Right + S(20), rowTop),
                Size = new Size(S(130), S(44))
            };
            centerButton.Click += (sender, e) => canvas.Center();

            ModernButton saveButton = new ModernButton("Gem placering")
            {
                Icon = "\uE74E",
                Size = new Size(S(170), S(44)),
                Location = new Point(Width - Padding24 - S(170), rowTop),
                DialogResult = DialogResult.OK
            };

            ModernButton cancelButton = new ModernButton("Annuller", ButtonStyle.Secondary)
            {
                Size = new Size(S(120), S(44)),
                Location = new Point(saveButton.Left - S(130), rowTop),
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(new Control[] { canvasCard, sizeLabel, sizeSlider, centerButton, cancelButton, saveButton });
            AcceptButton = saveButton;
            CancelButton = cancelButton;
            Height = rowTop + S(44) + Padding24;
        }

        private static Image? LoadImage(byte[]? bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            try
            {
                using MemoryStream stream = new MemoryStream(bytes);
                using Image image = Image.FromStream(stream);
                return new Bitmap(image);
            }
            catch
            {
                return null;
            }
        }

        private sealed class PlacementCanvas : Control
        {
            private readonly Image? background;
            private readonly string fit;
            private Bitmap? composed;
            private readonly QRCodeData sample = QrRenderer.Create("https://sorby-esport-web.onrender.com/?token=forhaandsvisning");
            private PointF? dragOffset;

            public QrLayout Placement { get; private set; }

            public event Action? LayoutChanged;

            public PlacementCanvas(Image? background, string fit, QrLayout layout)
            {
                this.background = background;
                this.fit = fit;
                Placement = new QrLayout { X = layout.X, Y = layout.Y, Size = layout.Size };
                DoubleBuffered = true;
                ResizeRedraw = true;
                SetStyle(ControlStyles.Selectable, true);
                BackColor = Color.FromArgb(14, 22, 42);
                Cursor = Cursors.Hand;
            }

            // Forhåndsvisningen har samme format som PC'ens egen skærm.
            private Rectangle ScreenArea => QrRenderer.ImageRect(ClientSize, Screen.PrimaryScreen!.Bounds.Size, "contain");

            private Rectangle ImageArea
            {
                get
                {
                    Rectangle screen = ScreenArea;

                    if (background == null)
                    {
                        return screen;
                    }

                    Rectangle image = QrRenderer.ImageRect(screen.Size, background.Size, fit);
                    image.Offset(screen.Location);
                    return image;
                }
            }

            private Rectangle QrRect => QrRenderer.Place(ImageArea, ScreenArea, Placement, 0);

            public void SetSize(double size) => Apply(Placement.X, Placement.Y, size);

            public void Center() => Apply(0.5, 0.5, Placement.Size);

            private void Apply(double x, double y, double size)
            {
                // Placér først, så QR-koden holdes inden for skærmen, og regn
                // derefter tilbage til brøkdele af billedet.
                Rectangle area = ImageArea;
                QrLayout tentative = new QrLayout { X = x, Y = y, Size = Math.Clamp(size, MinSize, MaxSize) };
                Rectangle qr = QrRenderer.Place(area, ScreenArea, tentative, 0);

                Placement = new QrLayout
                {
                    X = Math.Round((qr.X + qr.Width / 2.0 - area.X) / area.Width, 4),
                    Y = Math.Round((qr.Y + qr.Height / 2.0 - area.Y) / area.Height, 4),
                    Size = Math.Round(qr.Width / (double)area.Width, 4)
                };

                LayoutChanged?.Invoke();
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Rectangle screen = ScreenArea;

                if (background != null)
                {
                    if (composed == null || composed.Size != screen.Size)
                    {
                        composed?.Dispose();
                        composed = QrRenderer.ComposeBackground(background, screen.Size, fit);
                    }

                    e.Graphics.DrawImageUnscaled(composed, screen.Location);
                }
                else
                {
                    using SolidBrush brush = new SolidBrush(Color.FromArgb(20, 20, 20));
                    e.Graphics.FillRectangle(brush, screen);
                }

                QrRenderer.DrawCard(e.Graphics, sample, QrRect);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Rectangle area = ImageArea;
                Rectangle qr = QrRect;

                if (!qr.Contains(e.Location) || area.Width == 0 || area.Height == 0)
                {
                    return;
                }

                dragOffset = new PointF(
                    (float)((e.X - area.X) / (double)area.Width - Placement.X),
                    (float)((e.Y - area.Y) / (double)area.Height - Placement.Y));
                Capture = true;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);

                if (dragOffset is not PointF offset)
                {
                    return;
                }

                Rectangle area = ImageArea;
                Apply(
                    (e.X - area.X) / (double)area.Width - offset.X,
                    (e.Y - area.Y) / (double)area.Height - offset.Y,
                    Placement.Size);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                dragOffset = null;
                Capture = false;
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                SetSize(Placement.Size * (e.Delta > 0 ? 1.06 : 1 / 1.06));
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                Focus();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    background?.Dispose();
                    composed?.Dispose();
                    sample.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
