using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using SIPSorcery.CRM;
using SIPSorcery.Net;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIPAppServer
{
    class MainConsole
    {
        private static readonly string m_storageTypeKey = SIPSorceryConfiguration.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = SIPSorceryConfiguration.PERSISTENCE_STORAGECONNSTR_KEY;

        private static ILog logger = AppState.logger;

        private static ManualResetEvent m_proxyUp = new ManualResetEvent(false);

        private static StorageTypes m_serverStorageType;
        private static string m_serverStorageConnStr;

        [STAThread]
        static void Main(string[] args)
        {
            bool isConsole = false;

            try
            {
                // Get DateTime.ToString() to use a format ot ToString("o") instead of ToString("G").
                CultureInfo culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
                culture.DateTimeFormat.ShortDatePattern = "yyyy-MM-dd";
                culture.DateTimeFormat.LongTimePattern = "THH:mm:ss.fffffffzzz";
                Thread.CurrentThread.CurrentCulture = culture;

                m_serverStorageType = (AppState.GetConfigSetting(m_storageTypeKey) != null) ? StorageTypesConverter.GetStorageType(AppState.GetConfigSetting(m_storageTypeKey)) : StorageTypes.Unknown;
                m_serverStorageConnStr = AppState.GetConfigSetting(m_connStrKey);
                bool monitorCalls = true;

                if (m_serverStorageType == StorageTypes.Unknown || m_serverStorageConnStr.IsNullOrBlank())
                {
                    throw new ApplicationException("The SIP Application Service cannot start with no persistence settings specified.");
                }

                if (args != null && args.Length > 0)
                {
                    isConsole = true;
                    Console.WriteLine("SIP App Server starting");
                    logger.Debug("SIP App Server Console starting...");

                    string sipSocket = null;
                    string callManagerSvcAddress = null;

                    foreach (string arg in args) {
                        if (arg.StartsWith("-sip:")) {
                            sipSocket = arg.Substring(5);
                        }
                        else if (arg.StartsWith("-cms:")) {
                            callManagerSvcAddress = arg.Substring(5);
                        }
                        else if (arg.StartsWith("-hangupcalls:")) {
                            monitorCalls = Convert.ToBoolean(arg.Substring(13));
                        }
                    }

                    SIPAllInOneDaemon daemon = null;

                    if (sipSocket.IsNullOrBlank() || callManagerSvcAddress.IsNullOrBlank()) {
                        daemon = new SIPAllInOneDaemon(m_serverStorageType, m_serverStorageConnStr);
                    }
                    else {
                        daemon = new SIPAllInOneDaemon(m_serverStorageType, m_serverStorageConnStr, SIPEndPoint.ParseSIPEndPoint(sipSocket), callManagerSvcAddress, monitorCalls);
                    }

                    Thread daemonThread = new Thread(new ThreadStart(daemon.Start));
                    daemonThread.Start();

                    m_proxyUp.WaitOne();
                }
                else
                {
                    logger.Debug("SIP App Server Windows Service Starting...");
                    System.ServiceProcess.ServiceBase[] ServicesToRun;
                    SIPAllInOneDaemon daemon = new SIPAllInOneDaemon(m_serverStorageType, m_serverStorageConnStr);
                    ServicesToRun = new System.ServiceProcess.ServiceBase[] { new Service(daemon) };
                    System.ServiceProcess.ServiceBase.Run(ServicesToRun);
                }
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception SIP App Server Main. " + excp.Message);

                if (isConsole) {
                    Console.WriteLine("press any key to exit...");
                    Console.ReadLine();
                }
            }
        }
    }
}
