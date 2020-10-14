using System;
using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V7.App;
using Android.Views;
using Android.Widget;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP.App;

namespace AndroidSIPGetStarted
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            var callButton = FindViewById<Button>(Resource.Id.callButton);
            var cancelButton = FindViewById<Button>(Resource.Id.cancelButton);
            var destination = FindViewById<TextView>(Resource.Id.callDestination);
            var statusScroll = FindViewById<ScrollView>(Resource.Id.statusScroll);
            var statusText = FindViewById<TextView>(Resource.Id.statusTextView);

            Action<string> logDelegate = (str) =>
            {
                this.RunOnUiThread(() =>
                {
                    statusText.Append(str);
                    statusScroll.FullScroll(FocusSearchDirection.Down);
                });
            };

            SIPSorcery.LogFactory.Set(new TextViewLoggerFactory(logDelegate));
            var userAgent = new SIPUserAgent();

            callButton.Click += async (sender, e) =>
            {
                callButton.Enabled = false;
                cancelButton.Enabled = true;

                logDelegate($"Calling {destination.Text}...\n");

                var callResult = await userAgent.Call(destination.Text, null, null, new AudioSendOnlyMediaSession());

                logDelegate($"Call result {callResult}...\n");
            };

            cancelButton.Click += (sender, e) =>
            {
                cancelButton.Enabled = false;
                callButton.Enabled = true;

                logDelegate("Cancelled.\n");
            };
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }

    public class TextViewLoggerFactory : IDisposable, ILoggerFactory
    {
        private Action<string> _logDelegate;

        public TextViewLoggerFactory(Action<string> logDelegate)
        {
            _logDelegate = logDelegate;
        }

        public virtual Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
            => new TextViewLogger(_logDelegate);

        public virtual void AddProvider(ILoggerProvider provider)
        { }

        public void Dispose()
        { }
    }

    public class TextViewLogger : IDisposable, Microsoft.Extensions.Logging.ILogger
    {
        private Action<string> _logDelegate;

        public TextViewLogger(Action<string> logDelegate)
        {
            _logDelegate = logDelegate;
        }

        public IDisposable BeginScope<TState>(TState state) => this;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Dispose()
        { }

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            System.Diagnostics.Debug.WriteLine($"[{eventId}] {formatter(state, exception)}");
            _logDelegate($"[{eventId}] {formatter(state, exception)}\n");
        }
    }
}
