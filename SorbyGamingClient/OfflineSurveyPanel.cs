using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    // Spørgeskemaet vises direkte på PC-skærmen, når der ikke er internet
    // og QR-koden derfor ikke kan bruges.
    internal sealed class OfflineSurveyPanel : Panel
    {
        private readonly TableLayoutPanel layout;
        private readonly Dictionary<string, Control> answerControls = new Dictionary<string, Control>();
        private List<SurveyQuestion> questions = new List<SurveyQuestion>();
        private Label? messageLabel;

        public event Action<Dictionary<string, string?>>? StartRequested;

        public OfflineSurveyPanel()
        {
            BackColor = Color.FromArgb(20, 20, 20);
            // Barnet placeres ved (30, 30); Padding giver luft i højre og bund.
            Padding = new Padding(0, 0, 30, 30);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                Location = new Point(30, 30),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent
            };

            Controls.Add(layout);
        }

        public void Build(string? eventName, List<SurveyQuestion> surveyQuestions, int durationMinutes)
        {
            questions = surveyQuestions;
            answerControls.Clear();

            layout.SuspendLayout();
            layout.Controls.Clear();
            layout.RowStyles.Clear();

            AddRow(new Label
            {
                Text = string.IsNullOrWhiteSpace(eventName) ? "Gaming-session" : eventName,
                ForeColor = Color.White,
                Font = new Font("Arial", 22, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 20)
            });

            foreach (SurveyQuestion question in questions)
            {
                AddRow(new Label
                {
                    Text = question.Label,
                    ForeColor = Color.White,
                    Font = new Font("Arial", 13),
                    AutoSize = true,
                    Margin = new Padding(0, 8, 0, 4)
                });

                Control input;

                if (question.Type == "text")
                {
                    input = new TextBox { Font = new Font("Arial", 13), Width = 420, MaxLength = 200 };
                }
                else
                {
                    ComboBox comboBox = new ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Font = new Font("Arial", 13),
                        Width = 420
                    };

                    comboBox.Items.Add(new SurveyOption { Value = "", Label = "Vælg svar" });
                    comboBox.Items.AddRange(question.Options.ToArray());
                    comboBox.SelectedIndex = 0;
                    input = comboBox;
                }

                answerControls[question.Id] = input;
                AddRow(input);
            }

            Button startButton = new Button
            {
                Text = $"START {durationMinutes} MINUTTER",
                Font = new Font("Arial", 14, FontStyle.Bold),
                Width = 420,
                Height = 50,
                BackColor = Color.FromArgb(0, 170, 90),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 24, 0, 0)
            };

            startButton.Click += (sender, e) => Submit();
            AddRow(startButton);

            messageLabel = new Label
            {
                ForeColor = Color.FromArgb(255, 200, 80),
                Font = new Font("Arial", 12),
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0)
            };

            AddRow(messageLabel);
            layout.ResumeLayout();
        }

        private void AddRow(Control control)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control);
        }

        private void Submit()
        {
            Dictionary<string, string?> answers = new Dictionary<string, string?>();

            foreach (SurveyQuestion question in questions)
            {
                string value = answerControls[question.Id] switch
                {
                    ComboBox comboBox => (comboBox.SelectedItem as SurveyOption)?.Value ?? "",
                    TextBox textBox => textBox.Text.Trim(),
                    _ => ""
                };

                if (question.Required && value == "")
                {
                    if (messageLabel != null)
                    {
                        messageLabel.Text = "Udfyld venligst alle spørgsmål.";
                    }

                    return;
                }

                answers[question.Id] = value == "" ? null : value;
            }

            StartRequested?.Invoke(answers);
        }
    }
}
