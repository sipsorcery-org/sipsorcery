// ============================================================================
// FileName: SIPAppServerState.cs
//
// Description:
// Holds application configuration information.
//
// Author(s):
// Aaron Clauson
//
// History:
// 20 Jan 2006	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using SIPSorcery.SIP;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIPAppServer
{
	/// <summary>
	/// This class maintains static application configuration settings that can be used by all classes within
	/// the AppDomain. This class is the one stop shop for retrieving or accessing application configuration settings.
	/// </summary>
    public class SIPAppServerState : IConfigurationSectionHandler
	{
		public const int DEFAULT_PROXY_PORT = SIPConstants.DEFAULT_SIP_PORT;
        public const int NATKEEPALIVE_DEFAULTSEND_INTERVAL = 15;
        public const string LOGGER_NAME = "sipsporcery-app";

        // Config node names for each of the agents.
        private const string SIPAPPSERVER_CONFIGNODE_NAME = "sipappserver";
        private const string SIPSTATELESSPROXY_CONFIGNODE_NAME = "sipstatelessproxy";
        private const string SIPREGISTRAR_CONFIGNODE_NAME = "sipregistrar";
        private const string SIPREGISTRAR_SOCKETS_CONFIGNODE_NAME = "sipregistrarsockets";
        private const string SIPREGAGENT_SOCKETS_CONFIGNODE_NAME = "sipregagentsockets";

        private const string RUNTIME_CONFIG_FILE = "RuntimeConfigFile";
        private const string MONITOR_CLIENTCONTROLSOCKET_KEY = "MonitorClientControlSocket";
        private const string MONITOR_MACHINESOCKET_KEY = "MonitorMachineSocket";
        private const string MONITOR_EVENTLISTENERPORT_KEY = "MonitorEventListenerPort";
        private const string SIPPROXY_ENABLED_KEY = "SIPProxyEnabled";
        private const string PROXY_DBLOGTYPE_KEY = "ProxyLogStorageType";
        private const string PROXY_DBLOGCONNSTR_KEY = "ProxyLogDBConnStr";
        private const string PROXY_DBTYPE_KEY = "ProxyDBStorageType";
        private const string PROXY_DBCONNSTR_KEY = "ProxyDBConnStr";
        private const string REGISTRAR_ENABLED_KEY = "RegistrarEnabled";
        private const string REGISTRAR_REALM_KEY = "RegistrarRealm";
        private const string REGISTRAR_CONTACTSPERUSER_KEY = "RegistrarContactsPerUser";
        private const string REGISTRATIONAGENT_ENABLED_KEY = "RegistrationAgentEnabled";
        private const string REGISTRATIONAGENT_PROXYSOCKET_KEY = "RegistrationAgentProxySocket";
        private const string APPSERVER_ENABLED_KEY = "AppServerEnabled";
        private const string STUN_SECONDARYSOCKET_KEY = "STUNSecondarySocket";
        private const string NATKEEPALIVE_LISTENERSOCKET_KEY = "NATKeepAliveListenerSocket";
        private const string NATKEEPALIVE_SENDINTERVAL_KEY = "NATKeepAliveSendInterval";
        private const string MANGLE_CLIENTCONTACTS_KEY = "MangleClientContact";
        private const string SUPER_USERNAME_KEY = "SuperUsername";
        private const string LOGALL_KEY = "LogAll";
        private const string TRACE_DIRECTORY_KEY = "TraceDirectory";
        private const string RUBYSCRIPTCOMMON_PATH_KEY = "RubyScriptCommonPath";
        private const string USESIPTRANSPORTMETRICS = "UseSIPTransportMetrics";
        private const string METRICSFILENAMEKEY = "MetricsFileName";
        private const string METRICSFILECOPYNAMEKEY = "MetricsCopyFileName";
        private const string METRICSLOCALGRAPHDIRKEY = "MetricsLocalGraphDir";
        private const string METRICSGRAPHPERIODKEY = "MetricsGraphPeriod";
        private const string WEBSERVICE_ENABLED_KEY = "WebServiceEnabled";
        private const string PROXY_SCRIPT_PATH = "ProxyScriptPath";
        private const string OUTBOUND_PROXY_SOCKET_KEY = "OutboundProxySocket";
        private const string SILVERLIGHT_POLICY_FILEPATH_KEY = "SilverlightPolicyFilePath";

		public static ILog logger = null;

		public static readonly string RuntimeConfigFile;			// The file path to load the runtime config from.
        public static readonly int MonitorEventListenerPort = 0;	// Loopback port this process will listen on for events from SIP Servers.
        public static readonly string MonitorClientControlSocket;   // Socket the proxy monitor will listen on for client control connections.
        public static readonly string MonitorMachineSocket;         // Socket the proxy monitor will listen on for machine connections.
        public static XmlNode SIPAppServerConfigNode;
        public static XmlNode SIPStatelessProxyConfigNode;
        public static XmlNode SIPRegistrarConfigNode;
        public static XmlNode SIPRegistrarSocketsNode;
        public static XmlNode SIPRegistrationAgentSocketsNode;
        public static readonly bool SIPProxyEnabled;
		public static readonly bool ProxyLogging = false;				// Whether or not SIP messages flowing through the proxy should be logged.
		public static readonly IPEndPoint ProxyContactEndPoint;			// Socket address the proxy will use in Record-Route headers.	
        public static readonly StorageTypes ProxyLogStorageType;        // If DB logging of proxy events is being used this is the DB storage type.
        public static readonly string ProxyLogDBConnStr;                // If DB logging of proxy events is being used this is the DB connection string.
        public static readonly bool RegistrarEnabled = false;
        public static readonly StorageTypes ProxyDBStorageType;
        public static readonly string ProxyDBConnStr;                   // The DB conn for use by the proxy to authenticate calls and lookup dial plans.
        public static readonly int RegistrarContactsPerUser = 1;        // Number of contacts the registrar will maintain per user.
        public static readonly string RegistrarSocket;
        public static readonly bool RegistrationAgentEnabled = false;
        public static readonly string RegistrationAgentSocket;
        public static readonly string RegistrationAgentProxySocket;
        public static readonly bool AppServerEnabled = false;
        public static readonly IPEndPoint STUNSecondaryEndPoint;
        public static readonly IPEndPoint NATKeepAliveListenerEndPoint;
        public static readonly int NATKeepAliveSendInterval = NATKEEPALIVE_DEFAULTSEND_INTERVAL;
        public static bool MangleClientContacts = false;
        public static bool LogAll = false;
        public static string SuperUsername;
        public static string TraceDirectory;                            // Directory path for SIP traces or other dial plan app specific logging.
        public static readonly bool WebServiceEnabled = false;
        public static string CurrentDirectory;
        public static string RubyScriptCommonPath;
        public static bool UseSIPTransportMetrics = false;
        public static string MetricsFileName;
        public static string MetricsFileCopyName;
        public static string MetricsLocalGraphDir;
        public static int MetricsGraphPeriod;
        public static string ProxyScriptPath;
        public static string OutboundProxySocket;
        public static string SilverlightPolicyFilePath;
        public static string SIPTLSCertificatePath;

		static SIPAppServerState()
		{
			try
			{
				RuntimeConfigFile = ConfigurationManager.AppSettings[RUNTIME_CONFIG_FILE];
                MonitorClientControlSocket = ConfigurationManager.AppSettings[MONITOR_CLIENTCONTROLSOCKET_KEY];
                MonitorMachineSocket = ConfigurationManager.AppSettings[MONITOR_MACHINESOCKET_KEY];
                Int32.TryParse(ConfigurationManager.AppSettings[MONITOR_EVENTLISTENERPORT_KEY], out MonitorEventListenerPort);
                Boolean.TryParse(ConfigurationManager.AppSettings[SIPPROXY_ENABLED_KEY], out SIPProxyEnabled);
                ProxyLogStorageType = (ConfigurationManager.AppSettings[PROXY_DBLOGTYPE_KEY] != null) ? StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[PROXY_DBLOGTYPE_KEY]) : StorageTypes.Unknown;
                ProxyLogDBConnStr = ConfigurationManager.AppSettings[PROXY_DBLOGCONNSTR_KEY];
                Boolean.TryParse(ConfigurationManager.AppSettings[REGISTRAR_ENABLED_KEY], out RegistrarEnabled);
                Boolean.TryParse(ConfigurationManager.AppSettings[REGISTRATIONAGENT_ENABLED_KEY], out RegistrationAgentEnabled);
                Boolean.TryParse(ConfigurationManager.AppSettings[WEBSERVICE_ENABLED_KEY], out WebServiceEnabled);
                RegistrationAgentProxySocket = ConfigurationManager.AppSettings[REGISTRATIONAGENT_PROXYSOCKET_KEY];
                ProxyDBStorageType = StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[PROXY_DBTYPE_KEY]);
                ProxyDBConnStr = ConfigurationManager.AppSettings[PROXY_DBCONNSTR_KEY];
                string regContactsPerUserStr = ConfigurationManager.AppSettings[REGISTRAR_CONTACTSPERUSER_KEY];
                string stunSecondarySocketStr = ConfigurationManager.AppSettings[STUN_SECONDARYSOCKET_KEY];
                string natKeepAliveSocketStr = ConfigurationManager.AppSettings[NATKEEPALIVE_LISTENERSOCKET_KEY];
                string natKeepAliveIntervalStr = ConfigurationManager.AppSettings[NATKEEPALIVE_SENDINTERVAL_KEY];
                Boolean.TryParse(ConfigurationManager.AppSettings[MANGLE_CLIENTCONTACTS_KEY], out MangleClientContacts);
                Boolean.TryParse(ConfigurationManager.AppSettings[LOGALL_KEY], out LogAll); 
                SuperUsername = ConfigurationManager.AppSettings[SUPER_USERNAME_KEY];
                TraceDirectory = ConfigurationManager.AppSettings[TRACE_DIRECTORY_KEY];
                RubyScriptCommonPath = ConfigurationManager.AppSettings[RUBYSCRIPTCOMMON_PATH_KEY];
                Boolean.TryParse(ConfigurationManager.AppSettings[USESIPTRANSPORTMETRICS], out UseSIPTransportMetrics);
                MetricsFileName = ConfigurationManager.AppSettings[METRICSFILENAMEKEY];
                MetricsFileCopyName = ConfigurationManager.AppSettings[METRICSFILECOPYNAMEKEY];
                MetricsLocalGraphDir = ConfigurationManager.AppSettings[METRICSLOCALGRAPHDIRKEY];
                Int32.TryParse(ConfigurationManager.AppSettings[METRICSFILENAMEKEY], out MetricsGraphPeriod);
                Boolean.TryParse(ConfigurationManager.AppSettings[APPSERVER_ENABLED_KEY], out AppServerEnabled);
                ProxyScriptPath = ConfigurationManager.AppSettings[PROXY_SCRIPT_PATH];
                OutboundProxySocket = ConfigurationManager.AppSettings[OUTBOUND_PROXY_SOCKET_KEY];
                SilverlightPolicyFilePath = ConfigurationManager.AppSettings[SILVERLIGHT_POLICY_FILEPATH_KEY];

                if (ConfigurationManager.GetSection(SIPREGAGENT_SOCKETS_CONFIGNODE_NAME) != null) {
                        SIPRegistrarSocketsNode = (XmlNode)ConfigurationManager.GetSection(SIPREGISTRAR_SOCKETS_CONFIGNODE_NAME);
                }

                if (ConfigurationManager.GetSection(SIPREGAGENT_SOCKETS_CONFIGNODE_NAME) != null) {
                    SIPRegistrationAgentSocketsNode = (XmlNode)ConfigurationManager.GetSection(SIPREGAGENT_SOCKETS_CONFIGNODE_NAME);
                }

                try
				{
					// Configure logging.
					log4net.Config.XmlConfigurator.Configure();
					logger = log4net.LogManager.GetLogger(LOGGER_NAME);
				}
				catch(Exception excp)
				{
                    Console.WriteLine("Exception AssemblyStateProxy. " + excp.Message);
				}

                //Registrar contacts per user.
                try
                {
                    if (regContactsPerUserStr != null && regContactsPerUserStr.Trim().Length > 0)
                    {
                        RegistrarContactsPerUser = Convert.ToInt32(regContactsPerUserStr);

                        if (RegistrarContactsPerUser <= 0)
                        {
                            RegistrarContactsPerUser = 1;
                        }
                    }
                }
                catch
                {
                    logger.Warn("The RegistrarContactsPerUser provided was not a valid integer.");
                }

                try
                {
                    if (stunSecondarySocketStr != null)
                    {
                        STUNSecondaryEndPoint = IPSocket.ParseSocketString(stunSecondarySocketStr);
                    }
                }
                catch 
                {
                    logger.Warn("The STUNSecondarySocket value was missing or invalid, needs to be in form 127.0.0.1:1234.");
                }

                try
                {
                    if (natKeepAliveSocketStr != null)
                    {
                        NATKeepAliveListenerEndPoint = IPSocket.ParseSocketString(natKeepAliveSocketStr);
                    }
                }
                catch (Exception natKeepaliveExcp)
                {
                    logger.Warn("The " + NATKEEPALIVE_LISTENERSOCKET_KEY + " value was missing or invalid " + natKeepAliveSocketStr + ", needs to be in form 127.0.0.1:1234. " + natKeepaliveExcp.Message);
                }

                // NATKeepAlive send interval.
                try
                {
                    if (natKeepAliveIntervalStr != null && natKeepAliveIntervalStr.Trim().Length > 0)
                    {
                        NATKeepAliveSendInterval = Convert.ToInt32(natKeepAliveIntervalStr);
                    }
                }
                catch
                {
                    logger.Warn("The NATKeepAliveSendInterval value provided was not a valid integer.");
                }

                // SIP Registrar configuration node
                if (RegistrarEnabled)
                {
                    try
                    {
                        SIPRegistrarConfigNode = (XmlNode)ConfigurationManager.GetSection(SIPREGISTRAR_CONFIGNODE_NAME);
                    }
                    catch
                    {
                        logger.Warn(SIPREGISTRAR_CONFIGNODE_NAME + " not available in App.Config.");
                    }
                }

                // SIP App Server configuration node
                if (AppServerEnabled)
                {
                    try
                    {
                        SIPAppServerConfigNode = (XmlNode)ConfigurationManager.GetSection(SIPAPPSERVER_CONFIGNODE_NAME);
                    }
                    catch
                    {
                        logger.Warn(SIPAPPSERVER_CONFIGNODE_NAME + " not available in App.Config.");
                    }
                }

                if (System.Environment.OSVersion != null && Regex.Match(System.Environment.OSVersion.ToString(), "windows", RegexOptions.IgnoreCase).Success)
                {
                    // Windows.
                    CurrentDirectory = Regex.Replace(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().CodeBase), @"^file:\\", ""); // There's undoubtedly a better way!
                }
                else
                {
                    // Linux.
                    CurrentDirectory = Regex.Match(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().CodeBase), @"^file:/*(?<path>\/.*)").Result("${path}"); // There's undoubtedly a better way!
                }
			}
			catch(Exception excp)
			{
				logger.Error("Exception SIPAppServerState. " + excp.Message);
				Console.WriteLine("Exception AssemblyStateProxy. " + excp.Message);	// In case the logging configuration is what caused the exception.
				throw excp;
			}
		}

        /// <summary>
        /// Handler for processing the App.Config file and passing retrieving the proxy config node.
        /// </summary>
        public object Create(object parent, object context, XmlNode configSection) {
            return configSection;
        }
	}
}