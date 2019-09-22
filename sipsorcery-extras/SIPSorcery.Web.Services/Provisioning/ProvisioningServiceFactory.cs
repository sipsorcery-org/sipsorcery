//-----------------------------------------------------------------------------
// Filename: ProvisioningServiceFactory.cs
//
// Description: This class is a factory to create a provisioning service.
// 
// History:
// 29 Sep 2010	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Ltd, Hobart, Tasmania, Australia
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Ltd. 
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
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Web.Services
{
    public class ProvisioningServiceFactory
    {
        private const string DISABLED_PROVIDER_SERVERS_PATTERN = "DisabledProviderServersPattern";
        private const string NEW_CUSTOMERS_ALLOWED_LIMIT_KEY = "NewCustomersAllowedLimit";
        private const string INVITE_CODE_REQUIRED_KEY = "InviteCodeRequired";

        private static ILog logger = AppState.GetLogger("provisioningsvc");

        private static readonly string m_storageTypeKey = SIPSorceryConfiguration.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = SIPSorceryConfiguration.PERSISTENCE_STORAGECONNSTR_KEY;

        private static readonly string m_providersStorageFileName = AssemblyState.XML_SIPPROVIDERS_FILENAME;
        private static readonly string m_providerBindingsStorageFileName = AssemblyState.XML_SIPPROVIDERS_FILENAME;
        private static readonly string m_sipAccountsStorageFileName = AssemblyState.XML_SIPACCOUNTS_FILENAME;
        private static readonly string m_dialplansStorageFileName = AssemblyState.XML_DIALPLANS_FILENAME;
        private static readonly string m_registrarBindingsStorageFileName = AssemblyState.XML_REGISTRAR_BINDINGS_FILENAME;
        private static readonly string m_dialoguesStorageFileName = AssemblyState.XML_SIPDIALOGUES_FILENAME;
        private static readonly string m_cdrsStorageFileName = AssemblyState.XML_SIPCDRS_FILENAME;

        private static StorageTypes m_serverStorageType;
        private static string m_serverStorageConnStr;
        private static string m_disabledProviderServerPattern;
        private static int m_newCustomersAllowedLimit;
        private static bool m_inviteCodeRequired;

        public static SIPProvisioningWebService CreateProvisioningService()
        {
            try
            {
                m_serverStorageType = (ConfigurationManager.AppSettings[m_storageTypeKey] != null) ? StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[m_storageTypeKey]) : StorageTypes.Unknown;
                m_serverStorageConnStr = ConfigurationManager.AppSettings[m_connStrKey];
                Int32.TryParse(ConfigurationManager.AppSettings[NEW_CUSTOMERS_ALLOWED_LIMIT_KEY], out m_newCustomersAllowedLimit);
                Boolean.TryParse(ConfigurationManager.AppSettings[INVITE_CODE_REQUIRED_KEY], out m_inviteCodeRequired);

                if (m_serverStorageType == StorageTypes.Unknown || m_serverStorageConnStr.IsNullOrBlank())
                {
                    throw new ApplicationException("The Provisioning Web Service cannot start with no persistence settings specified.");
                }

                // Prevent users from creaing loopback or other crazy providers.
                m_disabledProviderServerPattern = ConfigurationManager.AppSettings[DISABLED_PROVIDER_SERVERS_PATTERN];
                if (!m_disabledProviderServerPattern.IsNullOrBlank())
                {
                    SIPProvider.DisallowedServerPatterns = m_disabledProviderServerPattern;
                }

                // The Registration Agent wants to know about any changes to SIP Provider entries in order to update any SIP 
                // Provider bindings it is maintaining or needs to add or remove.
                SIPAssetPersistor<SIPProvider> sipProviderPersistor = SIPAssetPersistorFactory<SIPProvider>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_providersStorageFileName);
                SIPAssetPersistor<SIPProviderBinding> sipProviderBindingsPersistor = SIPAssetPersistorFactory<SIPProviderBinding>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_providerBindingsStorageFileName);
                SIPProviderBindingSynchroniser sipProviderBindingSynchroniser = new SIPProviderBindingSynchroniser(sipProviderBindingsPersistor);

                sipProviderPersistor.Added += sipProviderBindingSynchroniser.SIPProviderAdded;
                sipProviderPersistor.Updated += sipProviderBindingSynchroniser.SIPProviderUpdated;
                sipProviderPersistor.Deleted += sipProviderBindingSynchroniser.SIPProviderDeleted;

                return new SIPProvisioningWebService(
                    SIPAssetPersistorFactory<SIPAccountAsset>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_sipAccountsStorageFileName),
                    SIPAssetPersistorFactory<SIPDialPlan>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_dialplansStorageFileName),
                    sipProviderPersistor,
                    sipProviderBindingsPersistor,
                    SIPAssetPersistorFactory<SIPRegistrarBinding>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_registrarBindingsStorageFileName),
                    SIPAssetPersistorFactory<SIPDialogueAsset>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_dialoguesStorageFileName),
                    SIPAssetPersistorFactory<SIPCDRAsset>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_cdrsStorageFileName),
                    new CustomerSessionManager(m_serverStorageType, m_serverStorageConnStr),
                    new SIPDomainManager(m_serverStorageType, m_serverStorageConnStr),
                    (e) => { logger.Debug(e.Message); },
                    m_newCustomersAllowedLimit,
                    m_inviteCodeRequired);
            }
            catch (Exception excp)
            {
                logger.Error("Exception CreateProvisioningServicee. " + excp.Message);
                throw;
            }
        }
    }
}