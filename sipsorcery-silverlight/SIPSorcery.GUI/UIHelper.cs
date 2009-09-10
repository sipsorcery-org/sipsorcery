using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using System.Windows.Browser;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SIPSorcery.SIP.App;

namespace SIPSorcery
{
    public delegate void SetTextDelegate(string message);
    public delegate void SetTextBlockDelegate(TextBlock textBlock, string message);
    public delegate void SetColouredTextBlockDelegate(TextBlock textBlock, string message, MessageLevelsEnum level);
    public delegate void AppendToActivityLogDelegate(ScrollViewer scrollViewer, TextBlock textBlock, MessageLevelsEnum level, string message);
    public delegate void SetTextBoxDelegate(TextBox textBox, string message);
    public delegate void SetPasswordBoxDelegate(PasswordBox passwordBox, string message);
    public delegate void SetButtonContentDelegate(Button button, string content);
    public delegate void SetVisibilityDelegate(UIElement element, Visibility visibility);
    public delegate void ClearDelegate();
    public delegate void AddCanvasChildDelegate(Canvas canvas, UIElement child);
    public delegate void AddGridChildDelegate(Grid grid, UIElement child);
    public delegate void AddStackPanelChildDelegate(StackPanel stackPanel, UIElement child);
    public delegate void ControlClosedDelegate();
    public delegate void SetFocusDelegate(Control control);
    public delegate void SetIsEnabledDelegate(Control control, bool isEnabled);
    public delegate void SetDataGridSourceDelegate(DataGrid dataGrid, IEnumerable list);
    public delegate void SetSIPRegistrarBindingDataGridSourceDelegate(DataGrid dataGrid, ObservableCollection<SIPRegistrarBinding> source);
    public delegate void SetProgressBarValueDelegate(ProgressBar progressBar, double progress);
    public delegate void SetFillDelegate(Shape shape, Color colour);
    public delegate void RemoveBorderChildDelegate(Border border);
    public delegate void SetBorderChildDelegate(Border border, UIElement child);
    public delegate void SetComboBoxSelectedIndexDelegate(ComboBox comboBox, int index);

    public class UIHelper
    {
        private static SolidColorBrush m_normalTextBrush;
        private static SolidColorBrush m_infoTextBrush;
        private static SolidColorBrush m_errorTextBrush;
        private static SolidColorBrush m_warnTextBrush;

        private static SilverlightHost m_silverlightHostControl = new SilverlightHost();
        private static Content m_browserContent = new Content();

        public static void SetVisibility(UIElement element, Visibility visibility)
        {
            if (element.Dispatcher.CheckAccess())
            {
                element.Visibility = visibility;
            }
            else
            {
                element.Dispatcher.BeginInvoke(new SetVisibilityDelegate(SetVisibility), element, visibility);
            }
        }

        public static void AddChild(Grid grid, UIElement child)
        {
            if (grid.Dispatcher.CheckAccess())
            {
                grid.Children.Add(child);
            }
            else
            {
                grid.Dispatcher.BeginInvoke(new AddGridChildDelegate(AddChild), grid, child);
            }
        }

        public static void AddChild(Canvas canvas, UIElement child)
        {
            if (canvas.Dispatcher.CheckAccess())
            {
                canvas.Children.Add(child);
            }
            else
            {
                canvas.Dispatcher.BeginInvoke(new AddCanvasChildDelegate(AddChild), canvas, child);
            }
        }

        public static void AddChild(StackPanel stackPanel, UIElement child)
        {
            if (stackPanel.Dispatcher.CheckAccess())
            {
                stackPanel.Children.Add(child);
            }
            else
            {
                stackPanel.Dispatcher.BeginInvoke(new AddStackPanelChildDelegate(AddChild), stackPanel, child);
            }
        }

        public static void SetText(TextBlock textBlock, string text)
        {
            if (textBlock.Dispatcher.CheckAccess())
            {
                textBlock.Text = text;
            }
            else
            {
                textBlock.Dispatcher.BeginInvoke(new SetTextBlockDelegate(SetText), textBlock, text);
            }
        }

        public static void AppendText(TextBlock textBlock, string text)
        {
            if (textBlock.Dispatcher.CheckAccess())
            {
                textBlock.Text += text;
            }
            else
            {
                textBlock.Dispatcher.BeginInvoke(new SetTextBlockDelegate(AppendText), textBlock, text);
            }
        }

        public static void AppendToActivityLog(ScrollViewer scrollViewer, TextBlock textBlock, MessageLevelsEnum level, string text)
        {
            if (textBlock.Dispatcher.CheckAccess())
            {
                Run activityRun = new Run();
                activityRun.Text = text;
                activityRun.Foreground = GetBrushForMessageLevel(level);
                textBlock.Inlines.Add(activityRun);
                textBlock.Inlines.Add(new LineBreak());

                scrollViewer.ScrollToVerticalOffset(scrollViewer.ScrollableHeight + 20);
            }
            else
            {
                textBlock.Dispatcher.BeginInvoke(new AppendToActivityLogDelegate(AppendToActivityLog), scrollViewer, textBlock, level, text);
            }
        }

        public static void SetText(TextBox textBox, string text)
        {
            if (textBox.Dispatcher.CheckAccess())
            {
                textBox.Text = text;
            }
            else
            {
                textBox.Dispatcher.BeginInvoke(new SetTextBoxDelegate(SetText), textBox, text);
            }
        }

