using Velopack;

namespace SorbyGamingClient;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // Skal køre først: håndterer installation og opdatering via Velopack.
        VelopackApp.Build().Run();

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        // Lukkes opsætningen uden at starte, er hovedvinduet allerede lukket.
        Form1 mainForm = new Form1(args);

        if (!mainForm.IsDisposed)
        {
            Application.Run(mainForm);
        }
    }
}
