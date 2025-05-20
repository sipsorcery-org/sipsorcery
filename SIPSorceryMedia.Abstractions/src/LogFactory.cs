using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SIPSorcery
{
    public class LogFactory
    {
        private ILoggerFactory _factory = NullLoggerFactory.Instance;

        public event Action? OnFactorySet;

        private static LogFactory? _appLog;
        public static LogFactory Instance => (_appLog ??= new());

        private LogFactory()
        { }

        public static ILogger CreateLogger(string categoryName) =>
            Instance._factory.CreateLogger(categoryName);

        public static ILogger<T> CreateLogger<T>() =>
            Instance._factory.CreateLogger<T>();

        public static void Set(ILoggerFactory? factory)
        {
            Instance._factory = factory ?? NullLoggerFactory.Instance;
            Instance.OnFactorySet?.Invoke();
        }
    }
}
