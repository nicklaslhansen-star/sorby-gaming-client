using System;
using System.Drawing;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    // Første vindue, når programmet starter: PC-nummer og admin-kode.
    // Fejl vises i vinduet, og knappen viser, hvad der sker lige nu.
    internal sealed class PcSetupForm : SorbyForm
    {
        private readonly ModernTextBox pcBox;
        private readonly ModernTextBox codeBox;
        private readonly ModernButton startButton;
        private readonly Banner banner;

        public event EventHandler? StartRequested;

        public string PcNumber => pcBox.Text.Trim();
        public string AdminCode => codeBox.Text;

        public PcSetupForm(string version, string? savedPcId)
        {
            Title = "Opsæt denne PC";
            Subtitle = "Vælg PC'ens nummer og log ind med admin-koden.";
            ShowInTaskbar = true;
            Text = "Sørby Gaming";
            Width = S(460);

            int left = Padding24;
            int width = Width - Padding24 * 2;
            int top = HeaderHeight + S(6);

            TextLabel pcLabel = new TextLabel("PC-nummer", 9.5f, true, Theme.Muted) { Location = new Point(left, top) };
            pcBox = new ModernTextBox(fontSize: 13)
            {
                Location = new Point(left, top + S(24)),
                Size = new Size(width, S(48)),
                Text = string.IsNullOrWhiteSpace(savedPcId) ? "01" : savedPcId,
                MaxLength = 50
            };

            top = pcBox.Bottom + S(18);
            TextLabel codeLabel = new TextLabel("Admin-kode", 9.5f, true, Theme.Muted) { Location = new Point(left, top) };
            codeBox = new ModernTextBox(password: true, fontSize: 13)
            {
                Location = new Point(left, top + S(24)),
                Size = new Size(width, S(48)),
                Placeholder = "Indtast koden"
            };

            banner = new Banner
            {
                Location = new Point(left, codeBox.Bottom + S(16)),
                Size = new Size(width, S(44))
            };

            startButton = new ModernButton("Start PC")
            {
                Icon = "",
                Location = new Point(left, banner.Bottom + S(16)),
                Size = new Size(width, S(50))
            };
            startButton.Click += (sender, e) => Submit();

            TextLabel versionLabel = new TextLabel($"Version {version}", 8.5f, color: Theme.Subtle)
            {
                Location = new Point(left, startButton.Bottom + S(18))
            };

            Controls.AddRange(new Control[] { pcLabel, pcBox, codeLabel, codeBox, banner, startButton, versionLabel });
            AcceptButton = startButton;
            Height = versionLabel.Bottom + S(22);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            codeBox.Focus();
        }

        private void Submit()
        {
            if (startButton.IsBusy)
            {
                return;
            }

            banner.Clear();

            if (string.IsNullOrWhiteSpace(PcNumber))
            {
                ShowError("Indtast et PC-nummer.");
                pcBox.SetError(true);
                pcBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(AdminCode))
            {
                ShowError("Indtast admin-koden.");
                codeBox.SetError(true);
                codeBox.Focus();
                return;
            }

            StartRequested?.Invoke(this, EventArgs.Empty);
        }

        // null = færdig; ellers vises teksten på knappen med en spinner.
        public void SetBusy(string? text)
        {
            startButton.SetBusy(text);
            pcBox.Enabled = text == null;
            codeBox.Enabled = text == null;
        }

        public void ShowError(string message, BannerKind kind = BannerKind.Error) => banner.Show(message, kind);

        public void RejectAdminCode(string message)
        {
            SetBusy(null);
            ShowError(message);
            codeBox.Clear();
            codeBox.SetError(true);
            codeBox.Focus();
        }

        public void RejectPcNumber(string message)
        {
            SetBusy(null);
            ShowError(message);
            pcBox.SetError(true);
            pcBox.Focus();
        }
    }
}
