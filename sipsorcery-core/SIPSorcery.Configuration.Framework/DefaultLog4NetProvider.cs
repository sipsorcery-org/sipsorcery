using log4net;

namespace SIPSorcery.Sys
{
    public class DefaultLog4NetProvider : ILoggerProvider
    {
        public DefaultLog4NetProvider()
        {
            log4net.Config.XmlConfigurator.Configure();
        }

        public ILog GetLogger(string logName)
        {
            return LogManager.GetLogger(logName);
        }
    }
}
