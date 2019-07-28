using log4net;

namespace SIPSorcery.Sys
{
    public interface ILoggerProvider
    {
        ILog GetLogger(string logName);
    }
}
