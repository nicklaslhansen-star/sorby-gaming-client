using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SorbyGamingClient
{
    // "Spil på denne PC" (admin-menuen): viser, hvilke af eventets spil PC'en
    // har fundet installeret, så admins kan se det, før eventet starter.
    internal sealed class GameStatusForm : SorbyForm
    {
        private readonly CachedEvent cachedEvent;
        private readonly List<Control> rows = new List<Control>();
        private readonly ModernButton rescan;
        private readonly ModernButton close;

        public GameStatusForm(CachedEvent cachedEvent)
        {
            this.cachedEvent = cachedEvent;
            Title = "Spil på denne PC";
            Subtitle = $"{cachedEvent.Name} · fundet på Steam, Epic, Riot, Minecraft og Roblox";
            Width = S(560);

            rescan = new ModernButton("Søg igen", ButtonStyle.Secondary) { Size = new Size(S(140), S(42)) };
            rescan.Click += (sender, e) => Fill();

            close = new ModernButton("Luk") { Size = new Size(S(140), S(42)), DialogResult = DialogResult.OK };

            Controls.AddRange(new Control[] { rescan, close });
            AcceptButton = close;
            Fill();
        }

        private void Fill()
        {
            foreach (Control row in rows)
            {
                Controls.Remove(row);
                row.Dispose();
            }

            rows.Clear();

            List<InstalledGame> installed = GameLibrary.Scan();
            int left = Padding24;
            int width = Width - Padding24 * 2;
            int top = HeaderHeight + S(4);

            foreach (EventGame wanted in cachedEvent.Games)
            {
                List<InstalledGame> found = GameLibrary.FindMatches(wanted, installed);
                string platform = GameMenuForm.PlatformName(wanted.Platform);

                TextLabel name = new TextLabel($"{wanted.Name}  ·  {platform}", 10.5f, true) { Location = new Point(left, top) };
                TextLabel status = new TextLabel(
                    found.Count > 0
                        ? "✓ Fundet: " + string.Join(", ", found.Select(game => game.Name))
                        : $"✗ Ikke fundet. Installér spillet, eller tjek at {platform} er installeret.",
                    9.5f,
                    color: found.Count > 0 ? Theme.Success : Theme.Danger)
                {
                    AutoSize = false,
                    Location = new Point(left, name.Bottom + S(4)),
                    Size = new Size(width, S(22))
                };

                rows.Add(name);
                rows.Add(status);
                top = status.Bottom + S(14);
            }

            if (cachedEvent.Games.Count == 0)
            {
                TextLabel empty = new TextLabel("Eventet har ingen spil. Spil vælges i panelet under eventet.", 10, color: Theme.Muted)
                {
                    Location = new Point(left, top)
                };
                rows.Add(empty);
                top = empty.Bottom + S(14);
            }

            TextLabel help = new TextLabel(
                "Admins logger ind på platformene (Steam, Epic osv.) på PC'en, før eventet starter.",
                9, color: Theme.Subtle)
            {
                AutoSize = false,
                Location = new Point(left, top),
                Size = new Size(width, S(22))
            };
            rows.Add(help);

            Controls.AddRange(rows.ToArray());

            close.Location = new Point(Width - Padding24 - close.Width, help.Bottom + S(14));
            rescan.Location = new Point(close.Left - rescan.Width - S(10), close.Top);
            Height = close.Bottom + Padding24;
        }
    }
}
