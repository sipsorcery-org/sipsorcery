// ============================================================================
// FileName: WebState.cs
//
// Description:
// Application configuration for the assembly.
//
// Author(s):
// Aaron Clauson
//
// History:
// 08 Sep 2009	Aaron Clauson	Created.
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
using SIPSorcery.Persistence;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.CRM.Web
{
    /// <summary>
    /// Retrieves application conifguration settings from App.Config.
    /// </summary>
    public class WebState : IConfigurationSectionHandler
    {
        private const string LOGGER_NAME = "crmweb";
        private const string CUSTOMER_VALIDATION_RULES = "validationrules";

        private static readonly string m_storageTypeKey = SIPSorceryConfiguration.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = SIPSorceryConfiguration.PERSISTENCE_STORAGECONNSTR_KEY;

        public static ILog logger;

        public static StorageTypes CRMStorageType;
        public static string CRMStorageConnStr;
        public static Dictionary<string, string> ValidationRules = new Dictionary<string,string>();

        static WebState() {
            try {
                #region Configure logging.

                try {

                    log4net.Config.XmlConfigurator.Configure();
                    logger = log4net.LogManager.GetLogger(LOGGER_NAME);
                }
                catch (Exception logExcp) {
                    Console.WriteLine("Exception SIPProxyState Configure Logging. " + logExcp.Message);
                }

                #endregion

                CRMStorageType = (ConfigurationManager.AppSettings[m_storageTypeKey] != null) ? StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[m_storageTypeKey]) : StorageTypes.Unknown;
                CRMStorageConnStr = ConfigurationManager.AppSettings[m_connStrKey];

                if (CRMStorageType == StorageTypes.Unknown || CRMStorageConnStr.IsNullOrBlank()) {
                    logger.Error("The SIPSorcery.CRM.Web does not have any persistence settings configured.");
                }

                XmlNode validationRulesNode = (XmlNode)ConfigurationManager.GetSection(CUSTOMER_VALIDATION_RULES);
                if (validationRulesNode != null) {
                    foreach (XmlNode validationNode in validationRulesNode) {
                         ValidationRules.Add(validationNode.SelectSingleNode("field").InnerText, validationNode.SelectSingleNode("rule").InnerText);
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception WebState. " + excp.Message);
            }
        }

        /// <summary>
        /// Handler for processing the App.Config file and passing retrieving any config nodes.
        /// </summary>
        public object Create(object parent, object context, XmlNode configSection) {
            return configSection;
        }
    }
}
