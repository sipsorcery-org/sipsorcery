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
        private const string LOGGER_NAME = "sipsporcery-app";
        private const string SIPAPPSERVER_CONFIGNODE_NAME = "sipappserver";         // Config node names for each of the agents.
        private const string SIPSOCKETS_CONFIGNODE_NAME = "sipsockets";
        private const string APPSERVER_LOOPBACK_PORT_KEY = "MonitorLoopbackPort";
        private const string TRACE_DIRECTORY_KEY = "TraceDirectory";
        private const string RUBY_SCRIPT_COMMON_PATH_KEY = "RubyScriptCommonPath";
        private const string OUTBOUND_PROXY_KEY = "OutboundProxy";
        private const string NAT_KEEPALIVE_RELAY_SOCKET_KEY = "NATKeepAliveRelaySocket";

		public static ILog logger = null;

        private static readonly XmlNode m_sipAppServerConfigNode;
        public static readonly XmlNode SIPAppServerSocketsNode;
        public static readonly int MonitorLoopbackPort;
        public static readonly string CurrentDirectory;
        public static readonly string TraceDirectory;
        public static readonly string RubyScriptCommonPath;
        public static readonly SIPEndPoint OutboundProxy;
        public static readonly IPEndPoint NATKeepAliveRelaySocket;

		static SIPAppServerState()
		{
			try
			{
                logger = AppState.GetLogger(LOGGER_NAME);
                CurrentDirectory = Regex.Replace(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().CodeBase), @"^file:\\", ""); // There's undoubtedly a better way!

                if (ConfigurationManager.GetSection(SIPAPPSERVER_CONFIGNODE_NAME) != null)
                {
                    m_sipAppServerConfigNode = (XmlNode)ConfigurationManager.GetSection(SIPAPPSERVER_CONFIGNODE_NAME);
                }
                else
                {
                    throw new ApplicationException("The SIP Application Server could not be started, no " + SIPAPPSERVER_CONFIGNODE_NAME + " config node available.");
                }

                SIPAppServerSocketsNode = m_sipAppServerConfigNode.SelectSingleNode(SIPSOCKETS_CONFIGNODE_NAME);
                if (SIPAppServerSocketsNode == null)
                {
                    throw new ApplicationException("The SIP Proxy could not be started, no " + SIPSOCKETS_CONFIGNODE_NAME + " node could be found.");
                }

                Int32.TryParse(AppState.GetConfigNodeValue(m_sipAppServerConfigNode, APPSERVER_LOOPBACK_PORT_KEY), out MonitorLoopbackPort);
                TraceDirectory = AppState.GetConfigNodeValue(m_sipAppServerConfigNode, TRACE_DIRECTORY_KEY);
                RubyScriptCommonPath = AppState.GetConfigNodeValue(m_sipAppServerConfigNode, RUBY_SCRIPT_COMMON_PATH_KEY);
                if (!AppState.GetConfigNodeValue(m_sipAppServerConfigNode, OUTBOUND_PROXY_KEY).IsNullOrBlank())
                {
                    OutboundProxy = SIPEndPoint.ParseSIPEndPoint(AppState.GetConfigNodeValue(m_sipAppServerConfigNode, OUTBOUND_PROXY_KEY));
                }
			}
			catch(Exception excp)
			{
				logger.Error("Exception SIPAppServerState. " + excp.Message);
                Console.WriteLine("Exception SIPAppServerState. " + excp.Message);	// In case the logging configuration is what caused the exception.
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