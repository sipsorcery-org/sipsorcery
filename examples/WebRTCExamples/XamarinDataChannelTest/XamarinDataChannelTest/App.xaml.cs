using Microsoft.Extensions.Logging;
using Serilog;
using Xamarin.Forms;

namespace XamarinDataChannelTest
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            InitialiseLogging();
            MainPage = new AppShell();
        }

        protected override void OnStart()
        {
        }

        protected override void OnSleep()
        {
        }

        protected override void OnResume()
        {
        }

        public void InitialiseLogging()
        {
            var loggerFactory = new LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Debug()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;

            SIPSorcery.Sys.Log.Logger.LogDebug("Logging initialised.");
        }
    }
}
