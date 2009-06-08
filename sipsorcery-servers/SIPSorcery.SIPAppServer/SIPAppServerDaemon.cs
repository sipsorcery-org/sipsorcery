// ============================================================================
// FileName: MainConsole.cs
//
// Description:
// Main console display for the demonstration SIP Proxy.
//
// Author(s):
// Aaron Clauson
//
// History:
// 13 Aug 2006	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;
using System.ServiceModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.CRM;
using SIPSorcery.Net;
using SIPSorcery.Servers;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.SIPMonitor;
using SIPSorcery.SIPProxy;
using SIPSorcery.SIPRegistrar;
using SIPSorcery.SIPRegistrationAgent;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIPAppServer {
    public class SIPAppServerDaemon {
        public const int MINIMUM_RELOAD_DELAY = 15;                     // Minimum period between reloads of the registration agent.   
        private const int DIALPLAN_NOTUSED_PURGEPERIOD = 300;           // The number of seconds after last use that a dialplan will be purged for.

        private static ILog logger = SIPAppServerState.logger;

        private XmlNode m_sipAppServerSocketsNode = SIPAppServerState.SIPAppServerSocketsNode;

        private static bool m_sipProxyEnabled = true; //SIPAppServerState.SIPProxyEnabled;
        private static bool m_sipMonitorEnabled = true; //SIPAppServerState.SIPProxyEnabled;
        private static bool m_sipRegistrarEnabled = true; //SIPAppServerState.RegistrarEnabled;
        private static bool m_sipRegAgentEnabled = true; //SIPAppServerState.RegistrationAgentEnabled;
        private static bool m_sipAppServerEnabled = true; //SIPAppServerState.AppServerEnabled;
        private static bool m_webServiceEnabled = true; //SIPAppServerState.WebServiceEnabled;
        
        private static int m_monitorEventLoopbackPort = SIPAppServerState.MonitorLoopbackPort;
        private string m_traceDirectory = SIPAppServerState.TraceDirectory;
        private string m_currentDirectory = SIPAppServerState.CurrentDirectory;
        private string m_rubyScriptCommonPath = SIPAppServerState.RubyScriptCommonPath;
        private SIPEndPoint m_outboundProxy = SIPAppServerState.OutboundProxy;

        private SIPSorceryPersistor m_sipSorceryPersistor;
        private SIPMonitorEventWriter m_monitorEventWriter;
        private SIPAppServerCore m_appServerCore;
        private SIPProxyDaemon m_sipProxyDaemon;
        private SIPMonitorDaemon m_sipMonitorDaemon;
        private SIPRegAgentDaemon m_sipRegAgentDaemon;
        private SIPRegistrarDaemon m_sipRegistrarDaemon;

        private SIPTransport m_sipTransport;
        private DialPlanEngine m_dialPlanEngine;
        private ServiceHost m_accessPolicyHost;
        private ServiceHost m_sipProvisioningHost;
        private CustomerSessionManager m_customerSessionManager;

        private StorageTypes m_storageType;
        private string m_connectionString;

        public SIPAppServerDaemon(StorageTypes storageType, string connectionString) 
        {
            m_storageType = storageType;
            m_connectionString = connectionString;
        }

        public void Start() {
            try {
                logger.Debug("pid=" + Process.GetCurrentProcess().Id + ".");
                logger.Debug("os=" + System.Environment.OSVersion + ".");
                logger.Debug("current directory=" + m_currentDirectory + ".");

                m_sipSorceryPersistor = new SIPSorceryPersistor(m_storageType, m_connectionString);
                m_customerSessionManager = new CustomerSessionManager(m_storageType, m_connectionString);

                if (m_sipProxyEnabled) {
                    m_sipProxyDaemon = new SIPProxyDaemon();
                    m_sipProxyDaemon.Start();
                }

                if (m_sipMonitorEnabled) {
                    m_sipMonitorDaemon = new SIPMonitorDaemon(m_customerSessionManager);
                    m_sipMonitorDaemon.Start();
                }

                if (m_sipRegistrarEnabled) {
                    m_sipRegistrarDaemon = new SIPRegistrarDaemon(
                        m_sipSorceryPersistor.SIPDomainManager.GetDomain,
                        m_sipSorceryPersistor.SIPAccountsPersistor.Get,
                        m_sipSorceryPersistor.SIPRegistrarBindingPersistor);
                    m_sipRegistrarDaemon.Start();
                }

                if (m_sipRegAgentEnabled) {
                    m_sipRegAgentDaemon = new SIPRegAgentDaemon(
                        m_sipSorceryPersistor.SIPProvidersPersistor.Get,
                        m_sipSorceryPersistor.SIPProvidersPersistor.Update,
                        m_sipSorceryPersistor.SIPProviderBindingsPersistor);

                    // The Registration Agent wants to know about any changes to SIP Provider entries in order to update any SIP 
                    // Provider bindings it is maintaining or needs to add or remove.
                    m_sipSorceryPersistor.SIPProvidersPersistor.Added += m_sipRegAgentDaemon.SIPProviderAdded;
                    m_sipSorceryPersistor.SIPProvidersPersistor.Updated += m_sipRegAgentDaemon.SIPProviderUpdated;
                    m_sipSorceryPersistor.SIPProvidersPersistor.Deleted += m_sipRegAgentDaemon.SIPProviderDeleted;

                    m_sipRegAgentDaemon.Start();
                }

                #region Initialise the SIP Application Server and its logging mechanisms including CDRs.

                if (m_sipAppServerEnabled) {
                    logger.Debug("Initiating SIP Application Server Agent.");

                    if (m_sipAppServerSocketsNode == null || m_sipAppServerSocketsNode.ChildNodes.Count == 0)
                    {
                        //throw new ApplicationException("Empty IP Address for proxy cannot start.");
                        logger.Warn("No IP addresses specified for App Server it will be disabled.");
                    }
                    else {
                        // Send events from this process to the monitoring socket.
                        if (m_monitorEventLoopbackPort != 0) {
                            m_monitorEventWriter = new SIPMonitorEventWriter(m_monitorEventLoopbackPort);
                        }
                        
                        if (m_sipSorceryPersistor != null && m_sipSorceryPersistor.SIPCDRPersistor != null) {
                            SIPCDR.NewCDR += WriteCDR;
                            SIPCDR.HungupCDR += WriteCDR;
                            SIPCDR.CancelledCDR += WriteCDR;
                        }

                        #region Initialise the SIPTransport layer.

                        m_sipTransport = new SIPTransport(SIPDNSManager.Resolve, new SIPTransactionEngine(), true, false);
                        m_sipTransport.AddSIPChannel(SIPTransportConfig.ParseSIPChannelsNode(m_sipAppServerSocketsNode));

                        m_sipTransport.SIPRequestInTraceEvent += LogSIPRequestIn;
                        m_sipTransport.SIPRequestOutTraceEvent += LogSIPRequestOut;
                        m_sipTransport.SIPResponseInTraceEvent += LogSIPResponseIn;
                        m_sipTransport.SIPResponseOutTraceEvent += LogSIPResponseOut;
                        m_sipTransport.SIPBadRequestInTraceEvent += LogSIPBadRequestIn;
                        m_sipTransport.SIPBadResponseInTraceEvent += LogSIPBadResponseIn;
                        m_sipTransport.UnrecognisedMessageReceived += UnrecognisedMessageReceived;

                        #endregion

                        SIPRequestAuthoriser sipRequestAuthoriser = new SIPRequestAuthoriser(
                            FireSIPMonitorEvent,
                            m_sipSorceryPersistor.SIPDomainManager.GetDomain,
                            m_sipSorceryPersistor.GetSIPAccount);

                        m_dialPlanEngine = new DialPlanEngine(
                            m_sipTransport,
                            m_sipSorceryPersistor.SIPDomainManager.GetDomain,
                            FireSIPMonitorEvent,
                            DialPlanExecutionTransition,
                            m_sipSorceryPersistor.SIPAccountsPersistor.Get,
                            m_sipSorceryPersistor.SIPRegistrarBindingPersistor.Get,
                            m_outboundProxy,
                            m_rubyScriptCommonPath);

                        SIPCallManager callManager = new SIPCallManager(
                             m_sipTransport,
                             m_outboundProxy,
                             FireSIPMonitorEvent,
                             m_sipSorceryPersistor.SIPDialoguePersistor,
                             m_sipSorceryPersistor.SIPCDRPersistor,
                             m_dialPlanEngine,
                             m_sipSorceryPersistor.LoadDialPlan,
                             m_sipSorceryPersistor.GetSIPAccount,
                             m_sipSorceryPersistor.SIPRegistrarBindingPersistor.Get,
                             m_sipSorceryPersistor.SIPProvidersPersistor.Get,
                             m_sipSorceryPersistor.SIPDomainManager.GetDomain,
                             m_traceDirectory);

                        m_appServerCore = new SIPAppServerCore(
                            m_sipTransport,
                            null,
                            null,
                            m_sipSorceryPersistor.SIPDomainManager.GetDomain,
                            FireSIPMonitorEvent,
                            callManager,
                            sipRequestAuthoriser,
                            m_outboundProxy);
                    }
                }
                else {
                    logger.Warn("The App Server was disabled.");
                }

                #endregion

                try {
                    if (m_webServiceEnabled) {
                        if (m_sipSorceryPersistor == null) {
                            logger.Warn("Web services could not be started as Persistor object was null.");
                        }
                        else {
                            SIPProvisioningWebService provisioningWebService = new SIPProvisioningWebService() {
                                SIPAccountPersistor = m_sipSorceryPersistor.SIPAccountsPersistor,
                                DialPlanPersistor = m_sipSorceryPersistor.SIPDialPlanPersistor,
                                SIPProviderPersistor = m_sipSorceryPersistor.SIPProvidersPersistor,
                                SIPProviderBindingsPersistor = m_sipSorceryPersistor.SIPProviderBindingsPersistor,
                                SIPDomainManager = m_sipSorceryPersistor.SIPDomainManager,
                                SIPRegistrarBindingsPersistor = m_sipSorceryPersistor.SIPRegistrarBindingPersistor,
                                SIPDialoguePersistor = m_sipSorceryPersistor.SIPDialoguePersistor,
                                SIPCDRPersistor = m_sipSorceryPersistor.SIPCDRPersistor,
                                AuthenticateToken_External = m_customerSessionManager.Authenticate,
                                AuthenticateWebService_External = m_customerSessionManager.Authenticate,
                                ExpireToken_External = m_customerSessionManager.ExpireToken,
                                CRMCustomerPersistor = m_customerSessionManager.CustomerPersistor,
                                LogDelegate_External = FireSIPMonitorEvent
                            };

                            m_sipProvisioningHost = new ServiceHost(provisioningWebService);
                            m_sipProvisioningHost.Open();

                            m_accessPolicyHost = new ServiceHost(typeof(CrossDomainService));
                            m_accessPolicyHost.Open();

                            logger.Debug("Web services successfully started on " + m_sipProvisioningHost.BaseAddresses[0].AbsoluteUri + ".");
                        }
                    }
                    else {
                        logger.Debug("The web services were disabled.");
                    }
                }
                catch (Exception excp) {
                    logger.Error("Exception starting Hosted Services. " + excp.Message);
                }

                // Initialise random number to save delay on first SIP request.
                Crypto.GetRandomString();
            }
            catch (Exception excp) {
                logger.Error("Exception SIPAppServerDaemon Start. " + excp.Message);
                throw excp;
            }
        }

        public void WriteCDR(SIPCDR cdr) {
            SIPCDRAsset cdrAsset = new SIPCDRAsset(cdr);
            if (m_sipSorceryPersistor.SIPCDRPersistor.Count(c => c.Id == cdrAsset.Id) == 1) {
                m_sipSorceryPersistor.SIPCDRPersistor.Update(cdrAsset);
            }
            else {
                m_sipSorceryPersistor.SIPCDRPersistor.Add(cdrAsset);
            }
        }

        private void DialPlanExecutionTransition(Guid dialPlanId, DialPlanExecutionStateEnum executionState) {
            try {
                SIPDialPlan dialPlan = m_sipSorceryPersistor.SIPDialPlanPersistor.Get(dialPlanId);

                if (dialPlan != null) {
                    if (executionState == DialPlanExecutionStateEnum.Starting) {
                        dialPlan.ExecutionCount = dialPlan.ExecutionCount + 1;
                    }
                    else {
                        dialPlan.ExecutionCount = dialPlan.ExecutionCount - 1;
                    }

                    m_sipSorceryPersistor.SIPDialPlanPersistor.Update(dialPlan);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception DialPlanExecutionTransition. " + excp.Message);
            }
        }

        public void Stop() {
            try {
                logger.Debug("SIP Application Server stopping...");

                DNSManager.Stop();
                DialPlanEngine.StopScriptMonitoring = true;

                if (m_accessPolicyHost != null) {
                    m_accessPolicyHost.Close();
                }

                if (m_sipProvisioningHost != null) {
                    m_sipProvisioningHost.Close();
                }

                if (m_monitorEventWriter != null) {
                    m_monitorEventWriter.Close();
                }

                if (m_sipProxyDaemon != null) {
                    m_sipProxyDaemon.Stop();
                }

                if (m_sipMonitorDaemon != null) {
                    m_sipMonitorDaemon.Stop();
                }

                if (m_sipRegistrarDaemon != null) {
                    m_sipRegistrarDaemon.Stop();
                }

                if (m_sipRegAgentDaemon != null) {
                    m_sipRegAgentDaemon.Stop();
                }

                // Shutdown the SIPTransport layer.
                m_sipTransport.Shutdown();

                logger.Debug("SIP Application Server stopped.");
            }
            catch (Exception excp) {
                logger.Error("Exception SIPAppServerDaemon Stop." + excp.Message);
            }
        }

        #region Logging functions.

        private void FireSIPMonitorEvent(SIPMonitorEvent sipMonitorEvent) {
            try {
                if (sipMonitorEvent != null) {
                    if (m_monitorEventWriter != null && sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.SIPTransaction) {
                        m_monitorEventWriter.Send(sipMonitorEvent);
                    }

                    if (sipMonitorEvent.ServerType == SIPMonitorServerTypesEnum.RegisterAgent ||
                        (sipMonitorEvent.GetType() != typeof(SIPMonitorMachineEvent) &&
                        sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.FullSIPTrace &&
                        sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.SIPTransaction &&
                        sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.Timing &&
                        sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.ContactRegisterInProgress &&
                        sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.Monitor &&
                        sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.UnrecognisedMessage &&
                        sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.NATKeepAliveRelay)) {
                        logger.Debug("as: " + sipMonitorEvent.Message);
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception FireSIPMonitorEvent. " + excp.Message);
            }
        }

        private void LogSIPRequestIn(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPRequest sipRequest) {
            string message = "App Svr Received: " + localSIPEndPoint.ToString() + "<-" + endPoint.ToString() + "\r\n" + sipRequest.ToString();
            FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, message, sipRequest.Header.From.FromURI.User, localSIPEndPoint, endPoint));
        }

        private void LogSIPRequestOut(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPRequest sipRequest) {
            string message = "App Svr Sent: " + localSIPEndPoint.ToString() + "->" + endPoint.ToString() + "\r\n" + sipRequest.ToString();
            FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, message, sipRequest.Header.From.FromURI.User, localSIPEndPoint, endPoint));
        }

        private void LogSIPResponseIn(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPResponse sipResponse) {
            string message = "App Svr Received: " + localSIPEndPoint.ToString() + "<-" + endPoint.ToString() + "\r\n" + sipResponse.ToString();
            FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, message, sipResponse.Header.From.FromURI.User, localSIPEndPoint, endPoint));
        }

        private void LogSIPResponseOut(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPResponse sipResponse) {
            string message = "App Svr Sent: " + localSIPEndPoint.ToString() + "->" + endPoint.ToString() + "\r\n" + sipResponse.ToString();
            FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, message, sipResponse.Header.From.FromURI.User, localSIPEndPoint, endPoint));
        }

       /* private void LogSIPRequestIn(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPRequest sipRequest) {
            string message = "App Svr Request Received: " + localSIPEndPoint + "<-" + endPoint.ToString() + "\r\n" + sipRequest.ToString();
            LogSIPMessage(message);
        }

        private void LogSIPRequestOut(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPRequest sipRequest) {
            string message = "App Svr Request Sent: " + localSIPEndPoint + "->" + endPoint.ToString() + "\r\n" + sipRequest.ToString();
            LogSIPMessage(message);
        }

        private void LogSIPResponseIn(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPResponse sipResponse) {
            string message = "App Svr Response Received: " + localSIPEndPoint + "<-" + endPoint.ToString() + "\r\n" + sipResponse.ToString();
            LogSIPMessage(message);
        }

        private void LogSIPResponseOut(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPResponse sipResponse) {
            string message = "App Svr Response Sent: " + localSIPEndPoint + "->" + endPoint.ToString() + "\r\n" + sipResponse.ToString();
            LogSIPMessage(message);
        }*/

        private void LogSIPBadRequestIn(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, string sipMessage, SIPValidationFieldsEnum errorField) {
            string message = "App Svr Bad Request Received: " + localSIPEndPoint + "<-" + endPoint.ToString() + ", " + errorField;
            string fullMessage = "App Svr Bad Request Received: " + localSIPEndPoint + "<-" + endPoint.ToString() + ", " + errorField + "\r\n" + sipMessage;
            LogSIPBadMessage(message, fullMessage);
        }

        private void LogSIPBadResponseIn(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, string sipMessage, SIPValidationFieldsEnum errorField) {
            string message = "App Svr Bad Response Received: " + localSIPEndPoint + "<-" + endPoint.ToString() + ", " + errorField;
            string fullMessage = "App Svr Bad Response Received: " + localSIPEndPoint + "<-" + endPoint.ToString() + ", " + errorField + "\r\n" + sipMessage;
            LogSIPBadMessage(message, fullMessage);
        }

        //private void LogSIPMessage(string message) {
        //    FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, message, null));
        //}

        private void LogSIPBadMessage(string message, string fullTraceMessage) {
            FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.BadSIPMessage, message, null));
            FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, fullTraceMessage, null));
        }

        private void UnrecognisedMessageReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint fromEndPoint, byte[] buffer) {
            int bufferLength = (buffer != null) ? buffer.Length : 0;
            string msg = null;

            if (bufferLength > 0) {
                if (buffer.Length > 128) {
                    msg = " =>" + Encoding.ASCII.GetString(buffer, 0, 128) + "...<=";
                }
                else {
                    msg = " =>" + Encoding.ASCII.GetString(buffer) + "<=";
                }
            }

            SIPMonitorEvent unrecgMsgEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.UnrecognisedMessage, "Unrecognised packet received from " + fromEndPoint.ToString() + " on " + localSIPEndPoint.ToString() + ", bytes=" + bufferLength + " " + msg + ".", null);
            FireSIPMonitorEvent(unrecgMsgEvent);
        }

        #endregion
    }
}
