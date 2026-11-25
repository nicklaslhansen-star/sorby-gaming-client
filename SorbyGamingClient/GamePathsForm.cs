using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    // "Spil på denne PC" (admin-menuen). Ligger et af eventets spil et andet
    // sted på netop denne PC, kan stien rettes her. Rettelsen gemmes kun lokalt.
    internal sealed class GamePathsForm : SorbyForm
    {
        private readonly List<(EventGame Game, ModernTextBox Box, TextLabel Status)> rows = new();

        public GamePathsForm(CachedEvent cachedEvent)
        {
            Title = "Spil på denne PC";
            Subtitle = $"{cachedEvent.Name} · stien til hvert spils .exe-fil";
            Width = S(640);

            Dictionary<string, string> local = LocalStore.LoadGamePaths();
            int left = Padding24;
            int width = Width - Padding24 * 2;
            int top = HeaderHeight + S(4);
            int browseWidth = S(96);

            foreach (EventGame game in cachedEvent.Games)
            {
                TextLabel nameLabel = new TextLabel(game.Name, 10.5f, true) { Location = new Point(left, top) };

                ModernTextBox box = new ModernTextBox(fontSize: 10)
                {
                    Location = new Point(left, top + S(26)),
                    Size = new Size(width - browseWidth - S(8), S(42)),
                    Placeholder = game.Path,
                    Text = local.TryGetValue(game.Id, out string? localPath) ? localPath : game.Path
                };

                ModernButton browse = new ModernButton("Vælg...", ButtonStyle.Secondary)
                {
                    Location = new Point(box.Right + S(8), box.Top),
                    Size = new Size(browseWidth, S(42))
                };

                TextLabel status = new TextLabel("", 9, color: Theme.Muted) { Location = new Point(left, box.Bottom + S(6)) };

                browse.Click += (sender, e) =>
                {
                    using OpenFileDialog dialog = new OpenFileDialog
                    {
                        Title = $"Vælg {game.Name}",
                        Filter = "Programmer (*.exe)|*.exe",
                        CheckFileExists = true
                    };

                    try
                    {
                        string? folder = Path.GetDirectoryName(box.Text);

                        if (folder != null && Directory.Exists(folder))
                        {
                            dialog.InitialDirectory = folder;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Ugyldig sti i feltet: start i standardmappen.
                    }

                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        box.Text = dialog.FileName;
                    }
                };

                box.TextChanged += (sender, e) => UpdateStatus(box, status, game);
                UpdateStatus(box, status, game);

                Controls.AddRange(new Control[] { nameLabel, box, browse, status });
                rows.Add((game, box, status));
                top = status.Bottom + S(18);
            }

            TextLabel help = new TextLabel(
                "Rettelser gælder kun denne PC. Et tomt felt eller eventets egen sti betyder: brug stien fra eventet.",
                9, color: Theme.Subtle)
            {
                AutoSize = false,
                Location = new Point(left, top),
                Size = new Size(width, S(36))
            };

            ModernButton save = new ModernButton("Gem")
            {
                Size = new Size(S(140), S(42)),
                Location = new Point(Width - Padding24 - S(140), help.Bottom + S(14))
            };
            save.Click += (sender, e) => Save();

            ModernButton cancel = new ModernButton("Annuller", ButtonStyle.Secondary)
            {
                Size = new Size(S(140), S(42)),
                Location = new Point(save.Left - S(150), save.Top),
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(new Control[] { help, save, cancel });
            CancelButton = cancel;
            Height = save.Bottom + Padding24;
        }

        private static void UpdateStatus(ModernTextBox box, TextLabel status, EventGame game)
        {
            string path = string.IsNullOrWhiteSpace(box.Text) ? game.Path : box.Text.Trim().Trim('"');
            bool exists = File.Exists(path);
            bool isLocal = !string.Equals(path, game.Path, StringComparison.OrdinalIgnoreCase);

            status.Text = exists
                ? (isLocal ? "✓ Findes · rettet på denne PC" : "✓ Findes på denne PC")
                : "✗ Findes ikke på denne PC";
            status.ForeColor = exists ? Theme.Success : Theme.Danger;
        }

        private void Save()
        {
            Dictionary<string, string> local = LocalStore.LoadGamePaths();

            foreach ((EventGame game, ModernTextBox box, _) in rows)
            {
                string path = box.Text.Trim().Trim('"');

                if (string.IsNullOrEmpty(path) || string.Equals(path, game.Path, StringComparison.OrdinalIgnoreCase))
                {
                    local.Remove(game.Id);
                }
                else
                {
                    local[game.Id] = path;
                }
            }

            LocalStore.SaveGamePaths(local);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
