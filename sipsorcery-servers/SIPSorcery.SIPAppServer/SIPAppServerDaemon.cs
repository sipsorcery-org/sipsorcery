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
using System.Security;
using System.ServiceProcess;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Web;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.AppServer.DialPlan;
using SIPSorcery.CRM;
using SIPSorcery.Net;
using SIPSorcery.Persistence;
using SIPSorcery.Servers;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;
using log4net;

namespace SIPSorcery.SIPAppServer
{
    public class SIPAppServerDaemon
    {
        private static ILog logger = SIPAppServerState.logger;
        private static ILog dialPlanLogger = AppState.GetLogger("dialplan");

        private XmlNode m_sipAppServerSocketsNode = SIPAppServerState.SIPAppServerSocketsNode;

        private static int m_monitorEventLoopbackPort = SIPAppServerState.MonitorLoopbackPort;
        private string m_traceDirectory = SIPAppServerState.TraceDirectory;
        private string m_currentDirectory = AppState.CurrentDirectory;
        private string m_rubyScriptCommonPath = SIPAppServerState.RubyScriptCommonPath;
        private SIPEndPoint m_outboundProxy = SIPAppServerState.OutboundProxy;
        private string m_dialplanImpersonationUsername = SIPAppServerState.DialPlanEngineImpersonationUsername;
        private string m_dialplanImpersonationPassword = SIPAppServerState.DialPlanEngineImpersonationPassword;
        private int m_dailyCallLimit = SIPAppServerState.DailyCallLimit;
        private int m_maxDialPlanExecutionLimit = SIPAppServerState.DialPlanMaxExecutionLimit;

        private SIPSorceryPersistor m_sipSorceryPersistor;
        private SIPSorcery.Entities.CDRDataLayer m_cdrDataLayer;
        private SIPMonitorEventWriter m_monitorEventWriter;
        private SIPAppServerCore m_appServerCore;
        private SIPCallManager m_callManager;
        private SIPDialogueManager m_sipDialogueManager;

        private SIPTransport m_sipTransport;
        private DialPlanEngine m_dialPlanEngine;
        private ServiceHost m_callManagerSvcHost;
        private CustomerSessionManager m_customerSessionManager;

        private StorageTypes m_storageType;
        private string m_connectionString;
        private SIPEndPoint m_appServerEndPoint;
        private string m_callManagerServiceAddress;
        private bool m_monitorCalls;                // If true this app server instance will monitor the sip dialogues table for expired calls to hangup.

        public SIPAppServerDaemon(StorageTypes storageType, string connectionString)
        {
            m_storageType = storageType;
            m_connectionString = connectionString;
            m_monitorCalls = true;
        }

        public SIPAppServerDaemon(
            StorageTypes storageType,
            string connectionString,
            SIPEndPoint appServerEndPoint,
            string callManagerServiceAddress,
            bool monitorCalls)
        {
            m_storageType = storageType;
            m_connectionString = connectionString;
            m_appServerEndPoint = appServerEndPoint;
            m_callManagerServiceAddress = callManagerServiceAddress;
            m_monitorCalls = monitorCalls;
        }

