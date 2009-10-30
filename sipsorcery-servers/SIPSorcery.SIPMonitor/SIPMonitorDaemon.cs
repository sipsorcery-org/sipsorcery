// ============================================================================
// FileName: SIPMonitorDaemon.cs
//
// Description:
// A daemon to configure and start a SIP Monitor Server Agent.
//
// Author(s):
// Aaron Clauson
//
// History:
// 25 Mar 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Xml;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.Servers;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;
using log4net;
using NSsh.Common.Utility;
using NSsh.Common.Packets;
using NSsh.Common.Packets.Channel;
using NSsh.Server;
using NSsh.Server.ChannelLayer;
using NSsh.Server.ChannelLayer.Console;
using NSsh.Server.Configuration;
using NSsh.Server.Services;
using NSsh.Server.TransportLayer;
using NSsh.Server.TransportLayer.State;

namespace SIPSorcery.SIPMonitor
{
    public class SIPMonitorDaemon
    {
        private ILog logger = AppState.logger;

        private XmlNode m_monitorClientSocketsNode = SIPMonitorState.MonitorClientSocketsNode;
        private XmlNode m_monitorMachineSocketsNode = SIPMonitorState.MonitorMachineSocketsNode;
        private int m_monitorLoopbackListenerPort = SIPMonitorState.MonitorLoopbackPort;
        private string m_silverlightPolicyFilePath = SIPMonitorState.SilverlightPolicyFilePath;
        private string m_nsshConfigurationFilePath = SIPMonitorState.NSSHConfigurationFilePath;

        private SIPMonitorMediator m_sipMonitorMediator;
        private CustomerSessionManager m_customerSessionManager;

        public SIPMonitorDaemon(CustomerSessionManager customerSessionManager)
        {
            m_customerSessionManager = customerSessionManager;
        }

        public void Start()
        {
            try {
                logger.Debug("SIPMonitorDaemon starting...");

                if (m_monitorLoopbackListenerPort == 0) {
                    throw new ApplicationException("Cannot start SIP Monitor with no loopback listener port specified.");
                }
                
                List<IPEndPoint> clientMonitorEndPoints = null;
                List<IPEndPoint> machineMonitorEndPoints = null;

                /*if (m_monitorClientSocketsNode == null) {
                    throw new ApplicationException("Cannot start SIP Monitor with no client socket node.");
                }
                else {
                    clientMonitorEndPoints = LocalIPConfig.ParseIPSockets(m_monitorClientSocketsNode);
                }*/

                if (m_monitorMachineSocketsNode != null) {
                    machineMonitorEndPoints = LocalIPConfig.ParseIPSockets(m_monitorMachineSocketsNode);
                }

                m_sipMonitorMediator = new SIPMonitorMediator(
                    //clientMonitorEndPoints.ToArray(),
                    null,
                    machineMonitorEndPoints.ToArray(),
                    m_monitorLoopbackListenerPort,
                    null,
                    m_customerSessionManager.Authenticate,
                    m_customerSessionManager.Authenticate,
                    m_customerSessionManager.CustomerPersistor.Get);
                m_sipMonitorMediator.StartMonitoring();

                logger.Debug("The SIP Monitor Server was successfully started, loopback port " + m_monitorLoopbackListenerPort + ".");
               // foreach (IPEndPoint clientEndPoint in clientMonitorEndPoints) {
                //    logger.Debug(" Listenng for client monitor connections on " + clientEndPoint + ".");
               // }
                foreach (IPEndPoint machineEndPoint in machineMonitorEndPoints) {
                    logger.Debug(" Listenng for machine monitor connections on " + machineEndPoint + ".");
                }

                if (!m_silverlightPolicyFilePath.IsNullOrBlank()) {
                    // Start the Silverlight Policy server so that Silverlight clients can connect to the monitor port.
                    SilverlightPolicyServer silverlightPolicyServer = new SilverlightPolicyServer(m_silverlightPolicyFilePath);
                }

                NSshServiceConfiguration configuration = NSshServiceConfiguration.LoadFromFile(m_nsshConfigurationFilePath);
                SetupDependencies(configuration);

                NSshService service = new NSshService();
                service.Start();
            }
            catch (Exception excp) {
                logger.Error("Exception SIPMonitorDaemon Start. " + excp.Message);
            }
        }

