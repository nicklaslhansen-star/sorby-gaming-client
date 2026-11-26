using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    // Fuldskærms-spilmenu under en session. Vises, når eventet har flere
    // spil, eller når spilleren prøver at bruge andet end eventets spil.
    internal sealed class GameMenuForm : Form
    {
        private readonly Banner banner;
        private readonly List<Control> tiles = new List<Control>();
        private readonly TextLabel titleLabel;
        private readonly TextLabel subtitleLabel;
        private readonly TextLabel eyebrowLabel;

        public event Action<PlayableGame>? GameChosen;

        public GameMenuForm(List<PlayableGame> games)
        {
            Text = "Sørby Gaming - spil";
            Icon = Theme.AppIcon;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

            eyebrowLabel = new TextLabel("SØRBY ESPORT", 10, true, Theme.Accent);
            titleLabel = new TextLabel(games.Count == 1 ? "Tilbage til spillet" : "Vælg et spil", 26, true);
            subtitleLabel = new TextLabel("Under sessionen kan du kun bruge eventets spil.", 12, color: Theme.Muted);
            banner = new Banner();

            Controls.AddRange(new Control[] { eyebrowLabel, titleLabel, subtitleLabel, banner });

            foreach (PlayableGame game in games)
            {
                ActionTile tile = new ActionTile("", game.Name, $"{PlatformName(game.Installed.Platform)} · Spil nu");
                tile.Click += (sender, e) => GameChosen?.Invoke(game);
                tiles.Add(tile);
                Controls.Add(tile);
            }
        }

        public static string PlatformName(string platform) => platform switch
        {
            "steam" => "Steam",
            "epic" => "Epic Games",
            "riot" => "Riot Games",
            "minecraft" => "Microsoft",
            "roblox" => "Roblox",
            _ => platform
        };

        private int S(float value) => (int)Math.Round(value * DeviceDpi / 96f);

        // Dækker hele skærmen og lægger sig forrest.
        public void Cover()
        {
            Bounds = Screen.PrimaryScreen!.Bounds;
            LayoutContent();

            if (!Visible)
            {
                Show();
            }

            TopMost = true;
            BringToFront();
            Activate();
            NativeWindowHelper.ForceForeground(Handle);
        }

        public void ShowError(string message)
        {
            banner.Show(message);
            LayoutContent();
        }

        private void LayoutContent()
        {
            int width = Math.Min(S(560), ClientSize.Width - S(48));
            int tileHeight = S(76);
            int gap = S(12);
            int contentHeight = S(150) + (banner.Visible ? S(58) : 0) + tiles.Count * (tileHeight + gap);
            int left = (ClientSize.Width - width) / 2;
            int top = Math.Max(S(40), (ClientSize.Height - contentHeight) / 2);

            eyebrowLabel.Location = new Point(left, top);
            titleLabel.Location = new Point(left, eyebrowLabel.Bottom + S(6));
            subtitleLabel.Location = new Point(left, titleLabel.Bottom + S(4));
            top = subtitleLabel.Bottom + S(28);

            if (banner.Visible)
            {
                banner.SetBounds(left, top, width, S(44));
                top = banner.Bottom + S(14);
            }

            foreach (Control tile in tiles)
            {
                tile.SetBounds(left, top, width, tileHeight);
                top += tileHeight + gap;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            tiles.Find(tile => tile.Enabled)?.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Menuen må ikke kunne lukkes af spilleren.
            if (keyData == (Keys.Alt | Keys.F4) || keyData == Keys.Escape)
            {
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool allowClose;

        // Lukkes kun af programmet selv (når sessionen slutter).
        public void ForceClose()
        {
            allowClose = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }
    }
}
