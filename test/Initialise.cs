//-----------------------------------------------------------------------------
// Filename: Initialise.cs
//
// Description: Assembly initialiser for SIPSorcery unit tests.
//
// Author(s):
// Aaron Clauson
//
// History:
// 14 Oct 2019	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Dublin, Ireland (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.UnitTests
{
    [TestClass]
    public class Initialize
    {
        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext context)
        {
            Console.WriteLine("AssemblyInitialise");
            SIPSorcery.Sys.Log.Logger = SimpleConsoleLogger.Instance;
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            Console.WriteLine("AssemblyCleanup");
        }
    }

    /// <summary>
    /// Getting the Microsoft console logger to work with the mstest framework was unsuccessful. Using this super
    /// simple console logger proved to be a lot easier. Can be revisited if mstest logging ever goes back to 
    /// just working OOTB.
    /// </summary>
    public class SimpleConsoleLogger : ILogger
    {
        public static SimpleConsoleLogger Instance { get; } = new SimpleConsoleLogger();

        private SimpleConsoleLogger()
        { }

        public IDisposable BeginScope<TState>(TState state)
        {
            return SimpleConsoleLoggerScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Console.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss:fff")}] [{Thread.CurrentThread.ManagedThreadId}] [{logLevel}] {formatter(state, exception)}");
        }
    }

    public class SimpleConsoleLoggerScope : IDisposable
    {
        public static SimpleConsoleLoggerScope Instance { get; } = new SimpleConsoleLoggerScope();

        private SimpleConsoleLoggerScope()
        {}

        public void Dispose()
        {}
    }
}
