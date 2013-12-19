using System;
using log4net;

namespace SIPSorcery.Sys
{
    public class AppState
    {
        public static readonly string RandomNumberURL = "http://www.random.org/cgi-bin/randnum?num=1&min=1&max=1000000000";

        public static ILog logger = new ILog();

        public static readonly string NewLine = Environment.NewLine;

        public static ILog GetLogger(string name)
        {
            return logger;
        }
    }
}
