using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace SorbyGamingClient
{
    // Henter nye versioner fra GitHub Releases. Opdateringen hentes i
    // baggrunden, men installeres først, når PC'en står låst uden session.
    internal sealed class UpdateService
    {
        private const string RepoUrl = "https://github.com/nicklaslhansen-star/sorby-gaming-client";

        // Argument til genstart efter opdatering: PC'en starter igen som
        // samme PC-nummer uden opsætningsdialog.
        public const string ResumeArgument = "--resume";

        private readonly UpdateManager? manager;
        private UpdateInfo? pendingUpdate;
        private bool checking;

        public UpdateService()
        {
            try
            {
                UpdateManager updateManager = new UpdateManager(new GithubSource(RepoUrl, null, false));

                // Kører programmet fra Visual Studio eller bin-mappen, er det
                // ikke installeret via Velopack og kan ikke opdateres.
                if (updateManager.IsInstalled)
                {
                    manager = updateManager;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Opdatering er ikke tilgængelig: {ex.Message}");
            }
        }

        public bool HasPendingUpdate => pendingUpdate != null;

        public string CurrentVersion => manager?.CurrentVersion?.ToString() ?? "udvikling";

        public async Task CheckAndDownloadAsync()
        {
            if (manager == null || checking || pendingUpdate != null)
            {
                return;
            }

            checking = true;

            try
            {
                UpdateInfo? update = await manager.CheckForUpdatesAsync();

                if (update == null)
                {
                    return;
                }

                Console.WriteLine($"Ny version fundet: {update.TargetFullRelease.Version}");
                await manager.DownloadUpdatesAsync(update);
                pendingUpdate = update;
            }
            catch (Exception ex)
            {
                // Typisk intet internet eller GitHubs grænse for opslag. Prøv igen senere.
                Console.WriteLine($"Kunne ikke søge efter opdatering: {ex.Message}");
            }
            finally
            {
                checking = false;
            }
        }

        // Lukker programmet, installerer og starter den nye version.
        public void ApplyAndRestart()
        {
            if (manager == null || pendingUpdate == null)
            {
                return;
            }

            manager.ApplyUpdatesAndRestart(pendingUpdate.TargetFullRelease, new[] { ResumeArgument });
        }
    }
}
