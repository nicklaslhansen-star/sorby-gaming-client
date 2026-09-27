using System;
using System.Drawing;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    // Manuel opsætning på selve PC'en. Bruges, når PC'en er offline og ikke
    // har kunnet hente eventet fra serveren. Online bestemmer serveren, men
    // har eventet ingen baggrund, vises den manuelle baggrund bag QR-koden.
    internal sealed class OfflineSettingsForm : Form
    {
        private readonly CheckBox manualDurationCheckBox;
        private readonly NumericUpDown durationInput;
        private readonly Label backgroundStatusLabel;
        private readonly Button placeQrButton;
        private QrLayout? qrLayout;

        public OfflineSettingsForm(CachedEvent? cachedEvent)
        {
            ManualSettings settings = LocalStore.LoadManual();
            qrLayout = settings.QrLayout;

            Text = "Sørby Gaming - Offline-indstillinger";
            Size = new Size(520, 400);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;
            Font = new Font("Arial", 11);

            Label eventLabel = new Label
            {
                Text = cachedEvent == null
                    ? "Gemt event: intet (standard 10 minutter)"
                    : $"Gemt event: {cachedEvent.Name} ({cachedEvent.DurationSeconds / 60} min.)",
                AutoSize = true,
                Location = new Point(25, 20)
            };

            manualDurationCheckBox = new CheckBox
            {
                Text = "Brug manuel åbningstid (minutter):",
                AutoSize = true,
                Location = new Point(25, 65),
                Checked = settings.DurationMinutes.HasValue
            };

            durationInput = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 1440,
                Value = settings.DurationMinutes ?? Math.Max(1, (cachedEvent?.DurationSeconds ?? LocalStore.DefaultDurationSeconds) / 60),
                Location = new Point(330, 63),
                Width = 90,
                Enabled = settings.DurationMinutes.HasValue
            };

            manualDurationCheckBox.CheckedChanged += (sender, e) =>
                durationInput.Enabled = manualDurationCheckBox.Checked;

            backgroundStatusLabel = new Label
            {
                AutoSize = true,
                Location = new Point(25, 120)
            };

            Button chooseBackgroundButton = new Button
            {
                Text = "Vælg baggrund...",
                Location = new Point(25, 150),
                Width = 180,
                Height = 34,
                ForeColor = Color.Black,
                BackColor = Color.White
            };

            Button clearBackgroundButton = new Button
            {
                Text = "Fjern manuel baggrund",
                Location = new Point(220, 150),
                Width = 200,
                Height = 34,
                ForeColor = Color.Black,
                BackColor = Color.White
            };

            placeQrButton = new Button
            {
                Text = "Placér QR-kode på baggrunden...",
                Location = new Point(25, 195),
                Width = 395,
                Height = 34,
                ForeColor = Color.Black,
                BackColor = Color.White
            };

            placeQrButton.Click += (sender, e) => PlaceQr();

            chooseBackgroundButton.Click += (sender, e) => ChooseBackground();
            clearBackgroundButton.Click += (sender, e) =>
            {
                LocalStore.ClearManualBackground();
                UpdateBackgroundStatus();
            };

            Button saveButton = new Button
            {
                Text = "GEM",
                Location = new Point(150, 280),
                Width = 100,
                Height = 38,
                ForeColor = Color.Black,
                BackColor = Color.White
            };

            Button cancelButton = new Button
            {
                Text = "Luk",
                Location = new Point(265, 280),
                Width = 100,
                Height = 38,
                ForeColor = Color.Black,
                BackColor = Color.White,
                DialogResult = DialogResult.Cancel
            };

            saveButton.Click += (sender, e) =>
            {
                LocalStore.SaveManual(new ManualSettings
                {
                    DurationMinutes = manualDurationCheckBox.Checked ? (int)durationInput.Value : null,
                    QrLayout = qrLayout
                });

                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.AddRange(new Control[]
            {
                eventLabel,
                manualDurationCheckBox,
                durationInput,
                backgroundStatusLabel,
                chooseBackgroundButton,
                clearBackgroundButton,
                placeQrButton,
                saveButton,
                cancelButton
            });

            AcceptButton = saveButton;
            CancelButton = cancelButton;
            UpdateBackgroundStatus();
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
                MessageBox.Show(this, $"Billedet kunne ikke bruges.\n\n{ex.Message}", "Sørby Gaming",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            UpdateBackgroundStatus();
        }

        private void PlaceQr()
        {
            using QrPlacementForm placementForm = new QrPlacementForm(LocalStore.LoadManualBackground(), qrLayout);

            if (placementForm.ShowDialog(this) == DialogResult.OK)
            {
                qrLayout = placementForm.Result;
            }
        }

        private void UpdateBackgroundStatus()
        {
            backgroundStatusLabel.Text = LocalStore.HasManualBackground
                ? "Manuel baggrund: valgt (offline, og online uden eventbaggrund)"
                : "Manuel baggrund: ingen (eventets baggrund bruges)";

            // QR-koden står kun på den manuelle baggrund, når der er en.
            placeQrButton.Enabled = LocalStore.HasManualBackground;
        }
    }
}