        public void Start()
        {
            try
            {
                logger.Debug("pid=" + Process.GetCurrentProcess().Id + ".");
                logger.Debug("os=" + System.Environment.OSVersion + ".");
                logger.Debug("current directory=" + m_currentDirectory + ".");
                logger.Debug("base directory=" + AppDomain.CurrentDomain.BaseDirectory + ".");

                SIPDNSManager.SIPMonitorLogEvent = FireSIPMonitorEvent;
                m_sipSorceryPersistor = new SIPSorceryPersistor(m_storageType, m_connectionString);
                m_customerSessionManager = new CustomerSessionManager(m_storageType, m_connectionString);
                m_cdrDataLayer = new Entities.CDRDataLayer();

                #region Initialise the SIP Application Server and its logging mechanisms including CDRs.

                logger.Debug("Initiating SIP Application Server Agent.");

                // Send events from this process to the monitoring socket.
                if (m_monitorEventLoopbackPort != 0)
                {
                    m_monitorEventWriter = new SIPMonitorEventWriter(m_monitorEventLoopbackPort);
                }

                if (m_cdrDataLayer != null)
                {
                    SIPCDR.CDRCreated += m_cdrDataLayer.Add;
                    SIPCDR.CDRAnswered += m_cdrDataLayer.Update;
                    SIPCDR.CDRHungup += m_cdrDataLayer.Update;
                    SIPCDR.CDRUpdated += m_cdrDataLayer.Update;
                }

                #region Initialise the SIPTransport layers.

                m_sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine(), true);
                if (m_appServerEndPoint != null)
                {
                    if (m_appServerEndPoint.Protocol == SIPProtocolsEnum.udp)
                    {
                        logger.Debug("Using single SIP transport socket for App Server " + m_appServerEndPoint.ToString() + ".");
                        m_sipTransport.AddSIPChannel(new SIPUDPChannel(m_appServerEndPoint.GetIPEndPoint()));
                    }
                    else if (m_appServerEndPoint.Protocol == SIPProtocolsEnum.tcp)
                    {
                        logger.Debug("Using single SIP transport socket for App Server " + m_appServerEndPoint.ToString() + ".");
                        m_sipTransport.AddSIPChannel(new SIPTCPChannel(m_appServerEndPoint.GetIPEndPoint()));
                    }
                    else
                    {
                        throw new ApplicationException("The SIP End Point of " + m_appServerEndPoint + " cannot be used with the App Server transport layer.");
                    }
                }
                else if (m_sipAppServerSocketsNode != null)
                {
                    m_sipTransport.AddSIPChannel(SIPTransportConfig.ParseSIPChannelsNode(m_sipAppServerSocketsNode));
                }
                else
                {
                    throw new ApplicationException("The SIP App Server could not be started, no SIP sockets have been configured.");
                }

                m_sipTransport.SIPRequestInTraceEvent += LogSIPRequestIn;
                m_sipTransport.SIPRequestOutTraceEvent += LogSIPRequestOut;
                m_sipTransport.SIPResponseInTraceEvent += LogSIPResponseIn;
                m_sipTransport.SIPResponseOutTraceEvent += LogSIPResponseOut;
                m_sipTransport.SIPBadRequestInTraceEvent += LogSIPBadRequestIn;
                m_sipTransport.SIPBadResponseInTraceEvent += LogSIPBadResponseIn;

                #endregion

                m_dialPlanEngine = new DialPlanEngine(
                    m_sipTransport,
                    m_sipSorceryPersistor.SIPDomainManager.GetDomain,
                    FireSIPMonitorEvent,
                    m_sipSorceryPersistor,
                    m_outboundProxy,
                    m_rubyScriptCommonPath,
                    m_dialplanImpersonationUsername,
                    m_dialplanImpersonationPassword,
                    m_maxDialPlanExecutionLimit);

                m_sipDialogueManager = new SIPDialogueManager(
                    m_sipTransport,
                     m_outboundProxy,
                     FireSIPMonitorEvent,
                     m_sipSorceryPersistor.SIPDialoguePersistor,
                     m_sipSorceryPersistor.SIPCDRPersistor,
                     SIPRequestAuthenticator.AuthenticateSIPRequest,
                     m_sipSorceryPersistor.SIPAccountsPersistor.Get,
                     m_sipSorceryPersistor.SIPDomainManager.GetDomain);

                m_callManager = new SIPCallManager(
                     m_sipTransport,
                     m_outboundProxy,
                     FireSIPMonitorEvent,
                     m_sipDialogueManager,
                     m_sipSorceryPersistor.SIPDialoguePersistor,
                     m_sipSorceryPersistor.SIPCDRPersistor,
                     m_dialPlanEngine,
                     m_sipSorceryPersistor.SIPDialPlanPersistor.Get,
                     m_sipSorceryPersistor.SIPAccountsPersistor.Get,
                     m_sipSorceryPersistor.SIPRegistrarBindingPersistor.Get,
                     m_sipSorceryPersistor.SIPProvidersPersistor.Get,
                     m_sipSorceryPersistor.SIPDomainManager.GetDomain,
                     m_customerSessionManager.CustomerPersistor,
                     m_sipSorceryPersistor.SIPDialPlanPersistor,
                     m_traceDirectory,
                     m_monitorCalls,
                     m_dailyCallLimit);
                m_callManager.Start();

                m_appServerCore = new SIPAppServerCore(
                    m_sipTransport,
                    m_sipSorceryPersistor.SIPDomainManager.GetDomain,
                    m_sipSorceryPersistor.SIPAccountsPersistor.Get,
                    FireSIPMonitorEvent,
                    m_callManager,
                    m_sipDialogueManager,
                    SIPRequestAuthenticator.AuthenticateSIPRequest,
                    m_outboundProxy);

                #endregion

                #region Initialise WCF services.

                try
                {
                    if (WCFUtility.DoesWCFServiceExist(typeof(CallManagerServices).FullName.ToString()))
                    {
                        CallManagerServiceInstanceProvider callManagerSvcInstanceProvider = new CallManagerServiceInstanceProvider(m_callManager, m_sipDialogueManager);

                        Uri callManagerBaseAddress = null;
                        if (m_callManagerServiceAddress != null)
                        {
                            logger.Debug("Adding service address to Call Manager Service " + m_callManagerServiceAddress + ".");
                            callManagerBaseAddress = new Uri(m_callManagerServiceAddress);
                        }

                        if (callManagerBaseAddress != null)
                        {
                            m_callManagerSvcHost = new ServiceHost(typeof(CallManagerServices), callManagerBaseAddress);
                        }
                        else
                        {
                            m_callManagerSvcHost = new ServiceHost(typeof(CallManagerServices));
                        }

                        m_callManagerSvcHost.Description.Behaviors.Add(callManagerSvcInstanceProvider);

                        m_callManagerSvcHost.Open();

                        logger.Debug("CallManager hosted service successfully started on " + m_callManagerSvcHost.BaseAddresses[0].AbsoluteUri + ".");
                    }
                }
                catch (Exception excp)
                {
                    logger.Warn("Exception starting CallManager hosted service. " + excp.Message);
                }

                #endregion
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAppServerDaemon Start. " + excp.Message);
                throw excp;
            }
        }

