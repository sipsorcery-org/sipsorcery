//-----------------------------------------------------------------------------
// Filename: ProxyState.cs
//
// Description: A helper class to load the application's settings and to hold some application wide variables. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 15 Nov 2016	Aaron Clauson	Refactored, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using System;
using System.Configuration;
using System.Net;
using System.Xml;

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
                var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
                {
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
