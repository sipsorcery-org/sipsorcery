// ============================================================================
// FileName: SIPMonitorState.cs
//
// Description:
// Application configuration for a SIP Monitor Server.
//
// Author(s):
// Aaron Clauson
//
// History:
// 25 mar 2009	Aaron Clauson	Created.
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
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIPMonitor
{
    /// <summary>
    /// Retrieves application conifguration settings from App.Config.
    /// </summary>
    public class SIPMonitorState : IConfigurationSectionHandler
    {
        private const string LOGGER_NAME = "sipmonitor";

        public const string SIPMONITOR_CONFIGNODE_NAME = "sipmonitor";

        //private const string SIPMONITOR_CLIENT_SOCKETS_NODE_NAME = "sipmonitorclientsockets";
        //private const string SIPMONITOR_MACHINE_SOCKETS_NODE_NAME = "sipmonitormachinesockets";
        private const string SIPMONITOR_LOOPBACK_EVENTPORT_KEY = "MonitorLoopbackPort";
        //private const string SIPMONITOR_SILVELRIGHT_POLICY_FILE_PATH = "SilverlightPolicyFilePath";
        private const string SIPMONITOR_SERVER_ID_KEY = "SIPMonitorServerID";

        public static ILog logger;

        private static XmlNode m_sipMonitorConfigNode;
       // public static readonly XmlNode MonitorClientSocketsNode;
        //public static readonly XmlNode MonitorMachineSocketsNode;
        public static readonly int MonitorLoopbackPort;
        //public static readonly string SilverlightPolicyFilePath;
        public static readonly string SIPMonitorServerID;

        static SIPMonitorState()
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
                    Console.WriteLine("Exception SIPMonitorState Configure Logging. " + logExcp.Message);
                }

                #endregion

                if (ConfigurationManager.GetSection(SIPMONITOR_CONFIGNODE_NAME) != null) {
                    m_sipMonitorConfigNode = (XmlNode)ConfigurationManager.GetSection(SIPMONITOR_CONFIGNODE_NAME);
                }
                if (m_sipMonitorConfigNode == null) {
                    logger.Warn("The SIP Monitor Agent " + SIPMONITOR_CONFIGNODE_NAME + " config node was not available, the agent will not be able to start.");
                }
                else {
                    //MonitorClientSocketsNode = m_sipMonitorConfigNode.SelectSingleNode(SIPMONITOR_CLIENT_SOCKETS_NODE_NAME);
                    //MonitorMachineSocketsNode = m_sipMonitorConfigNode.SelectSingleNode(SIPMONITOR_MACHINE_SOCKETS_NODE_NAME);
                    Int32.TryParse(AppState.GetConfigNodeValue(m_sipMonitorConfigNode, SIPMONITOR_LOOPBACK_EVENTPORT_KEY), out MonitorLoopbackPort);
                    //SilverlightPolicyFilePath = AppState.GetConfigNodeValue(m_sipMonitorConfigNode, SIPMONITOR_SILVELRIGHT_POLICY_FILE_PATH);
                    SIPMonitorServerID = AppState.GetConfigNodeValue(m_sipMonitorConfigNode, SIPMONITOR_SERVER_ID_KEY);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorState. " + excp.Message);
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