        public void Stop()
        {
            try
            {
                logger.Debug("SIP Application Server stopping...");

                DNSManager.Stop();
                m_dialPlanEngine.StopScriptMonitoring = true;

                if (m_callManagerSvcHost != null)
                {
                    m_callManagerSvcHost.Close();
                }

                if (m_callManager != null)
                {
                    m_callManager.Stop();
                }

                if (m_monitorEventWriter != null)
                {
                    m_monitorEventWriter.Close();
                }

                // Shutdown the SIPTransport layer.
                m_sipTransport.Shutdown();

                m_sipSorceryPersistor.StopCDRWrites = true;

                logger.Debug("SIP Application Server stopped.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAppServerDaemon Stop." + excp.Message);
            }
        }

        #region Logging functions.

        private void FireSIPMonitorEvent(SIPMonitorEvent sipMonitorEvent)
        {
            try
            {
                if (sipMonitorEvent != null && m_monitorEventWriter != null)
                {
                    if (sipMonitorEvent is SIPMonitorConsoleEvent)
                    {
                        SIPMonitorConsoleEvent consoleEvent = sipMonitorEvent as SIPMonitorConsoleEvent;

                        if (consoleEvent.ServerType == SIPMonitorServerTypesEnum.RegisterAgent ||
                            (consoleEvent.EventType != SIPMonitorEventTypesEnum.FullSIPTrace &&
                            consoleEvent.EventType != SIPMonitorEventTypesEnum.SIPTransaction &&
                            consoleEvent.EventType != SIPMonitorEventTypesEnum.Timing &&
                            consoleEvent.EventType != SIPMonitorEventTypesEnum.UnrecognisedMessage &&
                            consoleEvent.EventType != SIPMonitorEventTypesEnum.ContactRegisterInProgress &&
                            consoleEvent.EventType != SIPMonitorEventTypesEnum.Monitor))
                        {
                            string eventUsername = (sipMonitorEvent.Username.IsNullOrBlank()) ? null : " " + sipMonitorEvent.Username;
                            dialPlanLogger.Debug("as (" + DateTime.Now.ToString("mm:ss:fff") + eventUsername + "): " + sipMonitorEvent.Message);
                        }

                        if (consoleEvent.EventType != SIPMonitorEventTypesEnum.SIPTransaction)
                        {
                            m_monitorEventWriter.Send(sipMonitorEvent);
                        }
                    }
                    else
                    {
                        m_monitorEventWriter.Send(sipMonitorEvent);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireSIPMonitorEvent. " + excp.Message);
            }
        }

        private void LogSIPRequestIn(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPRequest sipRequest)
        {
            string message = "App Svr Received: " + localSIPEndPoint.ToString() + "<-" + endPoint.ToString() + "\r\n" + sipRequest.ToString();
            //logger.Debug("as: request in " + sipRequest.Method + " " + localSIPEndPoint.ToString() + "<-" + endPoint.ToString() + ", callid=" + sipRequest.Header.CallId + ".");
            string fromUser = (sipRequest.Header.From != null && sipRequest.Header.From.FromURI != null) ? sipRequest.Header.From.FromURI.User : "Error on From header";
            FireSIPMonitorEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, message, fromUser, localSIPEndPoint, endPoint));
        }

