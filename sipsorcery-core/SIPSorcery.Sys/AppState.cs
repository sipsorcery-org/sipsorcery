///----------------------------------------------------------------------------
/// File Name: AppState.cs
/// 
/// Description: 
/// The AppState class holds static application configuration settings for 
/// objects requiring configuration information. AppState provides a one stop
/// shop for settings rather then have configuration functions in separate 
/// classes.
/// 
/// History:
/// 04 Nov 2004	Aaron Clauson	Created.
///
/// License:
/// Public Domain.
///----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using log4net;

namespace SIPSorcery.Sys
{
	public class AppState
	{
        public const string CRLF = "\r\n";
        public const string DEFAULT_ERRRORLOG_FILE = @"c:\\temp\\appstate.error.log";
        private const string APP_LOGGING_ID = "blueface";								// Name of log4net identifier.
        public const string ERROR_EMAILNOTIFICATION_KEY = "ErrorNotificationAddress";
        
        public static ILog logger;		        // Used to provide logging functionality for the application.
        public static ILog threadNameLogger;	// Used to record thread anesm and ids for application.
		
		public static readonly string RandomNumberURL = "https://www.random.org/cgi-bin/randnum?num=1&min=1&max=1000000000";
		
		private static StringDictionary m_appConfigSettings;	// Contains application configuration key, value pairs.
        public static readonly string ErrorNotificationEmail;   // Email address to send developer notifications to.
        public static readonly string NewLine = Environment.NewLine;

		static AppState()
		{
			try
			{
				try
				{
					// Initialise logging functionality from an XML node in the app.config file.
					Console.WriteLine("Starting logging initialisation.");
					log4net.Config.XmlConfigurator.Configure();
				}
				catch
				{
					// Unable to load the log4net configuration node (probably invalid XML in the config file).
					Console.WriteLine("Unable to load logging configuration check that the app.config file exists and is well formed.");

					try
					{
						//EventLog.WriteEntry(APP_LOGGING_ID, "Unable to load logging configuration check that the app.config file exists and is well formed.", EventLogEntryType.Error, 0);
					}
					catch(Exception evtLogExcp)
					{
						Console.WriteLine("Exception writing logging configuration error to event log. " + evtLogExcp.Message);
					}
				
					// Configure a basic console appender so if there is anyone watching they can still see log messages and to
					// ensure that any classes using the logger won't get null references.
					ConfigureConsoleLogger();
				}
				finally
				{
					try
					{
						logger = log4net.LogManager.GetLogger(APP_LOGGING_ID);
						logger.Debug("Logging initialised.");
					}
					catch(Exception excp)
					{
						StreamWriter errorLog = new StreamWriter(DEFAULT_ERRRORLOG_FILE, true);
						errorLog.WriteLine(DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + " Exception Initialising AppState Logging. " +excp.Message);
						errorLog.Close();
					}
				}
			
				// Initialise the string dictionary to hold the application settings.
				m_appConfigSettings = new StringDictionary();
                ErrorNotificationEmail = GetConfigSetting(ERROR_EMAILNOTIFICATION_KEY);			
			}
			catch(Exception excp)
			{
				StreamWriter errorLog = new StreamWriter(DEFAULT_ERRRORLOG_FILE, true);
				errorLog.WriteLine(DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + " Exception Initialising AppState. " + excp.Message);
				errorLog.Close();
			}
		}

		public static ILog GetLogger(string logName)
		{
			return log4net.LogManager.GetLogger(logName);
		}

		/// <summary>
		/// Configures the logging object to use a console logger. This would normally be used
		/// as a fallback when either the application does not have any logging configuration
		/// or there is an error in it.
		/// </summary>
		public static void ConfigureConsoleLogger()
		{	
			log4net.Appender.ConsoleAppender appender = new log4net.Appender.ConsoleAppender();

			log4net.Layout.ILayout fallbackLayout = new log4net.Layout.PatternLayout("%m%n");
			appender.Layout = fallbackLayout;
			
			log4net.Config.BasicConfigurator.Configure(appender);
		}

		/// <summary>
		/// Wrapper around the object holding the application configuration settings extracted
		/// from the App.Config file.
		/// </summary>
		/// <param name="key">The name of the configuration setting wanted.</param>
		/// <returns>The value of the configuration setting.</returns>
		public static string GetConfigSetting(string key)
		{
			if(m_appConfigSettings != null && m_appConfigSettings.ContainsKey(key))
			{
				return m_appConfigSettings[key];
			}
			else
			{
				//string newSetting = ConfigurationSettings.AppSettings[key];
                string newSetting = ConfigurationManager.AppSettings[key];
				
				if( newSetting != null && newSetting.Length > 0)
				{
					m_appConfigSettings[key] = newSetting;
					return newSetting;
				}
				else
				{
					return null;
				}
			}
		}

        public static string GetConfigNodeValue(XmlNode configNode, string nodeName)
        {
            XmlNode valueNode = configNode.SelectSingleNode(nodeName);
            if (valueNode != null) {
                if (valueNode.Attributes.GetNamedItem("value") != null)  {
                    return valueNode.Attributes.GetNamedItem("value").Value;
                }
            }

            return null;
        }
	}
}
