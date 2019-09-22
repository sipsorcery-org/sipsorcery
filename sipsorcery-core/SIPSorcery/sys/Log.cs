///----------------------------------------------------------------------------
/// File Name: Log.cs
/// 
/// Description: 
/// The Log class holds static application configuration settings for 
/// objects requiring configuration information. Log provides a one stop
/// shop for settings rather then have configuration functions in separate 
/// classes.
/// 
/// History:
/// 04 Nov 2004	Aaron Clauson	Created.
/// 14 Sep 2019 Aaron Clauson   Added NetStandard support.
///
/// License:
/// Public Domain.
///----------------------------------------------------------------------------

using System;
using System.IO;
using System.Reflection;
using log4net;

namespace SIPSorcery.Sys
{
    public class Log
    {
        private const string LOG4NET_CONFIG_FILE = "log4net.config";
        private const string APP_LOGGING_ID = "sipsorcery"; // Name of log4net identifier.
        public const string DEFAULT_ERRRORLOG_FILE = @"c:\temp\sipsorcery.error.log";

        public static ILog logger { get; private set; }

        static Log()
        {
            try
            {
                try
                {
                    // Initialise logging functionality from an XML node in the app.config file.
                    Console.WriteLine("Starting logging initialisation.");

                    // dotnet core doesn't have app.config or web.config so the default log4net config initialisation cannot be used.
                    // The alternative is to use a dedicated log4net.config file which can contain exactly the same block of XML.

                    if (File.Exists(LOG4NET_CONFIG_FILE))
                    {
                        var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
                        log4net.Config.XmlConfigurator.Configure(logRepository, new FileInfo(LOG4NET_CONFIG_FILE));
                    }
                    else
                    {
                        ConfigureConsoleLogger();
                    }
                }
                catch
                {
                    // Unable to load the log4net configuration node (probably invalid XML in the config file).
                    Console.WriteLine($"Unable to load logging configuration from {LOG4NET_CONFIG_FILE}.");


                    // Configure a basic console appender so if there is anyone watching they can still see log messages and to
                    // ensure that any classes using the logger won't get null references.
                    ConfigureConsoleLogger();
                }
                finally
                {
                    try
                    {
                        logger = log4net.LogManager.GetLogger(Assembly.GetEntryAssembly(), APP_LOGGING_ID);
                        logger.Debug("Logging initialised.");
                    }
                    catch (Exception excp)
                    {
                        StreamWriter errorLog = new StreamWriter(DEFAULT_ERRRORLOG_FILE, true);
                        errorLog.WriteLine(DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + " Exception Initialising Log Logging. " + excp.Message);
                        errorLog.Close();
                    }
                }
            }
            catch (Exception excp)
            {
                StreamWriter errorLog = new StreamWriter(DEFAULT_ERRRORLOG_FILE, true);
                errorLog.WriteLine(DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + " Exception Initialising Log. " + excp.Message);
                errorLog.Close();
            }
        }

        public static void ConfigureConsoleLogger()
        {
            log4net.Appender.ConsoleAppender appender = new log4net.Appender.ConsoleAppender();

            log4net.Layout.ILayout fallbackLayout = new log4net.Layout.PatternLayout("%m%n");
            appender.Layout = fallbackLayout;

            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            log4net.Config.BasicConfigurator.Configure(logRepository);
        }
    }
}
