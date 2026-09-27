using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Kontrollerne bygges i kode, ikke i Visual Studio-designeren.
#pragma warning disable WFO1000

namespace SorbyGamingClient
{
    // Fælles udseende for PC-klientens vinduer: samme mørke farver og
    // afrundede former som web-panelet. Alt tegnes selv, så det ser ens ud
    // på Windows 10 og 11.
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(11, 16, 32);
        public static readonly Color Surface = Color.FromArgb(21, 30, 53);
        public static readonly Color SurfaceHover = Color.FromArgb(28, 40, 70);
        public static readonly Color Input = Color.FromArgb(14, 22, 42);
        public static readonly Color Border = Color.FromArgb(52, 70, 110);
        public static readonly Color BorderStrong = Color.FromArgb(75, 95, 141);
        public static readonly Color Text = Color.FromArgb(247, 249, 252);
        public static readonly Color Muted = Color.FromArgb(185, 196, 220);
        public static readonly Color Subtle = Color.FromArgb(127, 143, 179);
        public static readonly Color Accent = Color.FromArgb(101, 215, 255);
        public static readonly Color AccentHover = Color.FromArgb(163, 233, 255);
        public static readonly Color AccentText = Color.FromArgb(6, 33, 53);
        public static readonly Color Secondary = Color.FromArgb(41, 57, 88);
        public static readonly Color SecondaryHover = Color.FromArgb(54, 74, 112);
        public static readonly Color Danger = Color.FromArgb(255, 110, 138);
        public static readonly Color DangerSurface = Color.FromArgb(58, 24, 34);
        public static readonly Color Warning = Color.FromArgb(255, 216, 137);
        public static readonly Color WarningSurface = Color.FromArgb(58, 50, 32);
        public static readonly Color Success = Color.FromArgb(117, 241, 176);

        private static string? iconFamily;
        private static Icon? appIcon;

