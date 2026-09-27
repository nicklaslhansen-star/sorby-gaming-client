using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    internal enum AdminChoice
    {
        None,
        Settings,
        StopEvent
    }

    // Admin-menuen (Ctrl+Shift+S). Koden tjekkes, når man vælger en handling.
    internal sealed class AdminMenuForm : SorbyForm
    {
        private readonly Func<string, Task<string?>> verifyCode;
        private readonly ModernTextBox codeBox;
        private readonly Banner banner;
        private readonly ActionTile settingsTile;
        private readonly ActionTile stopTile;
        private bool verifying;

        public AdminChoice Choice { get; private set; }

        public AdminMenuForm(string? pcId, bool isOnline, Func<string, Task<string?>> verifyCode)
        {
            this.verifyCode = verifyCode;
            Title = "Admin";
            Subtitle = $"PC {pcId ?? "?"} · {(isOnline ? "Forbundet til serveren" : "Offline")}";
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
                // Enter åbner indstillingerne – aldrig "stop event" ved et uheld.
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    _ = ChooseAsync(AdminChoice.Settings);
                }
            };

            banner = new Banner
            {
                Location = new Point(left, codeBox.Bottom + S(14)),
                Size = new Size(width, S(44))
            };

            settingsTile = new ActionTile("", "Offline-indstillinger", "Åbningstid, lokal baggrund og QR-placering")
            {
                Location = new Point(left, banner.Top),
                Size = new Size(width, S(76))
            };
            settingsTile.Click += async (sender, e) => await ChooseAsync(AdminChoice.Settings);

            stopTile = new ActionTile("", "Stop event", "Lukker Sørby Gaming på denne PC", danger: true)
            {
                Location = new Point(left, settingsTile.Bottom + S(12)),
                Size = new Size(width, S(76))
            };
            stopTile.Click += async (sender, e) => await ChooseAsync(AdminChoice.StopEvent);

            Controls.AddRange(new Control[] { codeLabel, codeBox, banner, settingsTile, stopTile });
            LayoutTiles();
        }

        // Fliserne rykker ned, når der vises en besked.
        private void LayoutTiles()
        {
            int top = banner.Visible ? banner.Bottom + S(14) : codeBox.Bottom + S(20);
            settingsTile.Top = top;
            stopTile.Top = settingsTile.Bottom + S(12);
            Height = stopTile.Bottom + Padding24;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            codeBox.Focus();
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
            settingsTile.Enabled = false;
            stopTile.Enabled = false;
            codeBox.Enabled = false;
            ShowMessage("Tjekker koden...", BannerKind.Info);

            string? error = await verifyCode(codeBox.Text);

            verifying = false;
            settingsTile.Enabled = true;
            stopTile.Enabled = true;
            codeBox.Enabled = true;

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
