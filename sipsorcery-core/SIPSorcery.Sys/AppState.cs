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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using log4net;

namespace SIPSorcery.Sys
{
    public class AppState
    {
        public const string CRLF = "\r\n";
        public const string DEFAULT_ERRRORLOG_FILE = @"c:\temp\appstate.error.log";
        public const string ENCRYPTED_SETTING_PREFIX = "$#";
        private const string ENCRYPTED_SETTINGS_CERTIFICATE_NAME = "EncryptedSettingsCertificateName";
        private const string APP_LOGGING_ID = "sipsorcery";								// Name of log4net identifier.

        public static ILog logger;		                        // Used to provide logging functionality for the application.

        private static StringDictionary m_appConfigSettings;	// Contains application configuration key, value pairs.
        private static X509Certificate2 m_encryptedSettingsCertificate;
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
                    catch (Exception evtLogExcp)
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
                    catch (Exception excp)
                    {
                        StreamWriter errorLog = new StreamWriter(DEFAULT_ERRRORLOG_FILE, true);
                        errorLog.WriteLine(DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + " Exception Initialising AppState Logging. " + excp.Message);
                        errorLog.Close();
                    }
                }

                // Initialise the string dictionary to hold the application settings.
                m_appConfigSettings = new StringDictionary();
            }
            catch (Exception excp)
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
            try
            {
                if (m_appConfigSettings != null && m_appConfigSettings.ContainsKey(key))
                {
                    return m_appConfigSettings[key];
                }
                else
                {
                    string setting = ConfigurationManager.AppSettings[key];

                    if (!setting.IsNullOrBlank())
                    {
                        if (setting.StartsWith(ENCRYPTED_SETTING_PREFIX))
                        {
                            logger.Debug("Decrypting appSetting " + key + ".");

                            X509Certificate2 encryptedSettingsCertificate = GetEncryptedSettingsCertificate();
                            if (encryptedSettingsCertificate != null)
                            {
                                if (encryptedSettingsCertificate.HasPrivateKey)
                                {
                                    logger.Debug("Private key on encrypted settings certificate is available.");

                                    setting = setting.Substring(2);
                                    byte[] encryptedBytes = Convert.FromBase64String(setting);
                                    RSACryptoServiceProvider rsa = (RSACryptoServiceProvider)encryptedSettingsCertificate.PrivateKey;
                                    byte[] plainTextBytes = rsa.Decrypt(encryptedBytes, false);
                                    setting = Encoding.ASCII.GetString(plainTextBytes);

                                    logger.Debug("Successfully decrypted appSetting " + key + ".");
                                }
                                else
                                {
                                    throw new ApplicationException("Could not access private key on encrypted settings certificate.");
                                }
                            }
                            else
                            {
                                throw new ApplicationException("Could not load the encrypted settings certificate to decrypt setting " + key + ".");
                            }
                        }

                        m_appConfigSettings[key] = setting;
                        return setting;
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AppState.GetSetting. " + excp.Message);
                throw;
            }
        }

        public static string GetConfigNodeValue(XmlNode configNode, string nodeName)
        {
            XmlNode valueNode = configNode.SelectSingleNode(nodeName);
            if (valueNode != null)
            {
                if (valueNode.Attributes.GetNamedItem("value") != null)
                {
                    return valueNode.Attributes.GetNamedItem("value").Value;
                }
            }

            return null;
        }

        public static object GetSection(string sectionName)
        {
            return ConfigurationManager.GetSection(sectionName);
        }

        public static X509Certificate2 LoadCertificate(StoreLocation storeLocation, string certificateSubject)
        {
            X509Store store = new X509Store(storeLocation);
            logger.Debug("Certificate store " + store.Location + " opened");
            store.Open(OpenFlags.OpenExistingOnly);
            X509Certificate2Collection collection = store.Certificates.Find(X509FindType.FindBySubjectName, certificateSubject, true);
            if (collection != null && collection.Count > 0)
            {
                X509Certificate2 serverCertificate = collection[0];
                bool verifyCert = serverCertificate.Verify();
                logger.Debug("X509 certificate loaded from current user store, subject=" + serverCertificate.Subject + ", valid=" + verifyCert + ".");
                return serverCertificate;
            }
            else
            {
                logger.Warn("X509 certificate with subject name=" + certificateSubject + ", not found in " + store.Location + " store.");
                return null;
            }
        }

        private static X509Certificate2 GetEncryptedSettingsCertificate()
        {
            try
            {
                if (m_encryptedSettingsCertificate == null)
                {
                    string encryptedSettingsCertName = ConfigurationManager.AppSettings[ENCRYPTED_SETTINGS_CERTIFICATE_NAME];
                    if (!encryptedSettingsCertName.IsNullOrBlank())
                    {
                        X509Certificate2 encryptedSettingsCertificate = LoadCertificate(StoreLocation.LocalMachine, encryptedSettingsCertName);
                        if (encryptedSettingsCertificate != null)
                        {
                            logger.Debug("Encrypted settings certificate successfully loaded for " + encryptedSettingsCertName + ".");
                            m_encryptedSettingsCertificate = encryptedSettingsCertificate;
                        }
                        else
                        {
                            logger.Error("Could not load the encrypted settings certificate for " + encryptedSettingsCertName + ".");
                        }
                    }
                    else
                    {
                        logger.Error("Could not load the encrypted settings certificate, no " + ENCRYPTED_SETTINGS_CERTIFICATE_NAME + " setting found.");
                    }
                }

                return m_encryptedSettingsCertificate;
            }
            catch (Exception excp)
            {
                logger.Error("Exception AppState.GetEncryptedSettingsCertificate. " + excp.Message);
                return null;
            }
        }
    }
}
