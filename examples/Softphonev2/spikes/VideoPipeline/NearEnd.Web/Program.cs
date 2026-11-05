using VideoSpike;

namespace NearEnd.Web;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var cfg = RunConfig.Parse(args);
        if (cfg.Path == PipelinePath.Wpf)
        {
            Console.Error.WriteLine("The WPF path is NearEnd.Wpf.");
            return;
        }
        Clock.UseHighResolutionTimer();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(cfg));
    }
}
