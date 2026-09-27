using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using QRCoder;

namespace SorbyGamingClient
{
    // Træk QR-koden rundt på den lokale baggrund, og brug scrollhjulet eller
    // skyderen til at ændre størrelsen. Samme format som web-panelet bruger.
    internal sealed class QrPlacementForm : Form
    {
        private const double MinSize = 0.05;
        private const double MaxSize = 0.6;

        private readonly PlacementCanvas canvas;
        private readonly TrackBar sizeBar;

        public QrLayout Result => canvas.Placement;

        public QrPlacementForm(byte[]? backgroundBytes, QrLayout? layout)
        {
            Text = "Sørby Gaming - Placér QR-kode";
            Size = new Size(960, 700);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;
            Font = new Font("Arial", 11);

            Label help = new Label
            {
                Text = "Træk QR-koden derhen, hvor den skal stå. Scroll eller brug skyderen for at ændre størrelsen.",
                AutoSize = true,
                Location = new Point(20, 15)
            };

            canvas = new PlacementCanvas(LoadImage(backgroundBytes), layout ?? new QrLayout())
            {
                Location = new Point(20, 45),
                Size = new Size(904, 510)
            };

            Label sizeLabel = new Label { Text = "Størrelse:", AutoSize = true, Location = new Point(20, 582) };

            sizeBar = new TrackBar
            {
                Minimum = (int)(MinSize * 1000),
                Maximum = (int)(MaxSize * 1000),
                TickStyle = TickStyle.None,
                Location = new Point(110, 575),
                Width = 360,
                Value = (int)Math.Round(canvas.Placement.Size * 1000)
            };

            sizeBar.Scroll += (sender, e) => canvas.SetSize(sizeBar.Value / 1000.0);
            canvas.LayoutChanged += () => sizeBar.Value = Math.Clamp((int)Math.Round(canvas.Placement.Size * 1000), sizeBar.Minimum, sizeBar.Maximum);

            Button centerButton = MakeButton("Midt", new Point(490, 572), 90);
            centerButton.Click += (sender, e) => canvas.Center();

            Button saveButton = MakeButton("GEM", new Point(710, 572), 100);
            saveButton.DialogResult = DialogResult.OK;

            Button cancelButton = MakeButton("Annuller", new Point(824, 572), 100);
            cancelButton.DialogResult = DialogResult.Cancel;

            Controls.AddRange(new Control[] { help, canvas, sizeLabel, sizeBar, centerButton, saveButton, cancelButton });
            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        private static Button MakeButton(string text, Point location, int width) => new Button
        {
            Text = text,
            Location = location,
            Width = width,
            Height = 36,
            ForeColor = Color.Black,
            BackColor = Color.White
        };

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
            private readonly QRCodeData sample = QrRenderer.Create("https://sorby-esport-web.onrender.com/?token=forhaandsvisning");
            private PointF? dragOffset;

            public QrLayout Placement { get; private set; }

            public event Action? LayoutChanged;

            public PlacementCanvas(Image? background, QrLayout layout)
            {
                this.background = background;
                Placement = new QrLayout { X = layout.X, Y = layout.Y, Size = layout.Size };
                DoubleBuffered = true;
                ResizeRedraw = true;
                SetStyle(ControlStyles.Selectable, true);
                BackColor = Color.FromArgb(14, 22, 42);
                Cursor = Cursors.Hand;
            }

            private Rectangle ImageArea => background == null
                ? QrRenderer.ZoomRect(ClientSize, new Size(16, 9))
                : QrRenderer.ZoomRect(ClientSize, background.Size);

            private Rectangle QrRect => QrRenderer.Place(ImageArea, Placement, 0);

            public void SetSize(double size) => Apply(Placement.X, Placement.Y, size);

            public void Center() => Apply(0.5, 0.5, Placement.Size);

            private void Apply(double x, double y, double size)
            {
                Rectangle area = ImageArea;
                size = Math.Clamp(size, MinSize, MaxSize);
                double halfWidth = size / 2;
                double halfHeight = Math.Min(0.5, size * area.Width / Math.Max(1.0, area.Height) / 2);

                Placement = new QrLayout
                {
                    X = Math.Round(Math.Clamp(x, halfWidth, 1 - halfWidth), 4),
                    Y = Math.Round(Math.Clamp(y, halfHeight, 1 - halfHeight), 4),
                    Size = Math.Round(size, 4)
                };

                LayoutChanged?.Invoke();
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Rectangle area = ImageArea;

                if (background != null)
                {
                    e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    e.Graphics.DrawImage(background, area);
                }
                else
                {
                    using SolidBrush brush = new SolidBrush(Color.FromArgb(20, 20, 20));
                    e.Graphics.FillRectangle(brush, area);
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
                    sample.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
