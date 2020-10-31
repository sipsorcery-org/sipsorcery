using Foundation;
using System;
using UIKit;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP.App;
using System.Linq;

namespace iOSXamTest
{
    public partial class ViewController : UIViewController
    {
        SIPUserAgent _userAgent = new SIPUserAgent();

        public ViewController(IntPtr handle) : base(handle)
        { }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            // Perform any additional setup after loading the view, typically from a nib.

            Action<string> logDelegate = (str) =>
            {
                InvokeOnMainThread(() => {
                    LogTextView.Text += str;
                });
            };

            SIPSorcery.LogFactory.Set(new TextViewLoggerFactory(logDelegate));
            _userAgent = new SIPUserAgent();
        }

        public override void DidReceiveMemoryWarning()
        {
            base.DidReceiveMemoryWarning();
            // Release any cached data, images, etc that aren't in use.
        }

        /// <summary>
        /// Call Buttton Click.
        /// </summary>
        /// <param name="sender"></param>
        partial void UIButton199_TouchUpInside(UIButton sender)
        {
            System.Diagnostics.Debug.WriteLine("Call Button Click");

            _userAgent.Call(DestinationText.Text, null, null, new AudioSendOnlyMediaSession());
        }

        partial void CancelButton_TouchUpInside(UIButton sender)
        {
            System.Diagnostics.Debug.WriteLine("Cancel Button Click");
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