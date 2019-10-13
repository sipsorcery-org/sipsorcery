//-----------------------------------------------------------------------------
// Filename: ProxyState.cs
//
// Description: A helper class to load the application's settings and to hold some application wide variables. 
// 
// History:
// 15 Nov 2016	Aaron Clauson	Refactored.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2016 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery Ltd. 
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
//-----------------------------------------------------------------------------

using System;
using System.Configuration;
using System.Net;
using System.Xml;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIPProxy
{ 
    public class ProxyState : IConfigurationSectionHandler
    {
        private const string SIPPROXY_CONFIGNODE_NAME = "proxy";
        private const string SIPSOCKETS_CONFIGNODE_NAME = "sipsockets";
  
        private static readonly XmlNode m_proxyConfigNode;
        public static readonly XmlNode SIPSocketsNode;
        
        public static IPAddress DefaultLocalAddress;

        static ProxyState()
        {
            try
            {
                var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => {
                    builder
                    .AddFilter("*", LogLevel.Debug)
                    .AddConsole();
                });
                SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;

                if (ConfigurationManager.GetSection(SIPPROXY_CONFIGNODE_NAME) != null)
                {
                    m_proxyConfigNode = (XmlNode)ConfigurationManager.GetSection(SIPPROXY_CONFIGNODE_NAME);
                }

                if (m_proxyConfigNode != null)
                {
                    SIPSocketsNode = m_proxyConfigNode.SelectSingleNode(SIPSOCKETS_CONFIGNODE_NAME);
                }

                DefaultLocalAddress = LocalIPConfig.GetDefaultIPv4Address();
                SIPSorcery.Sys.Log.Logger.LogDebug("Default local IPv4 address determined as " + DefaultLocalAddress + ".");
            }
            catch (Exception excp)
            {
                SIPSorcery.Sys.Log.Logger.LogError("Exception ProxyState. " + excp.Message);
                throw;
            }
        }

        /// <summary>
        /// Handler for processing the App.Config file and retrieving a custom XML node.
        /// </summary>
        public object Create(object parent, object context, XmlNode configSection)
        {
            return configSection;
        }
    }
}
