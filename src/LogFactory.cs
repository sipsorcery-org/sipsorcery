using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SIPSorcery
{
    public class LogFactory
    {
        private ILoggerFactory _factory = NullLoggerFactory.Instance;

        public event Action OnFactorySet;

        private static LogFactory _appLog;
        public static LogFactory Instance
        {
            get
            {
                if (_appLog == null)
                {
                    _appLog = new LogFactory();
                }

                return _appLog;
            }
        }

        private LogFactory()
        { }

        public static ILogger CreateLogger(string categoryName) =>
            Instance._factory.CreateLogger(categoryName);

        public static ILogger CreateLogger<T>() =>
            Instance._factory.CreateLogger<T>();

        public static void Set(ILoggerFactory factory)
        {
            Instance._factory = factory;
            Instance.OnFactorySet?.Invoke();
        }
    }
}