        public void Stop()
        {
            try
            {
                logger.Debug("SIPMonitorDaemon Stopped.");

                m_sipMonitorMediator.Stop();
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorDaemon Stop. " + excp.Message);
            }
        }

        private void SetupDependencies(NSshServiceConfiguration configuration) {
            Dependency.RegisterInstance<NSshServiceConfiguration>("NSshServiceConfiguration", configuration);
            Dependency.RegisterInstance<SIPMonitorMediator>("SIPMonitorMediator", m_sipMonitorMediator);
            Dependency.RegisterSingleton<ISshService, NSshService>("NSshService");
            Dependency.RegisterTransient<ISshSession, SshSession>("SshSession");
            Dependency.RegisterTransient<ITransportLayerManager, TransportLayerManager>("TransportLayerManager");
            Dependency.RegisterTransient<AbstractTransportState, ConnectedState>(TransportLayerState.Connected.ToString());
            Dependency.RegisterTransient<AbstractTransportState, VersionsExchangedState>(TransportLayerState.VersionsExchanged.ToString());
            Dependency.RegisterTransient<AbstractTransportState, KeysExchangedState>(TransportLayerState.KeysExchanged.ToString());
            Dependency.RegisterTransient<AbstractTransportState, AuthenticatedState>(TransportLayerState.Authenticated.ToString());
            Dependency.RegisterSingleton<IKeySetupService, KeySetupService>("KeySetupService");
            Dependency.RegisterSingleton<ISecureRandom, SecureRandom>("SecureRandom");
            Dependency.RegisterSingleton<ICipherFactory, CipherFactory>("CipherFactory");
            Dependency.RegisterSingleton<IMacFactory, MacFactory>("MacFactory");
            Dependency.RegisterInstance<CustomerSessionManager>("CustomerSessionManager", m_customerSessionManager);
            Dependency.RegisterSingleton<IPasswordAuthenticationService, SIPSorcerySSHAuthenticationService>("PasswordAuthenticationService");
            Dependency.RegisterTransient<IChannel, Channel>("Channel");
            Dependency.RegisterTransient<IChannelConsumer, SIPSorceryChannelConsumer>(ChannelRequestType.PseudoTerminal.ToString());
            Dependency.RegisterTransient<IChannelConsumer, SIPSorceryChannelConsumer>(ChannelRequestType.Shell.ToString());
            Dependency.RegisterSingleton<IPacketFactory, PacketFactory>("PacketFactory");
        }
    }

    public class SIPSorcerySSHAuthenticationService : IPasswordAuthenticationService {

        private CustomerSessionManager m_customerSessionManager;

        public SIPSorcerySSHAuthenticationService(CustomerSessionManager customerSessionManager) {
            m_customerSessionManager = customerSessionManager;
        }

        public IIdentity CreateIdentity(string username, string password) {
            CustomerSession customerSession = m_customerSessionManager.Authenticate(username, password, null);
            if (customerSession != null) {
                return new GenericIdentity(username);
            }
            else {
                return null;
            }
        }
    }

    public class SIPSorceryChannelConsumer : BaseConsoleChannelConsumer {
        protected override IConsole CreateConsole() {
            return new SIPSorceryConsole(base.AuthenticatedIdentity.Name);
        }
    }

    public class SIPSorceryConsole : IConsole {

        public TextWriter StandardInput { get; private set; }
        public TextReader StandardOutput { get; private set; }
        public TextReader StandardError { get; private set; }

        private SIPMonitorClientConnection m_clientConnection;

        public event EventHandler Closed;

        public bool HasClosed {
            get { return m_clientConnection.HasClosed; }
        }

        public SIPSorceryConsole(string username) {
            SIPMonitorMediator mediator = Dependency.Resolve<SIPMonitorMediator>();
            m_clientConnection = mediator.NewAuthenticatedClient(username);
            m_clientConnection.Closed += (sender, args) => { Close(); };
            StandardError = new StreamReader(m_clientConnection.ErrorStream);
            StandardOutput = new StreamReader(m_clientConnection.OutStream);
            StandardInput = new StreamWriter(m_clientConnection.InStream); 
        }

        public void Close() {
            if (!m_clientConnection.HasClosed) {
                m_clientConnection.Close();
            }

            if (Closed != null) {
                Closed(this, new EventArgs());
            }
        }
    }
}
