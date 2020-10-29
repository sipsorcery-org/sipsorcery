using Foundation;
using System;
using UIKit;
using SIPSorcery.Media;
using SIPSorcery.SIP.App;

namespace iOSXamTest
{
    public partial class ViewController : UIViewController
    {
        SIPUserAgent _userAgent = new SIPUserAgent();

        public ViewController(IntPtr handle) : base(handle)
        {
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            // Perform any additional setup after loading the view, typically from a nib.

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
}