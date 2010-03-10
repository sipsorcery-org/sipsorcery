using System;
using System.IO;
using System.IO.IsolatedStorage;

namespace log4net
{
    public class ILog
    {
        private const string DEBUG_LOG_FILENAME = "sipsorcery.log";

        private StreamWriter m_logStreamWiter;

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
        }

        public void Debug(string message)
        {
            try
            {
                //Write("debug", message);
            }
            catch { }
        }

        public void Error(string message)
        {
            try
            {
                //Write("error", message);
            }
            catch { }
        }

        public void Info(string message)
        {
            try
            {
                //Write("info", message);
            }
            catch { }
        }

        public void Warn(string message)
        {
            try
            {
                //Write("warn", message);
            }
            catch { }
        }
    }
}
