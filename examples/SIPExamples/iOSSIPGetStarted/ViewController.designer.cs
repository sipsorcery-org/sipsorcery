// WARNING
//
// This file has been generated automatically by Visual Studio from the outlets and
// actions declared in your storyboard file.
// Manual changes to this file will not be maintained.
//
using Foundation;
using System;
using System.CodeDom.Compiler;
using UIKit;

namespace iOSXamTest
{
    [Register ("ViewController")]
    partial class ViewController
    {
        [Outlet]
        [GeneratedCode ("iOS Designer", "1.0")]
        UIKit.UIButton CallButton { get; set; }

        [Outlet]
        [GeneratedCode ("iOS Designer", "1.0")]
        UIKit.UIButton CancelButton { get; set; }

        [Outlet]
        [GeneratedCode ("iOS Designer", "1.0")]
        UIKit.UITextField DestinationText { get; set; }

        [Outlet]
        [GeneratedCode ("iOS Designer", "1.0")]
        UIKit.UITextView LogTextView { get; set; }

        [Action ("CancelButton_TouchUpInside:")]
        [GeneratedCode ("iOS Designer", "1.0")]
        partial void CancelButton_TouchUpInside (UIKit.UIButton sender);

        [Action ("UIButton199_TouchUpInside:")]
        [GeneratedCode ("iOS Designer", "1.0")]
        partial void UIButton199_TouchUpInside (UIKit.UIButton sender);

        void ReleaseDesignerOutlets ()
        {
            if (CallButton != null) {
                CallButton.Dispose ();
                CallButton = null;
            }

            if (CancelButton != null) {
                CancelButton.Dispose ();
                CancelButton = null;
            }

            if (DestinationText != null) {
                DestinationText.Dispose ();
                DestinationText = null;
            }

            if (LogTextView != null) {
                LogTextView.Dispose ();
                LogTextView = null;
            }
        }
    }
}