        public static void SetColouredText(TextBlock textBlock, string text, MessageLevelsEnum level)
        {
            if (textBlock.Dispatcher.CheckAccess())
            {
                textBlock.Foreground = GetBrushForMessageLevel(level);
                textBlock.Text = text;
            }
            else
            {
                textBlock.Dispatcher.BeginInvoke(new SetColouredTextBlockDelegate(SetColouredText), textBlock, text, level);
            }
        }

        public static void SetText(PasswordBox passwordBox, string text)
        {
            if (passwordBox.Dispatcher.CheckAccess())
            {
                passwordBox.Password = text;
            }
            else
            {
                passwordBox.Dispatcher.BeginInvoke(new SetPasswordBoxDelegate(SetText), passwordBox, text);
            }
        }

        public static void SetFocus(Control control)
        {
            if (control.Dispatcher.CheckAccess())
            {
                control.Focus();
            }
            else
            {
                control.Dispatcher.BeginInvoke(new SetFocusDelegate(SetFocus), control);
            }
        }
        
        public static void SetIsEnabled(Control control, bool isEnabled)
        {
            if (control.Dispatcher.CheckAccess())
            {
                control.IsEnabled = isEnabled;
            }
            else
            {
                control.Dispatcher.BeginInvoke(new SetIsEnabledDelegate(SetIsEnabled), control, isEnabled);
            }
        }

        public static void SetDataGridSource(DataGrid dataGrid, IEnumerable list)
        {
            if (dataGrid.Dispatcher.CheckAccess())
            {
                dataGrid.ItemsSource = list;
            }
            else
            {
                dataGrid.Dispatcher.BeginInvoke(new SetDataGridSourceDelegate(SetDataGridSource), dataGrid, list);
            }
        }

        public static void SetDataGridSource(DataGrid dataGrid, ObservableCollection<SIPRegistrarBinding> source)
        {
            if (dataGrid.Dispatcher.CheckAccess())
            {
                dataGrid.ItemsSource = source;
            }
            else
            {
                dataGrid.Dispatcher.BeginInvoke(new SetSIPRegistrarBindingDataGridSourceDelegate(SetDataGridSource), dataGrid, source);
            }
        }

        public static void SetProgressBarValue(ProgressBar progressBar, double progress)
        {
            if (progressBar.Dispatcher.CheckAccess())
            {
                progressBar.Value = progress;
            }
            else
            {
                progressBar.Dispatcher.BeginInvoke(new SetProgressBarValueDelegate(SetProgressBarValue), progressBar, progress);
            }
        }

        public static void SetFill(Shape shape, Color colour)
        {
            if (shape.Dispatcher.CheckAccess())
            {
                shape.Fill = new SolidColorBrush(colour);
            }
            else
            {
                shape.Dispatcher.BeginInvoke(new SetFillDelegate(SetFill), shape, colour);
            }
        }

        public static void AdjustPluginHeight(double heightAdjustment)
        {
            double newHeight = m_silverlightHostControl.Content.ActualHeight + heightAdjustment;
            HtmlPage.Plugin.SetProperty("height", newHeight);
        }

        public static void SetPluginDimensions(double width, double height)
        {
            if (m_browserContent.ActualWidth < width)
            {
                HtmlPage.Plugin.SetProperty("width", width);
            }

            HtmlPage.Plugin.SetProperty("height", height);
        }

        public static void RemoveBorderChild(Border border)
        {
            if (border.Dispatcher.CheckAccess())
            {
                border.Child = null;
            }
            else
            {
                border.Dispatcher.BeginInvoke(new RemoveBorderChildDelegate(RemoveBorderChild), border);
            }
        }

        public static void SetBorderChild(Border border, UIElement child)
        {
            if (border.Dispatcher.CheckAccess())
            {
                border.Child = child;
            }
            else
            {
                border.Dispatcher.BeginInvoke(new SetBorderChildDelegate(SetBorderChild), border, child);
            }
        }

        public static void SetComboBoxSelectedId(ComboBox comboBox, int index) {
            if (comboBox.Dispatcher.CheckAccess()) {
                comboBox.SelectedIndex = index;
            }
            else {
                comboBox.Dispatcher.BeginInvoke(new SetComboBoxSelectedIndexDelegate(SetComboBoxSelectedId), comboBox, index);
            }
        }

        private static SolidColorBrush GetBrushForMessageLevel(MessageLevelsEnum level) {
           SolidColorBrush brush = null;

           if (m_normalTextBrush == null) {
               m_normalTextBrush = AssemblyState.NormalTextBrush;
               m_infoTextBrush = AssemblyState.InfoTextBrush; 
               m_errorTextBrush = AssemblyState.ErrorTextBrush;
               m_warnTextBrush = AssemblyState.WarnTextBrush;
           }

            if (level == MessageLevelsEnum.Error) {
                brush = m_errorTextBrush;
            }
            else if (level == MessageLevelsEnum.Warn) {
                brush = m_warnTextBrush;
            }
            else if (level == MessageLevelsEnum.Monitor) {
                brush = m_infoTextBrush;
            }
            else {
                brush = m_normalTextBrush;
            }

            return brush;
        }
    }
}
