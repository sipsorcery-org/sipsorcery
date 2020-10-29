using Foundation;
using System;
using UIKit;

namespace iOSSIPGetStarted
{
    public partial class ViewController : UIViewController
    {
        public ViewController (IntPtr handle) : base (handle)
        {
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
        /// Call button click.
        /// </summary>
        /// <param name="sender"></param>
        partial void UIButton201_TouchUpInside(UIButton sender)
        {
            
        }

        /// <summary>
        /// Cancel button click.
        /// </summary>
        /// <param name="sender"></param>
        partial void UIButton202_TouchUpInside(UIButton sender)
        {
            
        }
    }
}