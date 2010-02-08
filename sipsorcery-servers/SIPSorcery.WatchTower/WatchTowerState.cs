// ============================================================================
// FileName: SIPWatchTowerState.cs
//
// Description:
// Holds application configuration information.
//
// Author(s):
// Aaron Clauson
//
// History:
// 23 Nov 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using SIPSorcery.SIP;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.WatchTower
{
    /// <summary>
    /// This class maintains static application configuration settings that can be used by all classes within
    /// the AppDomain. This class is the one stop shop for retrieving or accessing application configuration settings.
    /// </summary>
    public class WatchTowerState
    {
        private const string LOGGER_NAME = "sipsporcery-watchtower";
        private const string WATCHTOWER_CONFIGNODE_NAME = "watchtower";         // Config node names for each of the agents.
        private const string SIPSOCKETS_CONFIGNODE_NAME = "sipsockets";
        private const string MONITOR_LOOPBACK_PORT_KEY = "MonitorLoopbackPort";
        private const string PROXY_APPSERVER_ENDPOINTS_PATH_KEY = "AppServerEndPointsPath";
        private const string SIAPPSERVER_WORKERS_NODE_NAME = "sipappserverworkers";

        public static ILog logger = null;

        private static readonly XmlNode m_watchtowerConfigNode;
        public static readonly XmlNode SIPSocketsNode;
        public static readonly int MonitorLoopbackPort;
        public static readonly string CurrentDirectory;
        public static readonly string AppServerEndPointsPath;
        public static readonly XmlNode SIPAppServerWorkersNode;

        static WatchTowerState()
        {
            try
            {
                logger = AppState.GetLogger(LOGGER_NAME);
                CurrentDirectory = Regex.Replace(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().CodeBase), @"^file:\\", ""); // There's undoubtedly a better way!

                if (AppState.GetSection(WATCHTOWER_CONFIGNODE_NAME) != null)
                {
                    m_watchtowerConfigNode = (XmlNode)AppState.GetSection(WATCHTOWER_CONFIGNODE_NAME);
                }
                else
                {
                    throw new ApplicationException("The WatchTower Server could not be started, no " + WATCHTOWER_CONFIGNODE_NAME + " config node available.");
                }

                SIPSocketsNode = m_watchtowerConfigNode.SelectSingleNode(SIPSOCKETS_CONFIGNODE_NAME);

                Int32.TryParse(AppState.GetConfigNodeValue(m_watchtowerConfigNode, MONITOR_LOOPBACK_PORT_KEY), out MonitorLoopbackPort);
                AppServerEndPointsPath = AppState.GetConfigNodeValue(m_watchtowerConfigNode, PROXY_APPSERVER_ENDPOINTS_PATH_KEY);
                SIPAppServerWorkersNode = m_watchtowerConfigNode.SelectSingleNode(SIAPPSERVER_WORKERS_NODE_NAME);
            }
            catch (Exception excp)
            {
                logger.Error("Exception WatchTowerState. " + excp.Message);
                Console.WriteLine("Exception WatchTowerState. " + excp.Message);	// In case the logging configuration is what caused the exception.
                throw excp;
            }
        }
    }
}