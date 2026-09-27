using System;
using System.Drawing;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    // Manuel opsætning på selve PC'en. Bruges, når PC'en er offline og ikke
    // har kunnet hente eventet fra serveren. Online bestemmer serveren.
    internal sealed class OfflineSettingsForm : Form
    {
        private readonly CheckBox manualDurationCheckBox;
        private readonly NumericUpDown durationInput;
        private readonly Label backgroundStatusLabel;

        public OfflineSettingsForm(CachedEvent? cachedEvent)
        {
            ManualSettings settings = LocalStore.LoadManual();

            Text = "Sørby Gaming - Offline-indstillinger";
            Size = new Size(520, 360);
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

            chooseBackgroundButton.Click += (sender, e) => ChooseBackground();
            clearBackgroundButton.Click += (sender, e) =>
            {
                LocalStore.ClearManualBackground();
                UpdateBackgroundStatus();
            };

            Button saveButton = new Button
            {
                Text = "GEM",
                Location = new Point(150, 240),
                Width = 100,
                Height = 38,
                ForeColor = Color.Black,
                BackColor = Color.White
            };

            Button cancelButton = new Button
            {
                Text = "Luk",
                Location = new Point(265, 240),
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
                    DurationMinutes = manualDurationCheckBox.Checked ? (int)durationInput.Value : null
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

        private void UpdateBackgroundStatus()
        {
            backgroundStatusLabel.Text = LocalStore.HasManualBackground
                ? "Manuel baggrund: valgt (bruges når PC'en er offline)"
                : "Manuel baggrund: ingen (eventets baggrund bruges)";
        }
    }
}
