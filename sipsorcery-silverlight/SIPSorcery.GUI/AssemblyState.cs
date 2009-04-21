using System;
using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SIPSorcery
{
    public enum MessageLevelsEnum
    {
        None = 0,
        Info = 1,
        Error = 2,
        Warn = 3,
        Monitor = 4,
    }

    public enum ServiceConnectionStatesEnum
    {
        None = 0,
        Error = 1,
        Initialising = 2,
        Ok = 3,
    }

    public enum DetailsControlModesEnum
    {
        None = 0,
        Add = 1,
        Edit = 2,
    }

    public class AssemblyState
    {
        public static Color InfoTextColour = Color.FromArgb(0xff, 0xA0, 0xF9, 0x27);
        public static Color WarnTextColour = Colors.Yellow;
        public static Color ErrorTextColour = Colors.Red;
        public static Color MonitorTextColour = Colors.Purple;

        //public static SolidColorBrush TextBoxFocusedBackground = new SolidColorBrush(Color.FromArgb(0xff, 0x17, 0x1e, 0x17));
        public static SolidColorBrush TextBoxFocusedBackground = new SolidColorBrush(Color.FromArgb(0xff, 0xff, 0xff, 0xff));
    }
}