        private void LogSIPRequestOut(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPRequest sipRequest)
        {
            string message = "App Svr Sent: " + localSIPEndPoint.ToString() + "->" + endPoint.ToString() + "\r\n" + sipRequest.ToString();
            //logger.Debug("as: request out " + sipRequest.Method + " " + localSIPEndPoint.ToString() + "->" + endPoint.ToString() + ", callid=" + sipRequest.Header.CallId + ".");
            string fromUser = (sipRequest.Header.From != null && sipRequest.Header.From.FromURI != null) ? sipRequest.Header.From.FromURI.User : "Error on From header";
            FireSIPMonitorEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, message, fromUser, localSIPEndPoint, endPoint));
        }

        private void LogSIPResponseIn(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPResponse sipResponse)
        {
            string message = "App Svr Received: " + localSIPEndPoint.ToString() + "<-" + endPoint.ToString() + "\r\n" + sipResponse.ToString();
            //logger.Debug("as: response in " + sipResponse.Header.CSeqMethod + " " + localSIPEndPoint.ToString() + "<-" + endPoint.ToString() + ", callid=" + sipResponse.Header.CallId + ".");
            string fromUser = (sipResponse.Header.From != null && sipResponse.Header.From.FromURI != null) ? sipResponse.Header.From.FromURI.User : "Error on From header";
            FireSIPMonitorEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, message, fromUser, localSIPEndPoint, endPoint));
        }

        private void LogSIPResponseOut(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPResponse sipResponse)
        {
            string message = "App Svr Sent: " + localSIPEndPoint.ToString() + "->" + endPoint.ToString() + "\r\n" + sipResponse.ToString();
            //logger.Debug("as: response out " + sipResponse.Header.CSeqMethod + " " + localSIPEndPoint.ToString() + "->" + endPoint.ToString() + ", callid=" + sipResponse.Header.CallId + ".");
            string fromUser = (sipResponse.Header.From != null && sipResponse.Header.From.FromURI != null) ? sipResponse.Header.From.FromURI.User : "Error on From header";
            FireSIPMonitorEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, message, fromUser, localSIPEndPoint, endPoint));
        }

        private void LogSIPBadRequestIn(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, string sipMessage, SIPValidationFieldsEnum errorField, string rawMessage)
        {
            string errorMessage = "App Svr Bad Request Received: " + localSIPEndPoint + "<-" + endPoint.ToString() + ", " + errorField + ". " + sipMessage;
            FireSIPMonitorEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.BadSIPMessage, errorMessage, null));
            if (rawMessage != null)
            {
                FireSIPMonitorEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, errorMessage + "\r\n" + rawMessage, null));
            }
        }

        private void LogSIPBadResponseIn(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, string sipMessage, SIPValidationFieldsEnum errorField, string rawMessage)
        {
            string errorMessage = "App Svr Bad Response Received: " + localSIPEndPoint + "<-" + endPoint.ToString() + ", " + errorField + ". " + sipMessage;
            FireSIPMonitorEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.BadSIPMessage, errorMessage, null));
            if (rawMessage != null)
            {
                FireSIPMonitorEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, errorMessage + "\r\n" + rawMessage, null));
            }
        }

        #endregion
    }
}
