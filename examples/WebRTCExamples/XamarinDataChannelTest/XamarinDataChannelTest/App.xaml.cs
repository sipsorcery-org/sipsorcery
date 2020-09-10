using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
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
            var seriLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Debug()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(seriLogger);
            SIPSorcery.LogFactory.Set(factory);
        }
    }
}
