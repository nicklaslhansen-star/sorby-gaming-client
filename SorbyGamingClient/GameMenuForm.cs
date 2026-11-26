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
        private readonly string menuTitle;
        private readonly System.Windows.Forms.Timer dotsTimer = new System.Windows.Forms.Timer { Interval = 500 };
        private string? loadingName;
        private int dots;

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
            menuTitle = games.Count == 1 ? "Tilbage til spillet" : "Vælg et spil";
            titleLabel = new TextLabel(menuTitle, 26, true);
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

            dotsTimer.Tick += (sender, e) =>
            {
                dots = (dots + 1) % 4;
                titleLabel.Text = $"Starter {loadingName}" + new string('.', dots);
            };
        }

        public bool IsLoading => loadingName != null;

        // "Starter spillet ..." - dækker skærmen, mens spillet åbner, så
        // spilleren ikke kan klikke rundt på skrivebordet imens.
        public void ShowLoading(string gameName)
        {
            loadingName = gameName;
            dots = 3;
            titleLabel.Text = $"Starter {gameName}...";
            subtitleLabel.Text = "Spillet åbner af sig selv om et øjeblik. Vent venligst.";
            banner.Visible = false;
            tiles.ForEach(tile => tile.Visible = false);
            dotsTimer.Start();
            LayoutContent();
        }

        // Tilbage til spil-valget.
        public void ShowChoices()
        {
            loadingName = null;
            dotsTimer.Stop();
            titleLabel.Text = menuTitle;
            subtitleLabel.Text = "Under sessionen kan du kun bruge eventets spil.";
            tiles.ForEach(tile => tile.Visible = true);
            LayoutContent();
        }

        // Ligger forrest uden at tage fokus fra spillet, der er ved at starte.
        public void KeepOnTop()
        {
            if (!Visible)
            {
                return;
            }

            NativeWindowHelper.SetTopMostNoActivate(Handle);
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
            ShowChoices();
            banner.Show(message);
            LayoutContent();
        }

        private void LayoutContent()
        {
            int width = Math.Min(S(560), ClientSize.Width - S(48));
            int tileHeight = S(76);
            int gap = S(12);
            int visibleTiles = loadingName == null ? tiles.Count : 0;
            int contentHeight = S(150) + (banner.Visible ? S(58) : 0) + visibleTiles * (tileHeight + gap);
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
            dotsTimer.Stop();
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dotsTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
