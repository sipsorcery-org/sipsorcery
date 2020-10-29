using Foundation;
using System;
using System.Threading.Tasks;
using UIKit;
using SIPSorcery.Media;
using SIPSorcery.SIP.App;

namespace iOSSIPGetStarted
{
    public partial class ViewController : UIViewController
    {
        SIPUserAgent _userAgent;

        public ViewController (IntPtr handle) : base (handle)
        {
            _userAgent = new SIPUserAgent();
            CancelButton.Enabled = false;
        }

        public override void ViewDidLoad ()
        {
            base.ViewDidLoad ();
            // Perform any additional setup after loading the view, typically from a nib.
        }

        public override void DidReceiveMemoryWarning ()
        {
            base.DidReceiveMemoryWarning ();
            // Release any cached data, images, etc that aren't in use.
        }

        /// <summary>
        /// Cancel button click.
        /// </summary>
        /// <param name="sender"></param>
        partial void UIButton201_TouchUpInside(UIButton sender)
        {
            CallButton.Enabled = true;
            CancelButton.Enabled = false;
        }

        /// <summary>
        /// Call button click.
        /// </summary>
        /// <param name="sender"></param>
        partial void UIButton202_TouchUpInside(UIButton sender)
        {
            CancelButton.Enabled = true;
            CallButton.Enabled = false;

            var callResult = _userAgent.Call(DestinationTextBox.Text, null, null, new AudioSendOnlyMediaSession());
        }
    }
}