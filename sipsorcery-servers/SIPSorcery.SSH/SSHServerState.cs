// ============================================================================
// FileName: SSHServerState.cs
//
// Description:
// Application configuration state for a SIP Sorcery SSH Server.
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
using System.Configuration;
using System.Linq;
using System.Text;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SSHServer
{
    /// <summary>
    /// Retrieves application conifguration settings from App.Config.
    /// </summary>
    public class SSHServerState : IConfigurationSectionHandler
    {
        private const string LOGGER_NAME = "sshserver";

        public const string SSHSERVER_CONFIGNODE_NAME = "sshserver";

        private const string NSSH_CONFIGURATION_FILE_PATH_KEY = "NSSHConfigurationFilePath";

        public static ILog logger;

        private static XmlNode m_sshServerConfigNode;
        public static readonly string NSSHConfigurationFilePath;

        static SSHServerState()
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
                    Console.WriteLine("Exception SSHServerState Configure Logging. " + logExcp.Message);
                }

                #endregion

                if (ConfigurationManager.GetSection(SSHSERVER_CONFIGNODE_NAME) != null)
                {
                    m_sshServerConfigNode = (XmlNode)ConfigurationManager.GetSection(SSHSERVER_CONFIGNODE_NAME);
                }
                if (m_sshServerConfigNode == null)
                {
                    logger.Warn("The SSH Server " + SSHSERVER_CONFIGNODE_NAME + " config node was not available, the agent will not be able to start.");
                }
                else {
                    NSSHConfigurationFilePath = AppState.GetConfigNodeValue(m_sshServerConfigNode, NSSH_CONFIGURATION_FILE_PATH_KEY);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SSHServerState. " + excp.Message);
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
