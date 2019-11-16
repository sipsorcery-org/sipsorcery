//-----------------------------------------------------------------------------
// Filename: SIPSoftPhoneState.cs
//
// Description: A helper class to load the application's settings and to hold some application wide variables. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 27 Mar 2012	Aaron Clauson	Refactored, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Configuration;
using System.Net;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public class SIPSoftPhoneState
    {
        private const string SIPSOFTPHONE_CONFIGNODE_NAME = "sipsoftphone";
        private const string SIPSOCKETS_CONFIGNODE_NAME = "sipsockets";
        private const string STUN_SERVER_KEY = "STUNServerHostname";
        private const string VIDEO_DEVICE_INDEX_KEY = "VideoDeviceIndex";
        private const int DEFAULT_VIDEO_DEVICE_INDEX = 0;

        private static ILog logger = AppState.logger;

        private static readonly XmlNode m_sipSoftPhoneConfigNode;
        public static readonly XmlNode SIPSocketsNode;
        public static readonly string STUNServerHostname;

        public static readonly string SIPUsername = ConfigurationManager.AppSettings["SIPUsername"];    // Get the SIP username from the config file.
        public static readonly string SIPPassword = ConfigurationManager.AppSettings["SIPPassword"];    // Get the SIP password from the config file.
        public static readonly string SIPServer = ConfigurationManager.AppSettings["SIPServer"];        // Get the SIP server from the config file.
        public static readonly string SIPFromName = ConfigurationManager.AppSettings["SIPFromName"];    // Get the SIP From display name from the config file.
        public static readonly string DnsServer = ConfigurationManager.AppSettings["DnsServer"];        // Get the optional DNS server from the config file.

        public static IPAddress DefaultLocalAddress;

        public static IPAddress PublicIPAddress;

        static SIPSoftPhoneState()
        {
            try
            {
                if (ConfigurationManager.GetSection(SIPSOFTPHONE_CONFIGNODE_NAME) != null)
                {
                    m_sipSoftPhoneConfigNode = (XmlNode)ConfigurationManager.GetSection(SIPSOFTPHONE_CONFIGNODE_NAME);
                }

                if (m_sipSoftPhoneConfigNode != null)
                {
                    SIPSocketsNode = m_sipSoftPhoneConfigNode.SelectSingleNode(SIPSOCKETS_CONFIGNODE_NAME);
                }

                STUNServerHostname = ConfigurationManager.AppSettings[STUN_SERVER_KEY];

                DefaultLocalAddress = LocalIPConfig.GetDefaultIPv4Address();
                logger.Debug("Default local IPv4 address determined as " + DefaultLocalAddress + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPSoftPhoneState. " + excp.Message);
                throw;
            }
        }
    }
}
