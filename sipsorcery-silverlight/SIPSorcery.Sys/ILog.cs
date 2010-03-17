using System;
using System.IO;
using System.IO.IsolatedStorage;
using System.Windows.Controls;

namespace log4net
{
    public class ILog
    {
        public static TextBlock DebugTextBlock;

        //private const string DEBUG_LOG_FILENAME = "sipsorcery.log";

        /*private StreamWriter m_logStreamWiter;

        private void Write(string level, string message)
        {
            if (m_logStreamWiter == null)
            {
                //m_logStreamWiter = GetLogStreamWriter(DEBUG_LOG_FILENAME);
                var store = IsolatedStorageFile.GetUserStoreForApplication();
                IsolatedStorageFileStream stream = store.OpenFile(DEBUG_LOG_FILENAME, FileMode.OpenOrCreate);
                stream.Position = stream.Length;
                m_logStreamWiter = new StreamWriter(stream);
            }

            if (m_logStreamWiter != null)
            {
                m_logStreamWiter.WriteLine(DateTime.Now.ToString("ddMMMyy HH:mm:ss:fff") + " " + level.ToString() + ": " + message);
                m_logStreamWiter.Flush();
            }
        }

        private StreamWriter GetLogStreamWriter(string logFileName)
        {
            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    IsolatedStorageFileStream logFileStream = store.OpenFile(logFileName, FileMode.OpenOrCreate);
                    logFileStream.Position = logFileStream.Length;
                    return new StreamWriter(logFileStream);
                }
            }
            catch(Exception excp)
            {
                Console.WriteLine(excp.Message);
                return null;
            }
        }*/

        private void Write(string level, string message)
        {
            if (DebugTextBlock != null)
            {
                string messageToWrite = DateTime.Now.ToString("HH:mm:ss:fff") + " " + level + ": " + message + "\n";

                if (DebugTextBlock.Dispatcher.CheckAccess())
                {
                    DebugTextBlock.Inlines.Add(messageToWrite);
                }
                else
                {
                    DebugTextBlock.Dispatcher.BeginInvoke(delegate { DebugTextBlock.Inlines.Add(messageToWrite); });
                }
            }
        }

        public void Debug(string message)
        {
            Write("debug", message);
        }

        public void Error(string message)
        {
            Write("error", message);
        }

        public void Info(string message)
        {
            Write("info", message);
        }

        public void Warn(string message)
        {
            Write("warn", message);
        }
    }
}
