// ============================================================================
// FileName: SIPSorceryPersistor.cs
//
// Description:
// Handles persistence for the SIP Sorcery Application Server.
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

namespace SIPSorcery.SIPAppServer {

    public class SIPSorceryPersistor {

        public const string XML_DOMAINS_FILENAME = "sipdomains.xml";
        public const string XML_SIPACCOUNTS_FILENAME = "sipaccounts.xml";
        public const string XML_SIPPROVIDERS_FILENAME = "sipproviders.xml";
        public const string XML_DIALPLANS_FILENAME = "sipdialplans.xml";
        public const string XML_REGISTRAR_BINDINGS_FILENAME = "sipregistrarbindings.xml";
        public const string XML_PROVIDER_BINDINGS_FILENAME = "sipproviderbindings.xml";
        public const string XML_SIPDIALOGUES_FILENAME = "sipdialogues.xml";
        public const string XML_SIPCDRS_FILENAME = "sipcdrs.xml";

        private const string WRITE_CDRS_THREAD_NAME = "sipappsvr-writecdrs";

        private ILog logger = AppState.logger;

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

        public bool StopCDRWrites;
        private Queue<SIPCDR> m_pendingCDRs = new Queue<SIPCDR>();

        public SIPSorceryPersistor(StorageTypes storageType, string storageConnectionStr) {

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

            if (m_sipCDRPersistor != null) {
                ThreadPool.QueueUserWorkItem(delegate { WriteCDRs(); });
            }
        }

        public void QueueCDR(SIPCDR cdr) {
            try {
                if (m_sipCDRPersistor != null && !StopCDRWrites && !m_pendingCDRs.Contains(cdr)) {
                    m_pendingCDRs.Enqueue(cdr);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception QueueCDR. " + excp.Message);
            }
        }

        private void WriteCDRs() {
            try {
                Thread.CurrentThread.Name = WRITE_CDRS_THREAD_NAME;

                while (!StopCDRWrites || m_pendingCDRs.Count > 0) {
                    try {
                        if (m_pendingCDRs.Count > 0) {

                            SIPCDRAsset cdrAsset = new SIPCDRAsset(m_pendingCDRs.Dequeue());
                            if (m_sipCDRPersistor.Count(c => c.Id == cdrAsset.Id) == 1) {
                                m_sipCDRPersistor.Update(cdrAsset);
                            }
                            else {
                                m_sipCDRPersistor.Add(cdrAsset);
                            }
                        }
                        else {
                            Thread.Sleep(1000);
                        }
                    }
                    catch (Exception writeExcp) {
                        logger.Error("Exception WriteCDRs writing CDR. " + writeExcp.Message);
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception WriteCDRs. " + excp.Message);
            }
        }
    }
}
