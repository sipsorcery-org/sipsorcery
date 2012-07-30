// ============================================================================
// FileName: RTCCCore.cs
//
// Description:
// Real Time Call Control server to manage credit reservation and reconciliation for calls that have been
// designated as requiring credit management.
//
// Author(s):
// Aaron Clauson
//
// History:
// 26 Jun 2012	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2012 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery Pty Ltd 
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
using System.Linq;
using System.Text;
using System.Threading;
using System.Transactions;
using SIPSorcery.Entities;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers
{
    public class RTCCCore
    {
        private const string RTCC_THREAD_NAME = "rtcc-core";
        private const int NUMBER_CDRS_PER_ROUNDTRIP = 5;

        private static ILog logger = AppState.logger;

        private SIPMonitorLogDelegate Log_External;
        SIPAssetPersistor<SIPDialogueAsset> m_sipDialoguePersistor;
        private SIPDialogueManager m_sipDialogueManager;
        private int m_reserveDueSeconds = 15;           // A reservation is due when there is only this many seconds remaining on the call.
        private int m_reservationAmountSeconds = 60;    // The amount of seconds to reserve on each reservation request.
        private CustomerAccountDataLayer m_customerAccountDataLayer = new CustomerAccountDataLayer();

        private bool m_exit;

        public RTCCCore(
            SIPMonitorLogDelegate logDelegate,
            SIPDialogueManager sipDialogueManager,
           int reserveDueSeconds)
        {
            Log_External = logDelegate;
            m_sipDialogueManager = sipDialogueManager;
        }

        public void Start()
        {
            ThreadPool.QueueUserWorkItem(delegate { MonitorCDRs(); });
            ThreadPool.QueueUserWorkItem(delegate { ReconcileCDRs(); });
        }

        public void Stop()
        {
            m_exit = true;
        }

        /// <summary>
        /// Monitors the CDRs table for records that are using real-time call control and are within the limit that requires them to 
        /// re-reserve credit.
        /// </summary>
        private void MonitorCDRs()
        {
            try
            {
                Thread.CurrentThread.Name = RTCC_THREAD_NAME;

                while (!m_exit)
                {
                    try
                    {
                        using (var db = new SIPSorceryEntities())
                        {
                            DateTimeOffset reservationDue = DateTimeOffset.UtcNow.AddSeconds(-1 * m_reserveDueSeconds); 

                            var cdrsReservationDue = (from cdr in db.CDRs
                                                      where cdr.AccountCode != null && cdr.HungupTime == null && cdr.AnsweredDate != null &&
                                                            cdr.AnsweredDate.Value >= reservationDue
                                                      orderby cdr.AnsweredDate
                                                      select cdr).Take(NUMBER_CDRS_PER_ROUNDTRIP);

                            while (cdrsReservationDue.Count() > 0)
                            {
                                foreach (CDR cdr in cdrsReservationDue)
                                {
                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RTCC, SIPMonitorEventTypesEnum.DialPlan, "Reserving credit for call " + cdr.Dst + ".", cdr.Owner));

                                    // Attempt to re-reserve the next chunk of credit for the call.
                                    if (!m_customerAccountDataLayer.ReserveCredit(m_reservationAmountSeconds, cdr.ID))
                                    {
                                        // If the credit reservation fails then hangup the call.
                                        var dialogue = m_sipDialoguePersistor.Get(x => x.CDRId == cdr.ID, null, 0, 1).FirstOrDefault();
                                        if (dialogue != null)
                                        {
                                            m_sipDialogueManager.CallHungup(dialogue.SIPDialogue, "No credit", true);
                                        }
                                        else
                                        {
                                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RTCC, SIPMonitorEventTypesEnum.Warn, "A dialogue could not be found when terminating a call due to no credit.", cdr.Owner));
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception monitorExcp)
                    {
                        logger.Error("Exception MonitorCDRs Monitoring. " + monitorExcp.Message);
                    }

                    Thread.Sleep(1000);
                }

                logger.Warn("SIPCallManger MonitorCDRs thread stopping.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception MonitorCDRs. " + excp.Message);
            }
        }

        /// <summary>
        /// Monitors for CDR's that utilised real-time call control and that have now completed and require credit reconciliation.
        /// </summary>
        private void ReconcileCDRs()
        {
            try
            {
                Thread.CurrentThread.Name = RTCC_THREAD_NAME;

                while (!m_exit)
                {
                    try
                    {
                        using (var db = new SIPSorceryEntities())
                        {
                            var cdrsReconciliationDue = (from cdr in db.CDRs
                                                      where cdr.AccountCode != null && cdr.HungupTime != null
                                                      orderby cdr.HungupTime
                                                      select cdr).Take(NUMBER_CDRS_PER_ROUNDTRIP);

                            while (cdrsReconciliationDue.Count() > 0)
                            {
                                foreach (CDR cdr in cdrsReconciliationDue)
                                {
                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RTCC, SIPMonitorEventTypesEnum.DialPlan, "Reconciling credit for call " + cdr.Dst + ".", cdr.Owner));
                                    m_customerAccountDataLayer.ReturnUnusedCredit(cdr.ID);
                                }
                            }
                        }
                    }
                    catch (Exception monitorExcp)
                    {
                        logger.Error("Exception ReconcileCDRs Monitoring. " + monitorExcp.Message);
                    }

                    Thread.Sleep(1000);
                }

                logger.Warn("SIPCallManger ReconcileCDRs thread stopping.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception ReconcileCDRs. " + excp.Message);
            }
        }
    }
}
