using System;
using System.Collections;
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
        Selected = 5,
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
        public static SolidColorBrush NormalTextBrush = (SolidColorBrush)Application.Current.Resources["NormalTextBrush"];
        public static SolidColorBrush WarnTextBrush = (SolidColorBrush)Application.Current.Resources["WarningTextBrush"];
        public static SolidColorBrush InfoTextBrush = (SolidColorBrush)Application.Current.Resources["InfoTextBrush"];
        public static SolidColorBrush ErrorTextBrush = (SolidColorBrush)Application.Current.Resources["ErrorTextBrush"];
        public static SolidColorBrush SelectedTextBrush = (SolidColorBrush)Application.Current.Resources["SelectedTextBrush"];
    }
}
