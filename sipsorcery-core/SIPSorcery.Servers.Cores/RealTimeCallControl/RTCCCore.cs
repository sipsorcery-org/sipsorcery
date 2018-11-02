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
using System.Data.Entity.Core;
using System.Data.Entity.Core.Objects;
using System.Linq;
using System.Reflection;
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
        
        private static ILog logger = AppState.GetLogger("rtcc");

        private SIPMonitorLogDelegate Log_External;
        private SIPAssetPersistor<SIPDialogueAsset> m_sipDialoguePersistor;
        private SIPDialogueManager m_sipDialogueManager;
        private int m_reserveDueSeconds = 15;           // A reservation is due when there is only this many seconds remaining on the call.
        private int m_reservationAmountSeconds = 60;    // The amount of seconds to reserve on each reservation request.
        private CustomerDataLayer _customerDataLayer = new CustomerDataLayer();
        private CustomerAccountDataLayer m_customerAccountDataLayer = new CustomerAccountDataLayer();

        private bool m_exit;

        public RTCCCore(
            SIPMonitorLogDelegate logDelegate,
            SIPDialogueManager sipDialogueManager,
            SIPAssetPersistor<SIPDialogueAsset> sipDialoguePersistor)
        {
            Log_External = logDelegate;
            m_sipDialogueManager = sipDialogueManager;
            m_sipDialoguePersistor = sipDialoguePersistor;
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

                logger.Debug("RTCC Core Starting Monitor CDRs thread.");

                while (!m_exit)
                {
                    using (var db = new SIPSorceryEntities())
                    {
                        try
                        {
                            // Try and reserve credit on in progress calls.
                            DateTime reservationDue = DateTime.Now.AddSeconds(m_reserveDueSeconds);

                            var rtccReservationDue = (from rtcc in db.RTCCs1.Include("cdr")
                                                      where rtcc.AccountCode != null && rtcc.SecondsReserved != null && rtcc.SecondsReserved > 0 && rtcc.ReservationError == null && rtcc.ReconciliationResult == null
                                                            && rtcc.cdr.HungupTime == null && rtcc.cdr.AnsweredAt != null && rtcc.cdr.AnsweredStatus >= 200 && rtcc.cdr.AnsweredStatus <= 299 
                                                            && EntityFunctions.AddSeconds(rtcc.cdr.AnsweredAt, rtcc.SecondsReserved) <= reservationDue
                                                      orderby rtcc.cdr.AnsweredAt
                                                      select rtcc).Take(NUMBER_CDRS_PER_ROUNDTRIP);

                            while (rtccReservationDue.Count() > 0)
                            {
                                foreach (RTCC rtcc in rtccReservationDue)
                                {
                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RTCC, SIPMonitorEventTypesEnum.DialPlan, "Reserving credit for call " + rtcc.cdr.Dst + ".", rtcc.cdr.Owner));

                                    // Attempt to re-reserve the next chunk of credit for the call.
                                    m_customerAccountDataLayer.ReserveCredit(m_reservationAmountSeconds, rtcc.ID);
                                }
                            }
                        }
                        //catch (ReflectionTypeLoadException ex)
                        //{
                        //    StringBuilder sb = new StringBuilder();
                        //    foreach (Exception exSub in ex.LoaderExceptions)
                        //    {
                        //        sb.AppendLine(exSub.Message);
                        //        if (exSub is FileNotFoundException)
                        //        {
                        //            FileNotFoundException exFileNotFound = exSub as FileNotFoundException;
                        //            if (!string.IsNullOrEmpty(exFileNotFound.FusionLog))
                        //            {
                        //                sb.AppendLine("Fusion Log:");
                        //                sb.AppendLine(exFileNotFound.FusionLog);
                        //            }
                        //        }
                        //        sb.AppendLine();
                        //    }
                        //    string errorMessage = sb.ToString();
                        //    logger.Error(errorMessage);
                        //}
                        catch (Exception monitorExcp)
                        {
                            logger.Error("Exception MonitorCDRs Credit Reservation. " + monitorExcp);
                            logger.Error("InnerException MonitorCDRs Credit Reservation. " + monitorExcp.InnerException);
                        }

                        try
                        {
                            // Terminate any calls that have reached their time limit.
                            DateTime now = DateTime.Now;
                            var rtccTerminationDue = (from rtcc in db.RTCCs1.Include("cdr")
                                                      where !rtcc.IsHangingUp && rtcc.AccountCode != null && rtcc.cdr.HungupTime == null && rtcc.cdr.AnsweredAt != null && rtcc.SecondsReserved != null && 
                                                              rtcc.cdr.AnsweredStatus >= 200 && rtcc.cdr.AnsweredStatus <= 299 &&  EntityFunctions.AddSeconds(rtcc.cdr.AnsweredAt, rtcc.SecondsReserved) <= now && !rtcc.IsHangingUp
                                                              && rtcc.ReconciliationResult == null
                                                      orderby rtcc.cdr.AnsweredAt
                                                      select rtcc).Take(NUMBER_CDRS_PER_ROUNDTRIP);

                            while (rtccTerminationDue.Count() > 0)
                            {
                                foreach (RTCC rtcc in rtccTerminationDue)
                                {
                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RTCC, SIPMonitorEventTypesEnum.DialPlan, "Terminating call due to reservation limit being reached " + rtcc.cdr.Dst + ".", rtcc.cdr.Owner));

                                    m_customerAccountDataLayer.SetCDRIsHangingUp(rtcc.ID);

                                    var dialogue = m_sipDialoguePersistor.Get(x => x.CDRId == rtcc.CDRID, null, 0, 1).FirstOrDefault();
                                    if (dialogue != null)
                                    {
                                        m_sipDialogueManager.CallHungup(dialogue.SIPDialogue, "RTCC time limit reached", true);
                                    }
                                    else
                                    {
                                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RTCC, SIPMonitorEventTypesEnum.Warn, "A dialogue could not be found when terminating a call due to reservation limit being reached.", rtcc.cdr.Owner));
                                    }
                                }
                            }
                        }
                        catch (Exception monitorExcp)
                        {
                            logger.Error("Exception RTCCCore MonitorCDRs Call Termination. " + monitorExcp.Message);
                        }
                    }

                    Thread.Sleep(1000);
                }

                logger.Warn("RTCCCore MonitorCDRs thread stopping.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTCCCore MonitorCDRs. " + excp.Message);
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

                logger.Debug("RTCC Core Starting Reconcile CDRs thread.");

                while (!m_exit)
                {
                    try
                    {
                        using (var db = new SIPSorceryEntities())
                        {
                            var rtccReconciliationDue = (from rtcc in db.RTCCs1.Include("cdr")
                                                         where rtcc.AccountCode != null && ((rtcc.cdr.AnsweredStatus > 0 && rtcc.cdr.AnsweredStatus < 200) || rtcc.cdr.AnsweredStatus >= 300 || rtcc.cdr.HungupTime != null || rtcc.cdr.HungupReason != null) 
                                                            && rtcc.ReconciliationResult == null && rtcc.PostReconciliationBalance == null && rtcc.Cost > 0
                                                         orderby rtcc.cdr.HungupTime
                                                         select rtcc).Take(NUMBER_CDRS_PER_ROUNDTRIP);

                            while (rtccReconciliationDue.Count() > 0)
                            {
                                foreach (RTCC rtcc in rtccReconciliationDue)
                                {
                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RTCC, SIPMonitorEventTypesEnum.DialPlan, "Reconciling credit for call " + rtcc.cdr.Dst + ".", rtcc.cdr.Owner));

                                    logger.Debug("Answered Status=" + rtcc.cdr.AnsweredStatus + ", hungup time=" + rtcc.cdr.HungupTime + ", hungup reason=" + rtcc.cdr.HungupReason + ".");

                                    logger.Debug("Reconciliation starting for CDR " + rtcc.cdr.ID + ", owner " + rtcc.cdr.Owner + ", destination " + rtcc.cdr.Dst + ", duration " + rtcc.cdr.Duration + ", rate " + rtcc.Rate + ", setup cost " + 
                                        rtcc.SetupCost +", increment seconds " + rtcc.IncrementSeconds + " and reserved credit of " + rtcc.Cost + ".");
                                    
                                    decimal callCost = m_customerAccountDataLayer.ReturnUnusedCredit(rtcc.ID);

                                    if (callCost > Decimal.Zero)
                                    {
                                        // Check whether a reconciliation callback needs to be made for this customer account.
                                        var customer = _customerDataLayer.GetForName(rtcc.cdr.Owner);

                                        if(customer.RTCCReconciliationURL.NotNullOrBlank())
                                        {
                                            // Send an async HTTP GET request to the specified URL.
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception monitorExcp)
                    {
                        logger.Error("Exception ReconcileCDRs Monitoring. " + monitorExcp);
                    }

                    Thread.Sleep(1000);
                }

                logger.Warn("RTCCCore ReconcileCDRs thread stopping.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTCCCore ReconcileCDRs. " + excp.Message);
                logger.Error("InnerException RTCCCore ReconcileCDRs. " + excp.InnerException);
            }
        }
    }
}
