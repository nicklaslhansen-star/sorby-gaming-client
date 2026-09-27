using System;
using System.Drawing;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    // Manuel opsætning på selve PC'en. Bruges, når PC'en er offline og ikke
    // har kunnet hente eventet fra serveren. Online bestemmer serveren, men
    // har eventet ingen baggrund, vises den manuelle baggrund bag QR-koden.
    internal sealed class OfflineSettingsForm : SorbyForm
    {
        private readonly ToggleSwitch manualDurationToggle;
        private readonly NumberStepper durationInput;
        private readonly ThumbnailBox thumbnail;
        private readonly ModernButton removeBackgroundButton;
        private readonly ModernButton placeQrButton;
        private readonly SegmentedControl fitControl;
        private QrLayout? qrLayout;

        public OfflineSettingsForm(CachedEvent? cachedEvent)
        {
            ManualSettings settings = LocalStore.LoadManual();
            qrLayout = settings.QrLayout;

            Title = "Offline-indstillinger";
            Subtitle = cachedEvent == null
                ? "Gemt event: intet · standard 10 minutter"
                : $"Gemt event: {cachedEvent.Name} · {cachedEvent.DurationSeconds / 60} min.";
            Width = S(560);

            int left = Padding24;
            int width = Width - Padding24 * 2;

            // ---------------- Åbningstid ----------------
            CardPanel durationCard = new CardPanel
            {
                Heading = "Åbningstid",
                Description = "Bruges kun, når PC'en kører offline.",
                Location = new Point(left, HeaderHeight + S(4)),
                Size = new Size(width, S(136))
            };

            manualDurationToggle = new ToggleSwitch
            {
                Size = new Size(S(46), S(26)),
                Location = new Point(S(20), durationCard.ContentTop + S(12)),
                Checked = settings.DurationMinutes.HasValue
            };

            TextLabel toggleLabel = new TextLabel("Brug manuel tid", 10.5f)
            {
                Location = new Point(manualDurationToggle.Right + S(12), manualDurationToggle.Top + S(2))
            };
            toggleLabel.Click += (sender, e) => manualDurationToggle.Checked = !manualDurationToggle.Checked;

            durationInput = new NumberStepper
            {
                Size = new Size(S(170), S(44)),
                Location = new Point(width - S(20) - S(170), durationCard.ContentTop + S(3)),
                Value = settings.DurationMinutes ?? Math.Max(1, (cachedEvent?.DurationSeconds ?? LocalStore.DefaultDurationSeconds) / 60),
                Enabled = settings.DurationMinutes.HasValue
            };

            manualDurationToggle.CheckedChanged += (sender, e) => durationInput.Enabled = manualDurationToggle.Checked;
            durationCard.Controls.AddRange(new Control[] { manualDurationToggle, toggleLabel, durationInput });

            // ---------------- Baggrund ----------------
            CardPanel backgroundCard = new CardPanel
            {
                Heading = "Lokal baggrund",
                Description = "Vises offline, og online når eventet ikke har sin egen baggrund.",
                Location = new Point(left, durationCard.Bottom + S(14)),
                Size = new Size(width, S(270))
            };

            int thumbWidth = S(208);
            thumbnail = new ThumbnailBox
            {
                Location = new Point(S(20), backgroundCard.ContentTop + S(4)),
                Size = new Size(thumbWidth, thumbWidth * 9 / 16)
            };

            int buttonsLeft = thumbnail.Right + S(16);
            int buttonsWidth = width - buttonsLeft - S(20);

            ModernButton chooseButton = new ModernButton("Vælg billede", ButtonStyle.Secondary)
            {
                Icon = "",
                Location = new Point(buttonsLeft, thumbnail.Top),
                Size = new Size(buttonsWidth, S(40))
            };
            chooseButton.Click += (sender, e) => ChooseBackground();

            removeBackgroundButton = new ModernButton("Fjern", ButtonStyle.Danger)
            {
                Icon = "",
                Location = new Point(buttonsLeft, chooseButton.Bottom + S(10)),
                Size = new Size(buttonsWidth, S(40))
            };
            removeBackgroundButton.Click += (sender, e) =>
            {
                LocalStore.ClearManualBackground();
                UpdateBackground();
            };

            fitControl = new SegmentedControl("Fyld skærmen", "Vis hele billedet")
            {
                Location = new Point(S(20), thumbnail.Bottom + S(16)),
                Size = new Size(thumbWidth, S(40)),
                SelectedIndex = QrRenderer.IsCover(settings.BackgroundFit) ? 0 : 1
            };
            fitControl.SelectedIndexChanged += (sender, e) => UpdateBackground();

            placeQrButton = new ModernButton("Placér QR-kode", ButtonStyle.Secondary)
            {
                Icon = "",
                Location = new Point(buttonsLeft, fitControl.Top),
                Size = new Size(buttonsWidth, S(40))
            };
            placeQrButton.Click += (sender, e) => PlaceQr();

            backgroundCard.Controls.AddRange(new Control[] { thumbnail, chooseButton, removeBackgroundButton, fitControl, placeQrButton });
            backgroundCard.Height = fitControl.Bottom + S(20);

            // ---------------- Knapper ----------------
            int buttonTop = backgroundCard.Bottom + S(20);

            ModernButton saveButton = new ModernButton("Gem")
            {
                Icon = "",
                Size = new Size(S(140), S(46)),
                Location = new Point(Width - Padding24 - S(140), buttonTop)
            };

            ModernButton cancelButton = new ModernButton("Luk", ButtonStyle.Secondary)
            {
                Size = new Size(S(120), S(46)),
                Location = new Point(saveButton.Left - S(130), buttonTop),
                DialogResult = DialogResult.Cancel
            };

            saveButton.Click += (sender, e) =>
            {
                LocalStore.SaveManual(new ManualSettings
                {
                    DurationMinutes = manualDurationToggle.Checked ? durationInput.Value : null,
                    QrLayout = qrLayout,
                    BackgroundFit = SelectedFit
                });

                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.AddRange(new Control[] { durationCard, backgroundCard, cancelButton, saveButton });
            CancelButton = cancelButton;
            Height = buttonTop + S(46) + Padding24;

            UpdateBackground();
        }

        private string SelectedFit => fitControl.SelectedIndex == 1 ? "contain" : "cover";

        private void PlaceQr()
        {
            using QrPlacementForm placementForm = new QrPlacementForm(LocalStore.LoadManualBackground(), SelectedFit, qrLayout);

            if (placementForm.ShowDialog(this) == DialogResult.OK)
            {
                qrLayout = placementForm.Result;
            }
        }

        private void ChooseBackground()
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Vælg baggrundsbillede",
                Filter = "Billeder|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                // Tjek at filen faktisk er et billede, før den kopieres.
                using (Image.FromFile(dialog.FileName))
                {
                }

                LocalStore.SetManualBackground(dialog.FileName);
            }
            catch (Exception ex)
            {
                SorbyDialog.Show(this, "Billedet kunne ikke bruges", ex.Message);
            }

            UpdateBackground();
        }

        private void UpdateBackground()
        {
            bool hasBackground = LocalStore.HasManualBackground;
            thumbnail.SetImage(LocalStore.LoadManualBackground(), SelectedFit);

            // QR-koden står kun på den manuelle baggrund, når der er en.
            removeBackgroundButton.Enabled = hasBackground;
            placeQrButton.Enabled = hasBackground;
            fitControl.Enabled = hasBackground;
        }
    }
}
