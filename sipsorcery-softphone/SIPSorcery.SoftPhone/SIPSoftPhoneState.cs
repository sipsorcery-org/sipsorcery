using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Xml;
using log4net;

namespace SIPSorcery.SoftPhone {
    public class SIPSoftPhoneState : IConfigurationSectionHandler {
         private const string LOGGER_NAME = "sipsoftphone";

        private const string SIPSOFTPHONE_CONFIGNODE_NAME = "sipsoftphone";
        private const string SIPSOCKETS_CONFIGNODE_NAME = "sipsockets";
        private const string STUN_SERVER_KEY = "STUNServerHostname";

        public static ILog logger;

        private static readonly XmlNode m_sipSoftPhoneConfigNode;
        public static readonly XmlNode SIPSocketsNode;
        public static readonly string STUNServerHostname;

        static SIPSoftPhoneState()
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
                    Console.WriteLine("Exception SIPSoftPhoneState Configure Logging. " + logExcp.Message);
                }

                #endregion

                if (ConfigurationManager.GetSection(SIPSOFTPHONE_CONFIGNODE_NAME) != null) {
                    m_sipSoftPhoneConfigNode = (XmlNode)ConfigurationManager.GetSection(SIPSOFTPHONE_CONFIGNODE_NAME);
                }

                if (m_sipSoftPhoneConfigNode != null) {
                    SIPSocketsNode = m_sipSoftPhoneConfigNode.SelectSingleNode(SIPSOCKETS_CONFIGNODE_NAME);
                }

                STUNServerHostname = ConfigurationManager.AppSettings[STUN_SERVER_KEY];
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPSoftPhoneState. " + excp.Message);
                throw;
            }
        }

        /// <summary>
        /// Handler for processing the App.Config file and passing retrieving the App.Config node.
        /// </summary>
        public object Create(object parent, object context, XmlNode configSection) {
            return configSection;
        }
    }
}
