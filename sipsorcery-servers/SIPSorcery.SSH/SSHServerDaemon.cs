// ============================================================================
// FileName: SSHServerDaemon.cs
//
// Description:
// A daemon to configure and start the SIP Sorcery SSH Server.
//
// Author(s):
// Aaron Clauson
//
// History:
// 18 Nov 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, London UK
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
using System.ServiceModel;
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
using NSsh.Server.Configuration;
using NSsh.Server.Services;
using NSsh.Server.TransportLayer;
using NSsh.Server.TransportLayer.State;

namespace SIPSorcery.SSHServer
{
    public class SSHServerDaemon
    {
        private ILog logger = AppState.logger;

        private string m_nsshConfigurationFilePath = SSHServerState.NSSHConfigurationFilePath;
        private string m_udpNotificationReceiverSocket = SSHServerState.MonitorEventReceiveSocket;

        private ISshService m_sshService;
        private CustomerSessionManager m_customerSessionManager;
        private ISIPMonitorPublisher m_monitorPublisher;

        public SSHServerDaemon(CustomerSessionManager customerSessionManager)
        {
            m_customerSessionManager = customerSessionManager;
            if (m_udpNotificationReceiverSocket.IsNullOrBlank())
            {
                m_monitorPublisher = new MonitorProxyManager();
            }
            else
            {
                m_monitorPublisher = new SIPMonitorUDPSink(m_udpNotificationReceiverSocket);
            }
        }

        public SSHServerDaemon(CustomerSessionManager customerSessionManager, ISIPMonitorPublisher monitorPublisher)
        {
            m_customerSessionManager = customerSessionManager;
            m_monitorPublisher = monitorPublisher;
        }

        public void Start()
        {
            try
            {
                logger.Debug("SSHServerDaemon starting...");

                NSshServiceConfiguration configuration = NSshServiceConfiguration.LoadFromFile(m_nsshConfigurationFilePath);
                SetupDependencies(configuration);

                m_sshService = Dependency.Resolve<ISshService>();
                ((NSshService)m_sshService).Start();
            }
            catch (Exception excp)
            {
                logger.Error("Exception SSHServerDaemon Start. " + excp.Message);
            }
        }

        public void Stop()
        {
            try
            {
                logger.Debug("SSHServerDaemon Stopped.");

                if (m_sshService != null)
                {
                    ((NSshService)m_sshService).Stop();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SSHServerDaemon Stop. " + excp.Message);
            }
        }

        private void SetupDependencies(NSshServiceConfiguration configuration)
        {
            Dependency.RegisterInstance<NSshServiceConfiguration>("NSshServiceConfiguration", configuration);
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
            Dependency.RegisterSingleton<IPasswordAuthenticationService, SIPSorcerySSHAuthenticationService>("PasswordAuthenticationService");
            Dependency.RegisterTransient<IChannel, Channel>("Channel");
            Dependency.RegisterTransient<IChannelConsumer, SIPSorceryChannelConsumer>(ChannelRequestType.PseudoTerminal.ToString());
            Dependency.RegisterTransient<IChannelConsumer, SIPSorceryChannelConsumer>(ChannelRequestType.Shell.ToString());
            Dependency.RegisterSingleton<IPacketFactory, PacketFactory>("PacketFactory");

            // SIPSorcery specific registrations.
            Dependency.RegisterInstance<CustomerSessionManager>("CustomerSessionManager", m_customerSessionManager);
            Dependency.RegisterInstance<ISIPMonitorPublisher>("ISIPMonitorPublisher", m_monitorPublisher);
        }
    }
}
