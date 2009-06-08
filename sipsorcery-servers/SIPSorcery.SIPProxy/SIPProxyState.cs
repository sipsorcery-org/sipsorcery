// ============================================================================
// FileName: SIPProxyState.cs
//
// Description:
// Application configuration for a Stateless SIP Proxy Server.
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
using System.Configuration;
using System.Linq;
using System.Text;
using System.Xml;
using log4net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIPProxy
{
    /// <summary>
    /// Retrieves application conifguration settings from App.Config.
    /// </summary>
    public class SIPProxyState : IConfigurationSectionHandler
    {
        private const string LOGGER_NAME = "sipproxy";

        private const string SIPPROXY_CONFIGNODE_NAME = "sipproxy";
        private const string SIPSOCKETS_CONFIGNODE_NAME = "sipsockets";
        private const string PROXY_SCRIPTPATH_KEY = "ProxyScriptPath";
        private const string PROXY_LOOPBACK_PORT_KEY = "MonitorLoopbackPort";
        private const string PROXY_NATKEEPALIVESOCKET_KEY = "NATKeepAliveSocket";

        public static ILog logger;

        private static readonly XmlNode m_sipProxyNode;
        public static readonly XmlNode SIPProxySocketsNode;       
        public static readonly string ProxyScriptPath;
        public static readonly int MonitorLoopbackPort;
        public static readonly string NATKeepAliveSocket;

        static SIPProxyState()
        {
            try
            {
                logger = AppState.GetLogger(LOGGER_NAME);

                if (ConfigurationManager.GetSection(SIPPROXY_CONFIGNODE_NAME) != null) {
                    m_sipProxyNode = (XmlNode)ConfigurationManager.GetSection(SIPPROXY_CONFIGNODE_NAME);
                }
                else {
                    throw new ApplicationException("The SIP Proxy could not be started, no " + SIPPROXY_CONFIGNODE_NAME + " config node available.");
                }

                SIPProxySocketsNode = m_sipProxyNode.SelectSingleNode(SIPSOCKETS_CONFIGNODE_NAME);
                if (SIPProxySocketsNode == null) {
                    throw new ApplicationException("The SIP Proxy could not be started, no " + SIPSOCKETS_CONFIGNODE_NAME + " node could be found.");
                }

                ProxyScriptPath =  AppState.GetConfigNodeValue(m_sipProxyNode, PROXY_SCRIPTPATH_KEY);
                NATKeepAliveSocket = AppState.GetConfigNodeValue(m_sipProxyNode, PROXY_NATKEEPALIVESOCKET_KEY);
                Int32.TryParse(AppState.GetConfigNodeValue(m_sipProxyNode, PROXY_LOOPBACK_PORT_KEY), out MonitorLoopbackPort);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPProxyState. " + excp.Message);
                throw;
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
