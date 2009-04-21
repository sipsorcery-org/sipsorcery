// ============================================================================
// FileName: SIPSwitchXMLPersistor.cs
//
// Description:
// Retrieves and persists sipswitch objects from XML files.
//
// Author(s):
// Aaron Clauson
//
// History:
// 14 Sep 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.Servers;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIPAppServer
{
    public class SIPSwitchPersistor
    {
        public const string XML_DOMAINS_FILENAME = "sipdomains.xml";
        public const string XML_SIPACCOUNTS_FILENAME = "sipaccounts.xml";
        public const string XML_SIPPROVIDERS_FILENAME = "sipproviders.xml";
        public const string XML_DIALPLANS_FILENAME = "sipdialplans.xml";
        public const string XML_REGISTRAR_BINDINGS_FILENAME = "sipregistrarbindings.xml";
        public const string XML_PROVIDER_BINDINGS_FILENAME = "sipproviderbindings.xml";
        public const string XML_SIPDIALOGUES_FILENAME = "sipdialogues.xml";
        public const string XML_SIPCDRS_FILENAME = "sipcdrs.xml";
        private const int RELOAD_SPACING_SECONDS = 3;                           // Minimum interval the XML file change events will be allowed.

        private ILog logger = SIPAppServerState.logger;

        private StorageTypes m_persistorType;
        private StorageLayer m_sqlStorageLayer;    // Only set if an SQL persistence type is in use.
        
        private SIPAssetPersistor<SIPAccount> m_sipAccountsPersistor;
        public SIPAssetPersistor<SIPAccount> SIPAccountsPersistor { get { return m_sipAccountsPersistor; } }

        private SIPAssetPersistor<SIPDialPlan> m_dialPlanPersistor;
        public SIPAssetPersistor<SIPDialPlan> SIPDialPlanPersistor { get { return m_dialPlanPersistor; } } 
        
        private SIPAssetPersistor<SIPProvider> m_sipProvidersPersistor;
        public SIPAssetPersistor<SIPProvider> SIPProvidersPersistor { get { return m_sipProvidersPersistor; } }

        private SIPAssetPersistor<SIPProviderBinding> m_sipProviderBindingsPersistor;
        public SIPAssetPersistor<SIPProviderBinding> SIPProviderBindingsPersistor { get { return m_sipProviderBindingsPersistor; } }

        private SIPDomainManager m_sipDomainManager;
        public SIPDomainManager SIPDomainManager { get { return m_sipDomainManager; } }

        private SIPAssetPersistor<SIPRegistrarBinding> m_sipRegistrarBindingPersistor;
        public SIPAssetPersistor<SIPRegistrarBinding> SIPRegistrarBindingPersistor { get { return m_sipRegistrarBindingPersistor; } }

        private SIPAssetPersistor<SIPDialogueAsset> m_sipDialoguePersistor;
        public SIPAssetPersistor<SIPDialogueAsset> SIPDialoguePersistor { get { return m_sipDialoguePersistor; } }

        private SIPAssetPersistor<SIPCDRAsset> m_sipCDRPersistor;
        public SIPAssetPersistor<SIPCDRAsset> SIPCDRPersistor { get { return m_sipCDRPersistor; } }

        public event SIPAssetDelegate<SIPProvider> SIPProviderAdded;
        public event SIPAssetDelegate<SIPProvider> SIPProviderUpdated;
        public event SIPAssetDelegate<SIPProvider> SIPProviderDeleted;

        public SIPSwitchPersistor(StorageTypes storageType, string storageConnectionStr) {
            m_persistorType = storageType;

            if (storageType == StorageTypes.XML) {

                if (!Directory.Exists(storageConnectionStr)) {
                    throw new ApplicationException("Directory " + storageConnectionStr + " does not exist for XML persistor.");
                }

                m_sipAccountsPersistor = SIPAssetPersistorFactory.CreateSIPAccountPersistor(StorageTypes.XML, storageConnectionStr + XML_SIPACCOUNTS_FILENAME);
                m_dialPlanPersistor = SIPAssetPersistorFactory.CreateDialPlanPersistor(StorageTypes.XML, storageConnectionStr + XML_DIALPLANS_FILENAME);
                m_sipProvidersPersistor = SIPAssetPersistorFactory.CreateSIPProviderPersistor(StorageTypes.XML, storageConnectionStr + XML_SIPPROVIDERS_FILENAME);
                m_sipProviderBindingsPersistor = SIPAssetPersistorFactory.CreateSIPProviderBindingPersistor(StorageTypes.XML, storageConnectionStr + XML_PROVIDER_BINDINGS_FILENAME);
                m_sipDomainManager = new SIPDomainManager(StorageTypes.XML, storageConnectionStr + XML_DOMAINS_FILENAME);
                m_sipRegistrarBindingPersistor = SIPAssetPersistorFactory.CreateSIPRegistrarBindingPersistor(StorageTypes.XML, storageConnectionStr + XML_REGISTRAR_BINDINGS_FILENAME);
                m_sipDialoguePersistor = SIPAssetPersistorFactory.CreateSIPDialoguePersistor(StorageTypes.XML, storageConnectionStr + XML_SIPDIALOGUES_FILENAME);
                m_sipCDRPersistor = SIPAssetPersistorFactory.CreateSIPCDRPersistor(StorageTypes.XML, storageConnectionStr + XML_SIPCDRS_FILENAME);
            }
            else if (storageType == StorageTypes.DBLinqMySQL || storageType == StorageTypes.DBLinqPostgresql) {
                m_sipAccountsPersistor = SIPAssetPersistorFactory.CreateSIPAccountPersistor(storageType, storageConnectionStr);
                m_dialPlanPersistor = SIPAssetPersistorFactory.CreateDialPlanPersistor(storageType, storageConnectionStr);
                m_sipProvidersPersistor = SIPAssetPersistorFactory.CreateSIPProviderPersistor(storageType, storageConnectionStr);
                m_sipProviderBindingsPersistor = SIPAssetPersistorFactory.CreateSIPProviderBindingPersistor(storageType, storageConnectionStr);
                m_sipDomainManager = new SIPDomainManager(storageType, storageConnectionStr);
                m_sipRegistrarBindingPersistor = SIPAssetPersistorFactory.CreateSIPRegistrarBindingPersistor(storageType, storageConnectionStr);
                m_sipDialoguePersistor = SIPAssetPersistorFactory.CreateSIPDialoguePersistor(storageType, storageConnectionStr);
                m_sipCDRPersistor = SIPAssetPersistorFactory.CreateSIPCDRPersistor(storageType, storageConnectionStr);
            }
            else {
                throw new NotImplementedException(storageType + " is not implemented for the Application Server persistor.");
            }

            m_sipProvidersPersistor.Added += new SIPAssetDelegate<SIPProvider>(sipProvider => { if (SIPProviderAdded != null) SIPProviderAdded(sipProvider); });
            m_sipProvidersPersistor.Updated += new SIPAssetDelegate<SIPProvider>(sipProvider => { if (SIPProviderUpdated != null) SIPProviderUpdated(sipProvider); });
            m_sipProvidersPersistor.Deleted += new SIPAssetDelegate<SIPProvider>(sipProvider => { if (SIPProviderDeleted != null) SIPProviderDeleted(sipProvider); });
        }

        public bool DoesSIPAccountExist(string user, string canonicalDomain)
        {
            return (m_sipAccountsPersistor.Get(s => s.SIPUsername == user && s.SIPDomain == canonicalDomain) != null);
        }

        public List<SIPRegistrarBinding> GetSIPAccountBindings(string user, string canonicalDomain) {
            SIPAccount sipAccount = m_sipAccountsPersistor.Get(s => s.SIPUsername == user && s.SIPDomain == canonicalDomain);
            if (sipAccount != null) {
                return m_sipRegistrarBindingPersistor.Get(b => b.SIPAccountId == sipAccount.Id, 0, Int32.MaxValue);
            }
            else {
                return null;
            }
        }

        public bool SIPMonitorAuthenticate(string username, string password)
        {
            if (m_persistorType == StorageTypes.XML)
            {
                return true;
            }
            else
            {
                string usernameStr = Regex.Replace(username, "'", "''").ToUpper();
                string passwordStr = (password == null) ? null : Regex.Replace(password, "'", "''");

                string selectSQL = "select count(*) from customers where upper(username) = '" + usernameStr + "' and password = '" + passwordStr + "'";

                int count = Convert.ToInt32(m_sqlStorageLayer.ExecuteScalar(selectSQL));

                if (count == 1)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public bool HasDialPlanBeenUpdated(string owner, string dialPlanName, DateTime lastUpdateTime)
        {
            if (m_persistorType == StorageTypes.XML)
            {
                //DialPlan dialPlan = m_dialPlanPersistor.Get(owner, dialPlanName);
                SIPDialPlan dialPlan = m_dialPlanPersistor.Get(d => d.Owner == owner && d.DialPlanName == dialPlanName);

                if (dialPlan != null && dialPlan.LastUpdate <= lastUpdateTime)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                bool dialPlanUpdated = false;

                object updatedObj = m_sqlStorageLayer.ExecuteScalar("select updated from dialplans where owner = '" + owner + "'");

                if (updatedObj != null && updatedObj != DBNull.Value)
                {
                    dialPlanUpdated = m_sqlStorageLayer.ConvertToBool(updatedObj);
                }

                return dialPlanUpdated;
            }
        }

        public SIPDialPlan LoadDialPlan(string owner, string dialPlanName) {
            if (owner.IsNullOrBlank() || dialPlanName.IsNullOrBlank()) {
                return null;
            }

            SIPDialPlan dialPlan = m_dialPlanPersistor.Get(d => d.Owner == owner && d.DialPlanName == dialPlanName.Trim());

            if (dialPlan == null) {
                logger.Warn("SIPSwitchPersistor could not locate a dialplan for owner=" + owner + " and dialplan name=" + dialPlanName.Trim() + ".");
            }

            return dialPlan;
        }

        public SIPAccount AddSIPAccount(SIPAccount sipAccount)
        {
            return m_sipAccountsPersistor.Add(sipAccount);
        }

        public SIPAccount GetSIPAccount(string username, string domain)
        {
            return m_sipAccountsPersistor.Get(s => s.SIPUsername == username && s.SIPDomain == domain);
        }

        public SIPAccount UpdateSIPAccount(SIPAccount sipAccount)
        {
            return m_sipAccountsPersistor.Update(sipAccount);
        }

        public void DeleteSIPAccount(SIPAccount sipAccount)
        {
            m_sipAccountsPersistor.Delete(sipAccount);
        }

        public List<SIPProvider> GetSIPProvidersForUser(string owner)
        {
            return m_sipProvidersPersistor.Get(p => p.Owner == owner, 0, Int32.MaxValue);
        }

        public SIPProvider AddSIPProvider(SIPProvider sipProvider) {
            sipProvider = m_sipProvidersPersistor.Add(sipProvider);

            if (SIPProviderAdded != null) {
                SIPProviderAdded(sipProvider);
            }

            return m_sipProvidersPersistor.Add(sipProvider);
        }

        public SIPProvider UpdateSIPProvider(SIPProvider sipProvider) {
            sipProvider = m_sipProvidersPersistor.Update(sipProvider);

            if (SIPProviderUpdated != null) {
                SIPProviderUpdated(sipProvider);
            }

            return sipProvider;
        }

        public void DeleteSIPProvider(SIPProvider sipProvider) {
            m_sipProvidersPersistor.Delete(sipProvider);

            if (SIPProviderDeleted != null) {
                SIPProviderDeleted(sipProvider);
            }
        }

        /// <summary>
        /// Called when an external process wishes to let the hosting agent know a dialplan has been updated and that
        /// the agent should reload the specified dialplan before its next use. 
        /// Typically a call to this method will be as a result of a dialplan update being carried out by a web server 
        /// and then wanting to let the SIP Server Agent know. The method is redundant when an XML persistence layer 
        /// and single server deployment model is in use since the Agent will already know about the update.
        /// </summary>
        /// <param name="owner">The owner of the dialplan that has been updated.</param>
        /// <param name="dialplanName">The name assigned to the dialplan by the owner.</param>
        public void DialPlanExternalUpdate(string owner, string dialplanName)
        {

        }

        /// <summary>
        /// Called when an external process wishes to let the hosting agent know a SIPProvider has been deleted. 
        /// Typically a call to this method will be the result of a dialplan update being carried out by a web server 
        /// and then wanting to let the SIP Server Agent know. The method is redundant when an XML persistence layer 
        /// and single server deployment model is in use since the Agent will already know about the update.
        /// </summary>
        /// <param name="sipProviderId">The id of the SIPProvider that has been deleted.</param>
        public void SIPProviderExternalDeleted(Guid sipProviderId)
        {
             SIPProviderDeleted(m_sipProvidersPersistor.Get(sipProviderId));
        }

        /// <summary>
        /// Called when an external process wishes to let the hosting agent know a SIPProvider has been addeded. 
        /// Typically a call to this method will be the result of a dialplan update being carried out by a web server 
        /// and then wanting to let the SIP Server Agent know. The method is redundant when an XML persistence layer 
        /// and single server deployment model is in use since the Agent will already know about the update.
        /// </summary>
        /// <param name="sipProviderId">The id of the SIPProvider that has been added.</param>
        public void SIPProviderExternalAdded(Guid sipProviderId)
        {
            SIPProviderAdded(m_sipProvidersPersistor.Get(sipProviderId));
        }

        /// <summary>
        /// Called when an external process wishes to let the hosting agent know a SIPProvider has been updated. 
        /// Typically a call to this method will be the result of a dialplan update being carried out by a web server 
        /// and then wanting to let the SIP Server Agent know. The method is redundant when an XML persistence layer 
        /// and single server deployment model is in use since the Agent will already know about the update.
        /// </summary>
        /// <param name="sipProviderId">The id of the SIPProvider that has been updated.</param>
        public void SIPProviderExternalUpdated(Guid sipProviderId)
        {
            SIPProviderUpdated(m_sipProvidersPersistor.Get(sipProviderId));
        }
    }
}
