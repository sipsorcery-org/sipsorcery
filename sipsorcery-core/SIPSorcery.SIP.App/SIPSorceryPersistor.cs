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
// Copyright (c) 2009 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD 
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
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    public class SIPSorceryPersistor
    {
        private const string WRITE_CDRS_THREAD_NAME = "sipappsvr-writecdrs";

        private ILog logger = AppState.logger;

        private static readonly string m_sipAccountsXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_SIPACCOUNTS_FILENAME;
        private static readonly string m_sipProvidersXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_SIPPROVIDERS_FILENAME;
        private static readonly string m_sipDialplansXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_DIALPLANS_FILENAME;
        private static readonly string m_sipRegistrarBindingsXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_REGISTRAR_BINDINGS_FILENAME;
        private static readonly string m_sipProviderBindingsXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_PROVIDER_BINDINGS_FILENAME;
        private static readonly string m_sipDialoguesXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_SIPDIALOGUES_FILENAME;
        private static readonly string m_sipCDRsXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_SIPCDRS_FILENAME;

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

        public SIPSorceryPersistor(StorageTypes storageType, string storageConnectionStr)
        {
            if (storageType == StorageTypes.XML)
            {
                if (!storageConnectionStr.Contains(":"))
                {
                    // Relative path.
                    storageConnectionStr = AppDomain.CurrentDomain.BaseDirectory + storageConnectionStr;
                }

                if (!storageConnectionStr.EndsWith(@"\"))
                {
                    storageConnectionStr += @"\";
                }

                if (!Directory.Exists(storageConnectionStr))
                {
                    throw new ApplicationException("Directory " + storageConnectionStr + " does not exist for XML persistor.");
                }
            }

            m_sipAccountsPersistor = SIPAssetPersistorFactory<SIPAccount>.CreateSIPAssetPersistor(storageType, storageConnectionStr, m_sipAccountsXMLFilename);
            m_dialPlanPersistor = SIPAssetPersistorFactory<SIPDialPlan>.CreateSIPAssetPersistor(storageType, storageConnectionStr, m_sipDialplansXMLFilename);
            m_sipProvidersPersistor = SIPAssetPersistorFactory<SIPProvider>.CreateSIPAssetPersistor(storageType, storageConnectionStr, m_sipProvidersXMLFilename);
            m_sipProviderBindingsPersistor = SIPAssetPersistorFactory<SIPProviderBinding>.CreateSIPAssetPersistor(storageType, storageConnectionStr, m_sipProviderBindingsXMLFilename);
            m_sipDomainManager = new SIPDomainManager(storageType, storageConnectionStr);
            m_sipRegistrarBindingPersistor = SIPAssetPersistorFactory<SIPRegistrarBinding>.CreateSIPAssetPersistor(storageType, storageConnectionStr, m_sipRegistrarBindingsXMLFilename);
            m_sipDialoguePersistor = SIPAssetPersistorFactory<SIPDialogueAsset>.CreateSIPAssetPersistor(storageType, storageConnectionStr, m_sipDialoguesXMLFilename);
            m_sipCDRPersistor = SIPAssetPersistorFactory<SIPCDRAsset>.CreateSIPAssetPersistor(storageType, storageConnectionStr, m_sipCDRsXMLFilename);

            //if (m_sipCDRPersistor != null)
            //{
            //    ThreadPool.QueueUserWorkItem(delegate { WriteCDRs(); });
            //}
        }

        public void WriteCDR(SIPCDR cdr)
        {
            try
            {
                //if (m_sipCDRPersistor != null && !StopCDRWrites && !m_pendingCDRs.Contains(cdr))
                //{
                //    m_pendingCDRs.Enqueue(cdr);
                //}

                SIPCDRAsset cdrAsset = new SIPCDRAsset(cdr);

                var existingCDR = m_sipCDRPersistor.Get(cdrAsset.Id);

                if (existingCDR == null)
                {
                    cdrAsset.Inserted = DateTimeOffset.UtcNow;
                    m_sipCDRPersistor.Add(cdrAsset);
                }
                else //if (existingCDR.ReconciliationResult == null)
                {
                    m_sipCDRPersistor.Update(cdrAsset);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception QueueCDR. " + excp.Message);
            }
        }

    //    private void WriteCDRs()
    //    {
    //        try
    //        {
    //            Thread.CurrentThread.Name = WRITE_CDRS_THREAD_NAME;

    //            while (!StopCDRWrites || m_pendingCDRs.Count > 0)
    //            {
    //                try
    //                {
    //                    if (m_pendingCDRs.Count > 0)
    //                    {
    //                        SIPCDRAsset cdrAsset = new SIPCDRAsset(m_pendingCDRs.Dequeue());

    //                        // Check whether the CDR has been hungup already in which case no more updates are permitted.
    //                        var existingCDR = m_sipCDRPersistor.Get(cdrAsset.Id);

    //                        if (existingCDR == null)
    //                        {
    //                            cdrAsset.Inserted = DateTimeOffset.UtcNow;
    //                            m_sipCDRPersistor.Add(cdrAsset);
    //                        }
    //                        else //if (existingCDR.ReconciliationResult == null)
    //                        {
    //                            m_sipCDRPersistor.Update(cdrAsset);
    //                        }
    //                        //else
    //                        //{
    //                        //    logger.Warn("A CDR was not updated as the copy in the database had already been processed by the RTCC engine (" + existingCDR.Id + ").");
    //                        //}
    //                    }
    //                    else
    //                    {
    //                        Thread.Sleep(1000);
    //                    }
    //                }
    //                catch (Exception writeExcp)
    //                {
    //                    logger.Error("Exception WriteCDRs writing CDR. " + writeExcp.Message);
    //                }
    //            }
    //        }
    //        catch (Exception excp)
    //        {
    //            logger.Error("Exception WriteCDRs. " + excp.Message);
    //        }
    //    }
    }
}
