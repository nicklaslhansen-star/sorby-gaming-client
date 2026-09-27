using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    // Lille timer nederst i højre hjørne under en session. Sammenklappet er
    // den en diskret ring med minutterne; holder man musen over, folder den
    // sig ud og viser præcis tid tilbage og hvornår sessionen slutter.
    //
    // Vinduet er et "layered window" med gennemsigtighed pr. pixel og er helt
    // klik-igennem, så det aldrig stjæler fokus eller klik fra et spil.
    // Hover opdages ved at kigge på musens position.
    internal sealed class SessionTimerOverlay : Form
    {
        private const int CanvasWidth = 300;
        private const int CanvasHeight = 88;
        private const int PillHeight = 56;
        private const int ExpandedWidth = 272;
        private const int ScreenMargin = 18;
        private const float AnimationSeconds = 0.28f;

        private static readonly Color Accent = Color.FromArgb(101, 215, 255);
        private static readonly Color Warning = Color.FromArgb(255, 200, 87);
        private static readonly Color Danger = Color.FromArgb(255, 92, 122);

        private int totalSeconds;
        private readonly float scale;
        private readonly System.Windows.Forms.Timer frameTimer;
        private readonly Stopwatch clock = Stopwatch.StartNew();

        private int remainingSeconds;
        private double remainingSetAt;
        private DateTime endsAt;
        private float expand;
        private double autoExpandUntil;
        private double lastFrame;
        private double lastRender = -1;
        private bool dirty = true;

        public SessionTimerOverlay(int durationSeconds)
        {
            totalSeconds = Math.Max(1, durationSeconds);
            remainingSeconds = totalSeconds;
            endsAt = DateTime.Now.AddSeconds(totalSeconds);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Text = "Sørby Gaming - tid tilbage";

            scale = DeviceDpi / 96f;
            Size = new Size(Scaled(CanvasWidth), Scaled(CanvasHeight));

            Rectangle workingArea = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(
                workingArea.Right - Width - Scaled(ScreenMargin) + Scaled(10),
                workingArea.Bottom - Height - Scaled(ScreenMargin) + Scaled(14));

            // Vis timeren et øjeblik ved start, så man ved, den er der.
            autoExpandUntil = 4;

            frameTimer = new System.Windows.Forms.Timer { Interval = 33 };
            frameTimer.Tick += (sender, e) => Frame();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOPMOST = 0x00000008;
                const int WS_EX_TRANSPARENT = 0x00000020;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_LAYERED = 0x00080000;
                const int WS_EX_NOACTIVATE = 0x08000000;

                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            lastFrame = clock.Elapsed.TotalSeconds;
            Render();
            frameTimer.Start();
        }

        public void SetRemaining(int seconds)
        {
            int previous = remainingSeconds;
            remainingSeconds = Math.Max(0, seconds);
            remainingSetAt = clock.Elapsed.TotalSeconds;
            endsAt = DateTime.Now.AddSeconds(remainingSeconds);

            // Mere tid fra dashboardet: ringen starter forfra, og timeren
            // folder sig ud, så man kan se den nye tid.
            if (remainingSeconds > totalSeconds)
            {
                totalSeconds = remainingSeconds;
            }

            if (remainingSeconds > previous + 1)
            {
                autoExpandUntil = remainingSetAt + 5;
            }

            // Fold kort ud, når der er 5 og 1 minut tilbage.
            if ((previous > 300 && remainingSeconds <= 300 && totalSeconds > 300) ||
                (previous > 60 && remainingSeconds <= 60))
            {
                autoExpandUntil = remainingSetAt + 6;
            }

            dirty = true;
        }

        // ---------------------------------------------------------
        // ANIMATION
        // ---------------------------------------------------------

        private void Frame()
        {
            double now = clock.Elapsed.TotalSeconds;
            float delta = (float)Math.Min(0.1, now - lastFrame);
            lastFrame = now;

            bool wantExpanded = IsHovered() || now < autoExpandUntil || remainingSeconds <= 10;
            float target = wantExpanded ? 1f : 0f;

            if (expand != target)
            {
                float step = delta / AnimationSeconds;
                expand = target > expand ? Math.Min(target, expand + step) : Math.Max(target, expand - step);
                dirty = true;
            }

            // Ringen glider jævnt; ellers er 4 billeder i sekundet rigeligt.
            // Det sidste minut pulserer den, så den tegnes hver gang.
            bool animating = expand > 0f && expand < 1f;
            double interval = animating || remainingSeconds <= 60 ? 0 : 0.25;

            if (dirty || now - lastRender >= interval)
            {
                Render();
                lastRender = now;
                dirty = false;
            }
        }

        private bool IsHovered()
        {
            RectangleF pill = PillBounds(expand);
            pill.Offset(Left, Top);
            pill.Inflate(Scaled(6), Scaled(6));
            return pill.Contains(Cursor.Position);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.2f;
            const float c3 = c1 + 1;
            float x = t - 1;
            return 1 + c3 * x * x * x + c1 * x * x;
        }

        private static float EaseInOut(float t) => t * t * (3 - 2 * t);

        // ---------------------------------------------------------
        // TEGNING
        // ---------------------------------------------------------

        private RectangleF PillBounds(float expandAmount)
        {
            float eased = expandAmount <= 0 ? 0 : expandAmount >= 1 ? 1 : EaseOutBack(expandAmount);
            float height = Scaled(PillHeight);
            float width = height + (Scaled(ExpandedWidth) - height) * eased;
            float right = Width - Scaled(10);
            float top = (Height - height) / 2f - Scaled(4);
            return new RectangleF(right - width, top, width, height);
        }

        private double SmoothRemaining()
        {
            double elapsed = clock.Elapsed.TotalSeconds - remainingSetAt;
            return Math.Max(0, remainingSeconds - Math.Min(1, elapsed));
        }

        private Color RingColor(double fraction)
        {
            if (fraction > 0.5) return Accent;
            if (fraction > 0.2) return Blend(Warning, Accent, (float)((fraction - 0.2) / 0.3));
            return Blend(Danger, Warning, (float)(fraction / 0.2));
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        private void Draw(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            double now = clock.Elapsed.TotalSeconds;
            double remaining = SmoothRemaining();
            double fraction = Math.Clamp(remaining / totalSeconds, 0, 1);
            Color ringColor = RingColor(fraction);
            float pulse = remainingSeconds <= 60 ? (float)(0.5 + 0.5 * Math.Sin(now * Math.PI * 2 / 1.2)) : 0f;

            RectangleF pill = PillBounds(expand);
            float radius = pill.Height / 2f;

            // Blød skygge under pillen.
            for (int i = Scaled(10); i > 0; i -= 2)
            {
                RectangleF shadow = pill;
                shadow.Inflate(i, i);
                shadow.Offset(0, Scaled(3));
                int alpha = (int)(34 * Math.Pow(1 - i / (float)Scaled(10), 2)) + 1;
                using GraphicsPath shadowPath = QrRenderer.RoundedRect(shadow, radius + i);
                using SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                graphics.FillPath(shadowBrush, shadowPath);
            }

            using GraphicsPath pillPath = QrRenderer.RoundedRect(pill, radius);

            using (LinearGradientBrush background = new LinearGradientBrush(
                pill, Color.FromArgb(236, 24, 32, 54), Color.FromArgb(236, 12, 17, 32), LinearGradientMode.Vertical))
            {
                graphics.FillPath(background, pillPath);
            }

            // Tynd kant, der gløder i ringens farve, når tiden er ved at løbe ud.
            Color borderColor = pulse > 0
                ? Color.FromArgb((int)(70 + 120 * pulse), ringColor)
                : Color.FromArgb(46, 255, 255, 255);

            using (Pen border = new Pen(borderColor, Math.Max(1f, scale)))
            {
                graphics.DrawPath(border, pillPath);
            }

            // Ringen sidder altid i højre ende af pillen.
            float ringSize = pill.Height - Scaled(16);
            RectangleF ring = new RectangleF(pill.Right - Scaled(8) - ringSize, pill.Top + Scaled(8), ringSize, ringSize);
            float ringWidth = Scaled(4);

            if (pulse > 0)
            {
                RectangleF glow = ring;
                glow.Inflate(Scaled(3), Scaled(3));
                using Pen glowPen = new Pen(Color.FromArgb((int)(90 * pulse), ringColor), ringWidth + Scaled(4));
                graphics.DrawEllipse(glowPen, glow.X + Scaled(3), glow.Y + Scaled(3), glow.Width - Scaled(6), glow.Height - Scaled(6));
            }

            using (Pen track = new Pen(Color.FromArgb(40, 255, 255, 255), ringWidth))
            {
                graphics.DrawEllipse(track, ring);
            }

            if (fraction > 0)
            {
                using Pen progress = new Pen(ringColor, ringWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawArc(progress, ring, -90, (float)(360 * fraction));
            }

            DrawCentered(graphics, ShortTime(remainingSeconds), ring, Scaled(remainingSeconds >= 600 ? 11 : 12), Color.White, true);

            // Tekst i den udfoldede del toner frem, når pillen er bred nok.
            float textAlpha = EaseInOut(Math.Clamp((expand - 0.35f) / 0.65f, 0, 1));

            if (textAlpha <= 0.01f)
            {
                return;
            }

            GraphicsState state = graphics.Save();
            graphics.SetClip(pillPath);

            float textLeft = pill.Left + Scaled(22);
            float slide = (1 - textAlpha) * Scaled(12);

            using (Font big = new Font("Segoe UI Semibold", Scaled(19), FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font small = new Font("Segoe UI", Scaled(11.5f), FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush white = new SolidBrush(Color.FromArgb((int)(255 * textAlpha), Color.White)))
            using (SolidBrush muted = new SolidBrush(Color.FromArgb((int)(170 * textAlpha), 190, 205, 230)))
            {
                string label = remainingSeconds <= 60
                    ? "tilbage – gem dit spil!"
                    : $"tilbage · slutter kl. {endsAt:HH:mm}";

                graphics.DrawString(LongTime(remainingSeconds), big, white, textLeft + slide, pill.Top + Scaled(6));
                graphics.DrawString(label, small, muted, textLeft + slide + Scaled(1), pill.Top + Scaled(31));
            }

            graphics.Restore(state);
        }

        private void DrawCentered(Graphics graphics, string text, RectangleF bounds, float pixelSize, Color color, bool bold)
        {
            using Font font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using SolidBrush brush = new SolidBrush(color);
            using StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString(text, font, brush, bounds, format);
        }

        // I ringen: timer, minutter eller sekunder.
        private static string ShortTime(int seconds)
        {
            if (seconds >= 3600) return $"{seconds / 3600}t";
            if (seconds >= 60) return $"{(seconds + 59) / 60}";
            return $"{seconds}s";
        }

        private static string LongTime(int seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{time.Minutes:00}:{time.Seconds:00}";
        }

        private int Scaled(float value) => (int)Math.Round(value * scale);

        // ---------------------------------------------------------
        // LAYERED WINDOW
        // ---------------------------------------------------------

        private void Render()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            using Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                Draw(graphics);
            }

            // Sammenklappet er timeren lidt gennemsigtig, så den ikke forstyrrer.
            byte opacity = (byte)(185 + 70 * Math.Clamp(expand, 0, 1));

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = SelectObject(memoryDc, hBitmap);

            try
            {
                NativeSize size = new NativeSize { Width = Width, Height = Height };
                NativePoint source = new NativePoint();
                NativePoint destination = new NativePoint { X = Left, Y = Top };
                BlendFunction blend = new BlendFunction
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = opacity,
                    AlphaFormat = 1
                };

                UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, 2);
            }
            finally
            {
                SelectObject(memoryDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memoryDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                frameTimer.Stop();
                frameTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize,
            IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