        // Sørby-logoet fra exe-filen (app.ico), så vinduer og proceslinje viser det.
        public static Icon? AppIcon => appIcon ??= Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        public static Font Font(float size, bool bold = false) =>
            new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);

        // Segoe Fluent Icons (Windows 11) eller Segoe MDL2 Assets (Windows 10).
        public static Font IconFont(float size)
        {
            iconFamily ??= new InstalledFontCollection().Families.Any(family => family.Name == "Segoe Fluent Icons")
                ? "Segoe Fluent Icons"
                : "Segoe MDL2 Assets";
            return new Font(iconFamily, size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return Color.FromArgb(
                (int)(from.A + (to.A - from.A) * amount),
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        public static void Smooth(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static GraphicsPath Round(RectangleF rect, float radius) => QrRenderer.RoundedRect(rect, radius);

        public static void DrawText(Graphics graphics, string text, Font font, Color color, Rectangle bounds,
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis)
        {
            TextRenderer.DrawText(graphics, text, font, bounds, color, flags | TextFormatFlags.NoPadding);
        }

        // Baggrunden bag en afrundet kontrol er forælderens farve.
        public static Color ParentColor(Control control) => control.Parent?.BackColor ?? Background;
    }

    // Lille hjælper, der animerer en værdi mod et mål (hover, fokus osv.).
    internal sealed class Animator : IDisposable
    {
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 15 };
        private readonly Action invalidate;
        private readonly float speed;

        public float Value { get; private set; }
        public float Target { get; private set; }

        public Animator(Action invalidate, float speed = 0.18f)
        {
            this.invalidate = invalidate;
            this.speed = speed;
            timer.Tick += (sender, e) =>
            {
                float difference = Target - Value;

                if (Math.Abs(difference) < 0.01f)
                {
                    Value = Target;
                    timer.Stop();
                }
                else
                {
                    Value += difference * speed * 2.2f;
                }

                invalidate();
            };
        }

        public void AnimateTo(float target)
        {
            Target = target;

            if (Math.Abs(Target - Value) > 0.001f)
            {
                timer.Start();
            }
        }

        public void Jump(float value)
        {
            Target = value;
            Value = value;
            timer.Stop();
            invalidate();
        }

        public void Dispose() => timer.Dispose();
    }

    // ---------------------------------------------------------
    // VINDUE
    // ---------------------------------------------------------

    // Rammeløst vindue med afrundede hjørner, skygge, brandet header og en
    // luk-knap. Kan trækkes i headeren og lukkes med Esc.
    internal class SorbyForm : Form
    {
        private readonly IconButton closeButton;
        private readonly System.Windows.Forms.Timer fadeTimer = new System.Windows.Forms.Timer { Interval = 15 };

        public string Eyebrow { get; set; } = "SØRBY ESPORT";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public bool CloseOnEscape { get; set; } = true;

        protected int HeaderHeight => S(string.IsNullOrEmpty(Subtitle) ? 92 : 116);
        protected int Padding24 => S(28);

        public SorbyForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            Icon = Theme.AppIcon;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Font(10);
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            Opacity = 0;

            closeButton = new IconButton("") { Size = new Size(S(36), S(36)) };
            closeButton.Click += (sender, e) => Close();
            Controls.Add(closeButton);

            fadeTimer.Tick += (sender, e) =>
            {
                Opacity = Math.Min(1, Opacity + 0.12);

                if (Opacity >= 1)
                {
                    fadeTimer.Stop();
                }
            };
        }

        protected int S(float value) => (int)Math.Round(value * DeviceDpi / 96f);

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x20000;
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= CS_DROPSHADOW;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            try
            {
                // Windows 11: afrundede hjørner og mørk systemramme.
                int round = 2;
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
                int dark = 1;
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            }
            catch
            {
                // Ældre Windows: firkantede hjørner er fine.
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            fadeTimer.Start();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            // Kaldes også fra Forms egen konstruktør, før knappen findes.
            if (closeButton == null)
            {
                return;
            }

            closeButton.Location = new Point(ClientSize.Width - closeButton.Width - S(14), S(14));
            closeButton.BringToFront();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Escape && CloseOnEscape)
            {
                e.Handled = true;
                Close();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left && e.Y < HeaderHeight)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
            }
        }

        // Gløden tegnes som baggrund, så gennemsigtige kontroller (luk-knap,
        // tekster) viser den igennem.
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            Graphics graphics = e.Graphics;
            Theme.Smooth(graphics);

            // Blød blå glød i toppen.
            Rectangle glow = new Rectangle(-Width / 4, -S(160), Width + Width / 2, S(280));
            using (GraphicsPath glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(glow);
                using PathGradientBrush brush = new PathGradientBrush(glowPath)
                {
                    CenterColor = Color.FromArgb(46, Theme.Accent),
                    SurroundColors = new[] { Color.FromArgb(0, Theme.Accent) }
                };
                graphics.FillPath(brush, glowPath);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            Theme.Smooth(graphics);

            int left = Padding24;
            using (Font eyebrowFont = Theme.Font(8.5f, true))
            {
                Theme.DrawText(graphics, SpacedOut(Eyebrow), eyebrowFont, Theme.Accent, new Rectangle(left, S(24), Width, S(18)));
            }

            using (Font titleFont = Theme.Font(19, true))
            {
                Theme.DrawText(graphics, Title, titleFont, Theme.Text, new Rectangle(left, S(42), Width - left - S(60), S(40)));
            }

            if (!string.IsNullOrEmpty(Subtitle))
            {
                using Font subtitleFont = Theme.Font(10);
                Theme.DrawText(graphics, Subtitle, subtitleFont, Theme.Muted, new Rectangle(left, S(82), Width - left * 2, S(22)));
            }

            using Pen border = new Pen(Color.FromArgb(70, Theme.BorderStrong));
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        private static string SpacedOut(string text) => string.Join(" ", text.ToCharArray());

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fadeTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    // ---------------------------------------------------------
    // KORT OG TEKST
    // ---------------------------------------------------------

    // Afrundet panel med overskrift. Børn placeres i ContentTop og nedefter.
    internal sealed class CardPanel : Panel
    {
        public string Heading { get; set; } = "";
        public string Description { get; set; } = "";

        public int ContentTop => string.IsNullOrEmpty(Heading) ? Px(18) : string.IsNullOrEmpty(Description) ? Px(50) : Px(72);

        public CardPanel()
        {
            BackColor = Theme.Surface;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        private int Px(float value) => (int)Math.Round(value * DeviceDpi / 96f);

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(Theme.ParentColor(this));
            Theme.Smooth(graphics);

            RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using GraphicsPath path = Theme.Round(bounds, Px(14));
            using SolidBrush fill = new SolidBrush(BackColor);
            using Pen border = new Pen(Theme.Border);
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);

            if (!string.IsNullOrEmpty(Heading))
            {
                using Font headingFont = Theme.Font(11.5f, true);
                Theme.DrawText(graphics, Heading, headingFont, Theme.Text, new Rectangle(Px(20), Px(16), Width - Px(40), Px(24)));
            }

            if (!string.IsNullOrEmpty(Description))
            {
                using Font descriptionFont = Theme.Font(9);
                Theme.DrawText(graphics, Description, descriptionFont, Theme.Subtle, new Rectangle(Px(20), Px(40), Width - Px(40), Px(20)));
            }
        }
    }

    // Tekst, der følger forælderens baggrund.
    internal sealed class TextLabel : Label
    {
        public TextLabel(string text, float size = 10, bool bold = false, Color? color = null)
        {
            Text = text;
            Font = Theme.Font(size, bold);
            ForeColor = color ?? Theme.Text;
            AutoSize = true;
            BackColor = Color.Transparent;
            UseMnemonic = false;
        }
    }

    internal enum BannerKind
    {
        Error,
        Warning,
        Info
    }

    // Besked i selve vinduet (i stedet for en MessageBox).
    internal sealed class Banner : Control
    {
        private BannerKind kind;

        public Banner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.Font(9.5f);
            Visible = false;
        }

        public void Show(string text, BannerKind bannerKind = BannerKind.Error)
        {
            kind = bannerKind;
            Text = text;
            Visible = true;
            Invalidate();
        }

        public void Clear() => Visible = false;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(Theme.ParentColor(this));
            Theme.Smooth(graphics);

            (Color surface, Color foreground, string icon) = kind switch
            {
                BannerKind.Warning => (Theme.WarningSurface, Theme.Warning, ""),
                BannerKind.Info => (Theme.Surface, Theme.Accent, ""),
                _ => (Theme.DangerSurface, Theme.Danger, "")
            };

            using GraphicsPath path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), Height / 4f);
            using SolidBrush fill = new SolidBrush(surface);
            using Pen border = new Pen(Color.FromArgb(90, foreground));
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);

            int iconSize = Height;
            using (Font iconFont = Theme.IconFont(11))
            {
                Theme.DrawText(graphics, icon, iconFont, foreground, new Rectangle(4, 0, iconSize, Height),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            Theme.DrawText(graphics, Text, Font, foreground, new Rectangle(iconSize, 0, Width - iconSize - 12, Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }

    // ---------------------------------------------------------
    // KNAPPER
    // ---------------------------------------------------------

    internal enum ButtonStyle
    {
        Primary,
        Secondary,
        Danger,
        Ghost
    }

    internal class ModernButton : Control, IButtonControl
    {
        private readonly Animator hover;
        private readonly System.Windows.Forms.Timer spinTimer = new System.Windows.Forms.Timer { Interval = 16 };
        private bool pressed;
        private float spin;
        private string? busyText;

        public ButtonStyle Style { get; set; }
        public string Icon { get; set; } = "";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public DialogResult DialogResult { get; set; }

        public ModernButton(string text, ButtonStyle style = ButtonStyle.Primary)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Text = text;
            Style = style;
            Font = Theme.Font(10.5f, true);
            Cursor = Cursors.Hand;
            hover = new Animator(Invalidate);
            spinTimer.Tick += (sender, e) =>
            {
                spin = (spin + 9) % 360;
                Invalidate();
            };
        }

        public bool IsBusy => busyText != null;

        // Viser en spinner og en tekst, mens noget arbejder.
        public void SetBusy(string? text)
        {
            busyText = text;
            Enabled = text == null;

            if (text == null)
            {
                spinTimer.Stop();
            }
            else
            {
                spinTimer.Start();
            }

            Invalidate();
        }

        public void NotifyDefault(bool value)
        {
        }

        public void PerformClick()
        {
            if (Enabled && Visible)
            {
                OnClick(EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Form? form = FindForm();

            if (form != null && DialogResult != DialogResult.None)
            {
                form.DialogResult = DialogResult;
            }

            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover.AnimateTo(1);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            pressed = false;
            hover.AnimateTo(0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            pressed = false;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                PerformClick();
                e.Handled = true;
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(Theme.ParentColor(this));
            Theme.Smooth(graphics);

            (Color fill, Color fillHover, Color text, Color border) = Style switch
            {
                ButtonStyle.Primary => (Theme.Accent, Theme.AccentHover, Theme.AccentText, Color.Empty),
                ButtonStyle.Secondary => (Theme.Secondary, Theme.SecondaryHover, Theme.Text, Color.Empty),
                ButtonStyle.Danger => (Color.FromArgb(0, Theme.DangerSurface), Theme.DangerSurface, Theme.Danger, Color.FromArgb(120, Theme.Danger)),
                _ => (Color.FromArgb(0, Theme.Secondary), Theme.Secondary, Theme.Muted, Color.Empty)
            };

            float amount = hover.Value;
            Color background = Theme.Blend(fill, fillHover, amount);

            if (!Enabled && !IsBusy)
            {
                background = Theme.Blend(Theme.ParentColor(this), background, 0.45f);
                text = Theme.Blend(Theme.ParentColor(this), text, 0.45f);
            }

            RectangleF bounds = new RectangleF(1, 1, Width - 3, Height - 3);

            if (pressed)
            {
                bounds.Inflate(-1, -1);
            }

            float radius = Math.Min(Height / 2f - 1, (float)Math.Round(10 * DeviceDpi / 96f));
            using GraphicsPath path = Theme.Round(bounds, radius);
            using (SolidBrush brush = new SolidBrush(background))
            {
                graphics.FillPath(brush, path);
            }

            if (border != Color.Empty)
            {
                using Pen pen = new Pen(border);
                graphics.DrawPath(pen, path);
            }

            if (Focused && ShowFocusCues)
            {
                RectangleF ring = bounds;
                ring.Inflate(1, 1);
                using GraphicsPath ringPath = Theme.Round(ring, radius + 1);
                using Pen focus = new Pen(Color.FromArgb(200, Theme.Accent), 2);
                graphics.DrawPath(focus, ringPath);
            }

            string label = busyText ?? Text;
            Size textSize = TextRenderer.MeasureText(graphics, label, Font, Size.Empty, TextFormatFlags.NoPadding);
            int iconWidth = 0;
            int gap = (int)Math.Round(8 * DeviceDpi / 96f);

            if (IsBusy || Icon.Length > 0)
            {
                iconWidth = (int)Math.Round(16 * DeviceDpi / 96f);
            }

            int contentWidth = textSize.Width + (iconWidth > 0 ? iconWidth + gap : 0);
            int x = (Width - contentWidth) / 2;

            if (IsBusy)
            {
                Rectangle spinner = new Rectangle(x, (Height - iconWidth) / 2, iconWidth, iconWidth);
                using Pen track = new Pen(Color.FromArgb(60, text), 2.2f);
                using Pen arc = new Pen(text, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawEllipse(track, spinner);
                graphics.DrawArc(arc, spinner, spin, 100);
            }
            else if (Icon.Length > 0)
            {
                using Font iconFont = Theme.IconFont(10.5f);
                Theme.DrawText(graphics, Icon, iconFont, text, new Rectangle(x, 0, iconWidth, Height),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            Theme.DrawText(graphics, label, Font, text,
                new Rectangle(x + (iconWidth > 0 ? iconWidth + gap : 0), 0, textSize.Width + 4, Height));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hover.Dispose();
                spinTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    // Rund knap med kun et ikon (fx luk).
    internal sealed class IconButton : ModernButton
    {
        public IconButton(string icon) : base("", ButtonStyle.Ghost)
        {
            Icon = icon;
            TabStop = false;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            Theme.Smooth(graphics);

            if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
            {
                using SolidBrush brush = new SolidBrush(Theme.Secondary);
                graphics.FillEllipse(brush, 1, 1, Width - 3, Height - 3);
            }

            using Font iconFont = Theme.IconFont(9.5f);
            Theme.DrawText(graphics, Icon, iconFont, Theme.Muted, ClientRectangle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Stort, klikbart valg med ikon, titel og forklaring.
    internal sealed class ActionTile : Control
    {
        private readonly Animator hover;
        private readonly string icon;
        private readonly string title;
        private readonly string description;
        private readonly bool danger;

        public ActionTile(string icon, string title, string description, bool danger = false)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            this.icon = icon;
            this.title = title;
            this.description = description;
            this.danger = danger;
            Cursor = Cursors.Hand;
            hover = new Animator(Invalidate);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover.AnimateTo(1);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover.AnimateTo(0);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                OnClick(EventArgs.Empty);
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(Theme.ParentColor(this));
            Theme.Smooth(graphics);

            float scale = DeviceDpi / 96f;
            Color accent = danger ? Theme.Danger : Theme.Accent;
            float amount = Enabled ? hover.Value : 0;

            RectangleF bounds = new RectangleF(1, 1 + (1 - amount) * 2 * scale, Width - 3, Height - 3 - 2 * scale);
            using GraphicsPath path = Theme.Round(bounds, 14 * scale);
            using (SolidBrush fill = new SolidBrush(Theme.Blend(Theme.Surface, Theme.SurfaceHover, amount)))
            {
                graphics.FillPath(fill, path);
            }

            using (Pen border = new Pen(Theme.Blend(Theme.Border, accent, Focused ? 1 : amount * 0.8f), Focused ? 2 : 1))
            {
                graphics.DrawPath(border, path);
            }

            int iconBox = (int)(44 * scale);
            Rectangle iconRect = new Rectangle((int)(18 * scale), (int)((Height - iconBox) / 2f), iconBox, iconBox);
            using (GraphicsPath iconPath = Theme.Round(iconRect, 12 * scale))
            using (SolidBrush iconFill = new SolidBrush(Color.FromArgb(danger ? 50 : 38, accent)))
            {
                graphics.FillPath(iconFill, iconPath);
            }

            Color textColor = Enabled ? Theme.Text : Theme.Subtle;

            using (Font iconFont = Theme.IconFont(15))
            {
                Theme.DrawText(graphics, icon, iconFont, Enabled ? accent : Theme.Subtle, iconRect,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            int textLeft = iconRect.Right + (int)(16 * scale);
            using (Font titleFont = Theme.Font(11.5f, true))
            {
                Theme.DrawText(graphics, title, titleFont, danger && Enabled ? Theme.Danger : textColor,
                    new Rectangle(textLeft, Height / 2 - (int)(24 * scale), Width - textLeft - (int)(40 * scale), (int)(24 * scale)),
                    TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
            }

            using (Font descriptionFont = Theme.Font(9))
            {
                Theme.DrawText(graphics, description, descriptionFont, Theme.Subtle,
                    new Rectangle(textLeft, Height / 2 + (int)(2 * scale), Width - textLeft - (int)(40 * scale), (int)(20 * scale)),
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
            }

            using Font chevronFont = Theme.IconFont(10);
            Theme.DrawText(graphics, "", chevronFont, Theme.Blend(Theme.Subtle, accent, amount),
                new Rectangle(Width - (int)((34 - 4 * amount) * scale), 0, (int)(20 * scale), Height),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hover.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------
    // INPUT
    // ---------------------------------------------------------

    // Tekstfelt med afrundet ramme, der gløder ved fokus. Adgangskodefelter
    // får et øje, så man kan se det indtastede.
    internal sealed class ModernTextBox : Control
    {
        private readonly TextBox box;
        private readonly Animator focus;
        private readonly bool password;
        private bool revealed;
        private bool error;

        public TextBox InnerTextBox => box;

        public ModernTextBox(bool password = false, float fontSize = 12)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            this.password = password;
            focus = new Animator(Invalidate);

            box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Input,
                ForeColor = Theme.Text,
                Font = Theme.Font(fontSize),
                UseSystemPasswordChar = password
            };

            box.GotFocus += (sender, e) => focus.AnimateTo(1);
            box.LostFocus += (sender, e) => focus.AnimateTo(0);
            box.TextChanged += (sender, e) =>
            {
                SetError(false);
                OnTextChanged(EventArgs.Empty);
            };
            box.KeyDown += (sender, e) => OnKeyDown(e);

            Controls.Add(box);
            Cursor = Cursors.IBeam;
        }

        [AllowNull]
        public override string Text
        {
            get => box?.Text ?? "";
            set
            {
                if (box != null)
                {
                    box.Text = value ?? "";
                }
            }
        }

        public string Placeholder
        {
            get => box.PlaceholderText;
            set => box.PlaceholderText = value;
        }

        public int MaxLength
        {
            get => box.MaxLength;
            set => box.MaxLength = value;
        }

        public void SetError(bool value)
        {
            if (error != value)
            {
                error = value;
                Invalidate();
            }
        }

        public new void Focus()
        {
            box.Focus();
            box.SelectAll();
        }

        public void Clear() => box.Clear();

        private int EyeWidth => password ? Height : 0;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int padding = (int)(14 * DeviceDpi / 96f);
            box.Location = new Point(padding, (Height - box.Height) / 2);
            box.Width = Width - padding * 2 - EyeWidth;
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            box.Enabled = Enabled;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (password && e.X >= Width - EyeWidth)
            {
                revealed = !revealed;
                box.UseSystemPasswordChar = !revealed;
                Invalidate();
            }

            box.Focus();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = password && e.X >= Width - EyeWidth ? Cursors.Hand : Cursors.IBeam;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(Theme.ParentColor(this));
            Theme.Smooth(graphics);

            float scale = DeviceDpi / 96f;
            float amount = focus.Value;
            Color accent = error ? Theme.Danger : Theme.Accent;

            if (amount > 0.01f || error)
            {
                using GraphicsPath glowPath = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 12 * scale);
                using Pen glow = new Pen(Color.FromArgb((int)(70 * Math.Max(amount, error ? 1 : 0)), accent), 4 * scale);
                graphics.DrawPath(glow, glowPath);
            }

            RectangleF bounds = new RectangleF(2 * scale, 2 * scale, Width - 1 - 4 * scale, Height - 1 - 4 * scale);
            using GraphicsPath path = Theme.Round(bounds, 10 * scale);
            using (SolidBrush fill = new SolidBrush(Enabled ? Theme.Input : Theme.Blend(Theme.ParentColor(this), Theme.Input, 0.5f)))
            {
                graphics.FillPath(fill, path);
            }

            using (Pen border = new Pen(error ? Theme.Danger : Theme.Blend(Theme.BorderStrong, Theme.Accent, amount), 1.2f))
            {
                graphics.DrawPath(border, path);
            }

            if (password)
            {
                using Font iconFont = Theme.IconFont(11);
                Theme.DrawText(graphics, revealed ? "" : "", iconFont, Theme.Subtle,
                    new Rectangle(Width - EyeWidth, 0, EyeWidth - (int)(6 * scale), Height),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                focus.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    internal sealed class ToggleSwitch : Control
    {
        private readonly Animator knob;
        private bool isChecked;

        public event EventHandler? CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Cursor = Cursors.Hand;
            knob = new Animator(Invalidate, 0.25f);
        }

        public bool Checked
        {
            get => isChecked;
            set
            {
                if (isChecked == value)
                {
                    return;
                }

                isChecked = value;

                if (IsHandleCreated)
                {
                    knob.AnimateTo(value ? 1 : 0);
                }
                else
                {
                    knob.Jump(value ? 1 : 0);
                }

                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(Theme.ParentColor(this));
            Theme.Smooth(graphics);

            float amount = knob.Value;
            RectangleF track = new RectangleF(1, 1, Width - 3, Height - 3);
            using GraphicsPath path = Theme.Round(track, track.Height / 2);
            using (SolidBrush fill = new SolidBrush(Theme.Blend(Theme.Secondary, Theme.Accent, amount)))
            {
                graphics.FillPath(fill, path);
            }

            if (Focused && ShowFocusCues)
            {
                using Pen focus = new Pen(Theme.AccentHover, 1.5f);
                graphics.DrawPath(focus, path);
            }

            float size = track.Height - 6;
            float x = track.X + 3 + (track.Width - size - 6) * amount;
            using SolidBrush knobBrush = new SolidBrush(Theme.Blend(Theme.Muted, Color.White, amount));
            graphics.FillEllipse(knobBrush, x, track.Y + 3, size, size);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                knob.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    // Vælg én af flere muligheder; markeringen glider mellem dem.
    internal sealed class SegmentedControl : Control
    {
        private readonly string[] options;
        private readonly Animator slide;
        private int selectedIndex;
        private int hoverIndex = -1;

        public event EventHandler? SelectedIndexChanged;

        public SegmentedControl(params string[] options)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            this.options = options;
            Font = Theme.Font(9.5f, true);
            Cursor = Cursors.Hand;
            slide = new Animator(Invalidate, 0.22f);
        }

        public int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                value = Math.Clamp(value, 0, options.Length - 1);

                if (selectedIndex == value)
                {
                    return;
                }

                selectedIndex = value;

                if (IsHandleCreated)
                {
                    slide.AnimateTo(value);
                }
                else
                {
                    slide.Jump(value);
                }

                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private float SegmentWidth => (Width - 8f) / options.Length;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            SelectedIndex = (int)((e.X - 4) / SegmentWidth);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = Math.Clamp((int)((e.X - 4) / SegmentWidth), 0, options.Length - 1);

            if (index != hoverIndex)
            {
                hoverIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverIndex = -1;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Left) SelectedIndex--;
            if (e.KeyCode == Keys.Right) SelectedIndex++;
        }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(Theme.ParentColor(this));
            Theme.Smooth(graphics);

            float scale = DeviceDpi / 96f;
            using GraphicsPath outer = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), 11 * scale);
            using (SolidBrush fill = new SolidBrush(Theme.Input))
            using (Pen border = new Pen(Focused && ShowFocusCues ? Theme.Accent : Theme.BorderStrong))
            {
                graphics.FillPath(fill, outer);
                graphics.DrawPath(border, outer);
            }

            RectangleF thumb = new RectangleF(4 + slide.Value * SegmentWidth, 4, SegmentWidth, Height - 9);
            using (GraphicsPath thumbPath = Theme.Round(thumb, 8 * scale))
            using (SolidBrush thumbBrush = new SolidBrush(Enabled ? Theme.Accent : Theme.Secondary))
            {
                graphics.FillPath(thumbBrush, thumbPath);
            }

            for (int i = 0; i < options.Length; i++)
            {
                Rectangle segment = new Rectangle((int)(4 + i * SegmentWidth), 0, (int)SegmentWidth, Height);
                bool selected = Math.Abs(slide.Value - i) < 0.5f;
                Color color = !Enabled ? Theme.Subtle : selected ? Theme.AccentText : i == hoverIndex ? Theme.Text : Theme.Muted;
                Theme.DrawText(graphics, options[i], Font, color, segment,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                slide.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    // Tal med − og + (hold knappen nede for at tælle hurtigt, eller scroll).
    internal sealed class NumberStepper : Control
    {
        private readonly System.Windows.Forms.Timer repeatTimer = new System.Windows.Forms.Timer();
        private int value = 10;
        private int direction;
        private int repeats;
        private int hoverSide;

        public int Minimum { get; set; } = 1;
        public int Maximum { get; set; } = 1440;
        public string Unit { get; set; } = "min";

        public event EventHandler? ValueChanged;

        public NumberStepper()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.Font(13, true);

            repeatTimer.Tick += (sender, e) =>
            {
                repeats++;
                repeatTimer.Interval = repeats > 10 ? 40 : 90;
                Step(direction * (repeats > 25 ? 5 : 1));
            };
        }

        public int Value
        {
            get => value;
            set
            {
                int clamped = Math.Clamp(value, Minimum, Maximum);

                if (clamped != this.value)
                {
                    this.value = clamped;
                    Invalidate();
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private int ButtonWidth => Height;

        private void Step(int amount) => Value += amount;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            direction = e.X < ButtonWidth ? -1 : e.X > Width - ButtonWidth ? 1 : 0;

            if (direction != 0)
            {
                Step(direction);
                repeats = 0;
                repeatTimer.Interval = 400;
                repeatTimer.Start();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            repeatTimer.Stop();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int side = e.X < ButtonWidth ? -1 : e.X > Width - ButtonWidth ? 1 : 0;
            Cursor = side != 0 && Enabled ? Cursors.Hand : Cursors.Default;

            if (side != hoverSide)
            {
                hoverSide = side;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverSide = 0;
            repeatTimer.Stop();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Step(e.Delta > 0 ? 1 : -1);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode is Keys.Up or Keys.Right) Step(e.Shift ? 5 : 1);
            if (e.KeyCode is Keys.Down or Keys.Left) Step(e.Shift ? -5 : -1);
        }

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(Theme.ParentColor(this));
            Theme.Smooth(graphics);

            float scale = DeviceDpi / 96f;
            float opacity = Enabled ? 1f : 0.4f;
            Color parent = Theme.ParentColor(this);

            using GraphicsPath path = Theme.Round(new RectangleF(1, 1, Width - 3, Height - 3), 10 * scale);
            using (SolidBrush fill = new SolidBrush(Theme.Blend(parent, Theme.Input, opacity)))
            using (Pen border = new Pen(Theme.Blend(parent, Focused ? Theme.Accent : Theme.BorderStrong, opacity), 1.2f))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            foreach (int side in new[] { -1, 1 })
            {
                Rectangle area = side < 0
                    ? new Rectangle(4, 4, ButtonWidth - 8, Height - 8)
                    : new Rectangle(Width - ButtonWidth + 4, 4, ButtonWidth - 8, Height - 8);

                if (hoverSide == side && Enabled)
                {
                    using GraphicsPath hoverPath = Theme.Round(area, 8 * scale);
                    using SolidBrush hover = new SolidBrush(Theme.Secondary);
                    graphics.FillPath(hover, hoverPath);
                }

                using Font iconFont = Theme.IconFont(10);
                Theme.DrawText(graphics, side < 0 ? "" : "", iconFont, Theme.Blend(parent, Theme.Accent, opacity), area,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            Rectangle middle = new Rectangle(ButtonWidth, 0, Width - ButtonWidth * 2, Height);
            string text = $"{value}";
            using Font unitFont = Theme.Font(9.5f);
            Size valueSize = TextRenderer.MeasureText(graphics, text, Font, Size.Empty, TextFormatFlags.NoPadding);
            Size unitSize = TextRenderer.MeasureText(graphics, Unit, unitFont, Size.Empty, TextFormatFlags.NoPadding);
            int gap = (int)(5 * scale);
            int x = middle.X + (middle.Width - valueSize.Width - gap - unitSize.Width) / 2;
            Theme.DrawText(graphics, text, Font, Theme.Blend(parent, Theme.Text, opacity), new Rectangle(x, 0, valueSize.Width + 2, Height));
            Theme.DrawText(graphics, Unit, unitFont, Theme.Blend(parent, Theme.Subtle, opacity),
                new Rectangle(x + valueSize.Width + gap, (int)(2 * scale), unitSize.Width + 2, Height));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                repeatTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    internal sealed class ModernSlider : Control
    {
        private double value;
        private bool dragging;
        private bool hover;

        public double Minimum { get; set; }
        public double Maximum { get; set; } = 1;

        public event EventHandler? ValueChanged;

        public ModernSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
        }

        public double Value
        {
            get => value;
            set
            {
                double clamped = Math.Clamp(value, Minimum, Maximum);

                if (Math.Abs(clamped - this.value) > 1e-9)
                {
                    this.value = clamped;
                    Invalidate();
                }
            }
        }

        private float KnobRadius => Height / 2f - 2;

        private void SetFromMouse(int x)
        {
            float left = KnobRadius + 2;
            float width = Width - left * 2;
            Value = Minimum + (Maximum - Minimum) * Math.Clamp((x - left) / width, 0, 1);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            dragging = true;
            SetFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (dragging)
            {
                SetFromMouse(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(Theme.ParentColor(this));
            Theme.Smooth(graphics);

            float radius = KnobRadius;
            float left = radius + 2;
            float width = Width - left * 2;
            float fraction = (float)((Value - Minimum) / Math.Max(1e-9, Maximum - Minimum));
            float trackHeight = Math.Max(4, Height / 6f);
            float y = Height / 2f;

            using (GraphicsPath track = Theme.Round(new RectangleF(left, y - trackHeight / 2, width, trackHeight), trackHeight / 2))
            using (SolidBrush trackBrush = new SolidBrush(Theme.Secondary))
            {
                graphics.FillPath(trackBrush, track);
            }

            if (fraction > 0)
            {
                using GraphicsPath filled = Theme.Round(new RectangleF(left, y - trackHeight / 2, width * fraction, trackHeight), trackHeight / 2);
                using SolidBrush fill = new SolidBrush(Theme.Accent);
                graphics.FillPath(fill, filled);
            }

            float knobX = left + width * fraction;
            float knobRadius = radius * (hover || dragging ? 1f : 0.85f);

            if (hover || dragging)
            {
                using SolidBrush halo = new SolidBrush(Color.FromArgb(50, Theme.Accent));
                graphics.FillEllipse(halo, knobX - radius - 1, y - radius - 1, radius * 2 + 2, radius * 2 + 2);
            }

            using SolidBrush knob = new SolidBrush(Color.White);
            graphics.FillEllipse(knob, knobX - knobRadius * 0.7f, y - knobRadius * 0.7f, knobRadius * 1.4f, knobRadius * 1.4f);
        }
    }

    // Miniature af et billede i skærmformat, fx den lokale baggrund.
    internal sealed class ThumbnailBox : Control
    {
        private Image? image;
        private string fit = "cover";

        public ThumbnailBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void SetImage(byte[]? bytes, string backgroundFit)
        {
            image?.Dispose();
            image = null;
            fit = backgroundFit;

            if (bytes != null)
            {
                try
                {
                    using System.IO.MemoryStream stream = new System.IO.MemoryStream(bytes);
                    using Image temporary = Image.FromStream(stream);
                    image = QrRenderer.ComposeBackground(temporary, Size, fit);
                }
                catch
                {
                    image = null;
                }
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(Theme.ParentColor(this));
            Theme.Smooth(graphics);

            float scale = DeviceDpi / 96f;
            RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using GraphicsPath path = Theme.Round(bounds, 10 * scale);

            if (image != null)
            {
                graphics.SetClip(path);
                graphics.DrawImage(image, 0, 0, Width, Height);
                graphics.ResetClip();
                using Pen border = new Pen(Theme.Border);
                graphics.DrawPath(border, path);
                return;
            }

            using (SolidBrush fill = new SolidBrush(Theme.Input))
            {
                graphics.FillPath(fill, path);
            }

            using (Pen dashed = new Pen(Theme.BorderStrong) { DashStyle = DashStyle.Dash })
            {
                graphics.DrawPath(dashed, path);
            }

            using Font iconFont = Theme.IconFont(18);
            using Font textFont = Theme.Font(8.5f);
            Theme.DrawText(graphics, "", iconFont, Theme.Subtle, new Rectangle(0, 0, Width, Height / 2 + (int)(8 * scale)),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);
            Theme.DrawText(graphics, "Ingen baggrund", textFont, Theme.Subtle, new Rectangle(0, Height / 2 + (int)(12 * scale), Width, (int)(20 * scale)),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                image?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------
    // DIALOG
    // ---------------------------------------------------------

    // Erstatter MessageBox med en dialog i samme stil.
    internal sealed class SorbyDialog : SorbyForm
    {
        private SorbyDialog(string title, string message, string confirmText, string? cancelText, bool danger)
        {
            Title = title;
            Width = S(460);

            TextLabel messageLabel = new TextLabel(message, 10.5f, color: Theme.Muted)
            {
                AutoSize = false,
                Location = new Point(Padding24, HeaderHeight - S(8)),
                Width = Width - Padding24 * 2
            };
            messageLabel.Height = TextRenderer.MeasureText(message, messageLabel.Font, new Size(messageLabel.Width, 0), TextFormatFlags.WordBreak).Height + S(6);

            int buttonTop = messageLabel.Bottom + S(24);
            int buttonWidth = S(140);

            ModernButton confirmButton = new ModernButton(confirmText, danger ? ButtonStyle.Danger : ButtonStyle.Primary)
            {
                Size = new Size(buttonWidth, S(42)),
                Location = new Point(Width - Padding24 - buttonWidth, buttonTop),
                DialogResult = DialogResult.OK
            };

            Controls.Add(messageLabel);
            Controls.Add(confirmButton);
            AcceptButton = confirmButton;

            if (cancelText != null)
            {
                ModernButton cancelButton = new ModernButton(cancelText, ButtonStyle.Secondary)
                {
                    Size = new Size(buttonWidth, S(42)),
                    Location = new Point(confirmButton.Left - buttonWidth - S(10), buttonTop),
                    DialogResult = DialogResult.Cancel
                };
                Controls.Add(cancelButton);
                CancelButton = cancelButton;
            }

            Height = buttonTop + S(42) + Padding24;
        }

        public static void Show(IWin32Window? owner, string title, string message)
        {
            using SorbyDialog dialog = new SorbyDialog(title, message, "OK", null, false);
            dialog.ShowDialog(owner);
        }

        public static bool Confirm(IWin32Window? owner, string title, string message, string confirmText, string cancelText = "Annuller", bool danger = false)
        {
            using SorbyDialog dialog = new SorbyDialog(title, message, confirmText, cancelText, danger);
            return dialog.ShowDialog(owner) == DialogResult.OK;
        }
    }
}
