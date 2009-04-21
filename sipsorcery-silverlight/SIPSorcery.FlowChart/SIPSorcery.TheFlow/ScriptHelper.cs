using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SIPSorcery.TheFlow
{
    public delegate void LogMessageDelegate(string message);

    public class ScriptHelper
    {
        private event LogMessageDelegate m_logMessage;

        public ScriptHelper(LogMessageDelegate logMessage)
        {
            m_logMessage = logMessage;
        }

        public void Log(string message)
        {
            m_logMessage(message);
        }
    }
}
