using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    internal enum AdminChoice
    {
        None,
        Settings,
        StopEvent,
        EndSession,
        Games
    }

    // Admin-menuen (Ctrl+Shift+S, eller Ctrl+Shift+F under en session).
    // Koden tjekkes, når man vælger en handling.
    internal sealed class AdminMenuForm : SorbyForm
    {
        private readonly Func<string, Task<string?>> verifyCode;
        private readonly ModernTextBox codeBox;
        private readonly Banner banner;
        private readonly List<ActionTile> tiles = new List<ActionTile>();
        private bool verifying;

        public AdminChoice Choice { get; private set; }

        // enterChoice: hvad Enter i kodefeltet gør. Ctrl+Shift+F åbner menuen
        // med "Afslut session" som Enter-handling.
        public AdminMenuForm(string? pcId, bool isOnline, string version, Func<string, Task<string?>> verifyCode,
            bool inSession = false, bool hasGames = false, AdminChoice enterChoice = AdminChoice.Settings)
        {
            this.verifyCode = verifyCode;
            Title = enterChoice == AdminChoice.EndSession ? "Afslut session" : "Admin";
            Subtitle = $"PC {pcId ?? "?"} · {(isOnline ? "Forbundet til serveren" : "Offline")} · {UpdateService.DisplayVersion(version)}";
            Width = S(480);

            int left = Padding24;
            int width = Width - Padding24 * 2;
            int top = HeaderHeight + S(6);

            TextLabel codeLabel = new TextLabel("Admin-kode", 9.5f, true, Theme.Muted) { Location = new Point(left, top) };
            codeBox = new ModernTextBox(password: true, fontSize: 13)
            {
                Location = new Point(left, top + S(24)),
                Size = new Size(width, S(48)),
                Placeholder = "Indtast koden for at fortsætte"
            };
            codeBox.KeyDown += (sender, e) =>
            {
                // Enter vælger aldrig "stop event" ved et uheld.
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    _ = ChooseAsync(enterChoice == AdminChoice.EndSession && inSession ? AdminChoice.EndSession : AdminChoice.Settings);
                }
            };

            banner = new Banner
            {
                Location = new Point(left, codeBox.Bottom + S(14)),
                Size = new Size(width, S(44))
            };

            if (inSession)
            {
                AddTile("", "Afslut session", "Lukker spillet og viser QR-koden igen", AdminChoice.EndSession);
            }

            if (hasGames)
            {
                AddTile("", "Spil på denne PC", "Se hvilke af eventets spil PC'en har fundet", AdminChoice.Games);
            }

            AddTile("", "Offline-indstillinger", "Åbningstid, lokal baggrund og QR-placering", AdminChoice.Settings);
            AddTile("", "Stop event", "Lukker Sørby Gaming på denne PC", AdminChoice.StopEvent, danger: true);

            Controls.AddRange(new Control[] { codeLabel, codeBox, banner });
            LayoutTiles();
        }

        private void AddTile(string icon, string title, string description, AdminChoice choice, bool danger = false)
        {
            ActionTile tile = new ActionTile(icon, title, description, danger) { Size = new Size(Width - Padding24 * 2, S(76)) };
            tile.Click += async (sender, e) => await ChooseAsync(choice);
            tiles.Add(tile);
            Controls.Add(tile);
        }

        // Fliserne rykker ned, når der vises en besked.
        private void LayoutTiles()
        {
            int top = banner.Visible ? banner.Bottom + S(14) : codeBox.Bottom + S(20);

            foreach (ActionTile tile in tiles)
            {
                tile.Location = new Point(Padding24, top);
                top = tile.Bottom + S(12);
            }

            Height = top - S(12) + Padding24;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            codeBox.Focus();
        }

        private void SetEnabled(bool enabled)
        {
            tiles.ForEach(tile => tile.Enabled = enabled);
            codeBox.Enabled = enabled;
        }

        private async Task ChooseAsync(AdminChoice choice)
        {
            if (verifying)
            {
                return;
            }

            if (string.IsNullOrEmpty(codeBox.Text))
            {
                ShowMessage("Indtast admin-koden først.");
                codeBox.SetError(true);
                codeBox.Focus();
                return;
            }

            verifying = true;
            SetEnabled(false);
            ShowMessage("Tjekker koden...", BannerKind.Info);

            string? error = await verifyCode(codeBox.Text);

            verifying = false;
            SetEnabled(true);

            if (error != null)
            {
                ShowMessage(error);
                codeBox.Clear();
                codeBox.SetError(true);
                codeBox.Focus();
                return;
            }

            if (choice == AdminChoice.StopEvent && !SorbyDialog.Confirm(this, "Stop event?",
                    "Sørby Gaming lukkes på denne PC, og skærmen låses op. Programmet skal startes igen for at køre eventet.",
                    "Stop event", danger: true))
            {
                banner.Clear();
                LayoutTiles();
                return;
            }

            Choice = choice;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowMessage(string message, BannerKind kind = BannerKind.Error)
        {
            banner.Show(message, kind);
            LayoutTiles();
        }
    }
}
