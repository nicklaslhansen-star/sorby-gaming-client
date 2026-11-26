using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    // Sponsorbanner under en session: glider med jævne mellemrum ind fra
    // siden, lodret midt på skærmen, bliver et øjeblik og glider ud igen.
    // Ved flere sponsorer skiftes der hver gang. En GIF animeres.
    //
    // Samme teknik som tidsvisningen: et separat "layered window", der er helt
    // klik-igennem og aldrig tager fokus. Programmet rører ikke spillet (ingen
    // indsprøjtning, ingen læsning af spillets hukommelse, intet input), så
    // anti-cheat ser det som ethvert andet vindue på skrivebordet.
    internal sealed class SponsorBannerOverlay : Form
    {
        // Samme mål som web-panelets forhåndsvisning (brøkdele af skærmen).
        private const float MaxWidth = 0.16f;
        private const float MaxHeight = 0.42f;
        private const float EdgeMargin = 0.012f;
        private const double SlideSeconds = 0.6;
        private static readonly TimeSpan FirstShowAfter = TimeSpan.FromMinutes(1);

        private readonly List<Image> images;
        private readonly bool rightSide;
        private readonly TimeSpan every;
        private readonly double showSeconds;
        private readonly Func<bool> isBlocked;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly EventHandler frameChanged;

        private DateTime nextShow;
        private int index = -1;
        private Image? current;
        private Rectangle bannerRect;
        private double shownAt = -1;
        private bool frameDirty;

        public SponsorBannerOverlay(SponsorSetup setup, Func<bool> isBlocked)
        {
            images = SponsorImages.LoadAll(setup.Game);
            rightSide = setup.GameSide != "left";
            every = TimeSpan.FromMinutes(Math.Clamp(setup.GameEveryMinutes, 1, 120));
            showSeconds = Math.Clamp(setup.GameShowSeconds, 5, 120);
            this.isBlocked = isBlocked;
            nextShow = DateTime.Now + FirstShowAfter;
            frameChanged = (sender, e) => frameDirty = true;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Text = "Sørby Gaming - sponsor";
            Bounds = new Rectangle(0, 0, 1, 1);

            timer.Tick += (sender, e) => Tick();
        }

        public bool HasSponsors => images.Count > 0;

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
            Clear();
            timer.Start();
        }

        private void Tick()
        {
            if (shownAt < 0)
            {
                // Venter på næste gang. Er spilmenuen fremme, eller kører spillet
                // i eksklusiv fuldskærm (så kan banneret ikke ses), udskydes det lidt.
                if (images.Count == 0 || DateTime.Now < nextShow)
                {
                    return;
                }

                if (isBlocked() || NativeWindowHelper.IsExclusiveFullscreen())
                {
                    nextShow = DateTime.Now.AddSeconds(15);
                    return;
                }

                Begin();
                return;
            }

            double elapsed = clock.Elapsed.TotalSeconds - shownAt;
            double total = showSeconds + SlideSeconds * 2;

            if (elapsed >= total || isBlocked())
            {
                End();
                return;
            }

            bool sliding = elapsed < SlideSeconds || elapsed > total - SlideSeconds;

            if (sliding || frameDirty)
            {
                frameDirty = false;
                Render(elapsed, total);
            }
        }

        private void Begin()
        {
            index = (index + 1) % images.Count;
            current = images[index];

            Rectangle screen = Screen.PrimaryScreen!.Bounds;
            float aspect = current.Width / (float)Math.Max(1, current.Height);
            int width = (int)Math.Min(screen.Width * MaxWidth, screen.Height * MaxHeight * aspect);
            int height = (int)Math.Round(width / aspect);
            int margin = (int)Math.Round(screen.Width * EdgeMargin);

            // Vinduet går helt ud til skærmkanten, så banneret glider ind fra kanten.
            int windowWidth = width + margin;
            int x = rightSide ? screen.Right - windowWidth : screen.Left;
            int y = screen.Top + (screen.Height - height) / 2;
            Bounds = new Rectangle(x, y, windowWidth, height);
            bannerRect = new Rectangle(rightSide ? 0 : margin, 0, width, height);

            if (SponsorImages.IsAnimated(current))
            {
                ImageAnimator.Animate(current, frameChanged);
            }

            shownAt = clock.Elapsed.TotalSeconds;
            timer.Interval = 33;
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            Render(0, showSeconds + SlideSeconds * 2);
        }

        private void End()
        {
            if (current != null && SponsorImages.IsAnimated(current))
            {
                ImageAnimator.StopAnimate(current, frameChanged);
            }

            current = null;
            shownAt = -1;
            timer.Interval = 1000;
            nextShow = DateTime.Now + every;
            Clear();
        }

        private void Render(double elapsed, double total)
        {
            if (current == null || !IsHandleCreated || IsDisposed || Width <= 1)
            {
                return;
            }

            // Glid ind/ud med blød opbremsning.
            double inAmount = Math.Clamp(elapsed / SlideSeconds, 0, 1);
            double outAmount = Math.Clamp((total - elapsed) / SlideSeconds, 0, 1);
            double shown = Ease(Math.Min(inAmount, outAmount));
            int offset = (int)Math.Round((1 - shown) * Width);
            Rectangle area = bannerRect;
            area.Offset(rightSide ? offset : -offset, 0);

            using Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                if (SponsorImages.IsAnimated(current))
                {
                    ImageAnimator.UpdateFrames(current);
                }

                graphics.DrawImage(current, SponsorImages.Fit(current, area));
            }

            Present(bitmap, (byte)(255 * shown));
        }

        private static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

        private void Clear()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            using Bitmap bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppArgb);
            Present(bitmap, 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Stop();
                timer.Dispose();
                SponsorImages.DisposeAll(images, frameChanged);
            }

            base.Dispose(disposing);
        }

        // ---------------------------------------------------------
        // LAYERED WINDOW
        // ---------------------------------------------------------

        private void Present(Bitmap bitmap, byte opacity)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = SelectObject(memoryDc, hBitmap);

            try
            {
                NativeSize size = new NativeSize { Width = bitmap.Width, Height = bitmap.Height };
                NativePoint source = new NativePoint();
                NativePoint destination = new NativePoint { X = Left, Y = Top };
                BlendFunction blend = new BlendFunction { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = opacity, AlphaFormat = 1 };
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

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

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
