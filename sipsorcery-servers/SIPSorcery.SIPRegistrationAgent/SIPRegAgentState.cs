// ============================================================================
// FileName: SIPRegAgentState.cs
//
// Description:
// Application configuration for a Stateless SIP Proxy Server.
//
// Author(s):
// Aaron Clauson
//
// History:
// 29 Mar 2009	Aaron Clauson	Created.
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
using SIPSorcery.SIP;
using SIPSorcery.Sys;

namespace SIPSorcery.SIPRegistrationAgent
{
    /// <summary>
    /// Retrieves application conifguration settings from App.Config.
    /// </summary>
    public class SIPRegAgentState : IConfigurationSectionHandler
    {
        private const string LOGGER_NAME = "sipregagent";

        public const string SIPREGAGENT_CONFIGNODE_NAME = "sipregistrationagent";

        private const string SIPSOCKETS_CONFIGNODE_NAME = "sipsockets";
        private const string MONITOR_LOOPBACK_PORT_KEY = "MonitorLoopbackPort";
        private const string OUTBOUND_PROXY_KEY = "OutboundProxy";
        private const string THREAD_COUNT_KEY = "ThreadCount";

        public static ILog logger;

        private static readonly XmlNode m_sipRegAgentNode;
        public static readonly XmlNode SIPRegAgentSocketsNode;
        public static readonly int MonitorLoopbackPort;
        public static readonly SIPEndPoint OutboundProxy;
        public static readonly int ThreadCount = 1;

        static SIPRegAgentState()
        {
            try
            {
                #region Configure logging.

                try
                {

                    log4net.Config.XmlConfigurator.Configure();
                    logger = log4net.LogManager.GetLogger(LOGGER_NAME);
                }
                catch (Exception logExcp)
                {
                    Console.WriteLine("Exception SIPProxyState Configure Logging. " + logExcp.Message);
                }

                #endregion

                if (ConfigurationManager.GetSection(SIPREGAGENT_CONFIGNODE_NAME) != null)
                {
                    m_sipRegAgentNode = (XmlNode)ConfigurationManager.GetSection(SIPREGAGENT_CONFIGNODE_NAME);
                }

                if (m_sipRegAgentNode == null)
                {
                    logger.Warn("The SIP Registration Agent " + SIPREGAGENT_CONFIGNODE_NAME + " config node was not available, the agent will not be able to start.");
                }
                else
                {
                    SIPRegAgentSocketsNode = m_sipRegAgentNode.SelectSingleNode(SIPSOCKETS_CONFIGNODE_NAME);
                    Int32.TryParse(AppState.GetConfigNodeValue(m_sipRegAgentNode, MONITOR_LOOPBACK_PORT_KEY), out MonitorLoopbackPort);
                    if (!AppState.GetConfigNodeValue(m_sipRegAgentNode, OUTBOUND_PROXY_KEY).IsNullOrBlank())
                    {
                        OutboundProxy = SIPEndPoint.ParseSIPEndPoint(AppState.GetConfigNodeValue(m_sipRegAgentNode, OUTBOUND_PROXY_KEY));
                    }
                    if (!AppState.GetConfigNodeValue(m_sipRegAgentNode, THREAD_COUNT_KEY).IsNullOrBlank())
                    {
                        Int32.TryParse(AppState.GetConfigNodeValue(m_sipRegAgentNode, THREAD_COUNT_KEY), out ThreadCount);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegAgentState. " + excp.Message);
                throw;
            }
        }

        /// <summary>
        /// Handler for processing the App.Config file and passing retrieving the proxy config node.
        /// </summary>
        public object Create(object parent, object context, XmlNode configSection)
        {
            return configSection;
        }
    }
}
