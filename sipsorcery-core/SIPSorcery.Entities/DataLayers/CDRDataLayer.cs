//-----------------------------------------------------------------------------
// Filename: CDRDataLayer.cs
//
// Description: Data layer class for CDR entities.
// 
// History:
// 03 Aug 2013	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2012 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd. 
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
using System.Linq;
using System.Text;
using System.Transactions;
using SIPSorcery.Entities;
using SIPSorcery.SIP;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Entities
{
    public class CDRDataLayer
    {
        private static ILog logger = AppState.logger;

        /// <summary>
        /// Adds a new CDR.
        /// </summary>
        /// <param name="rate">The rate record to add.</param>
        public void Add(SIPCDR cdr)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                var entityCDR = new CDR()
                {
                    ID = cdr.CDRId.ToString(),
                    Inserted = DateTimeOffset.UtcNow.ToString("o"),
                    Created = cdr.Created.ToString("o"),
                    Direction = cdr.CallDirection.ToString(),
                    DstURI = cdr.Destination.ToString(),
                    Dst = cdr.Destination.User,
                    DstHost = cdr.Destination.Host,
                    FromHeader = cdr.From.ToString(),
                    FromName = cdr.From.FromName,
                    FromUser = cdr.From.FromURI.User,
                    CallID = cdr.CallId,
                    Owner = cdr.Owner,
                    AdminMemberID = cdr.AdminMemberId,
                    RemoteSocket = (cdr.RemoteEndPoint != null) ? cdr.RemoteEndPoint.ToString() : null,
                    LocalSocket = (cdr.LocalSIPEndPoint != null) ? cdr.LocalSIPEndPoint.ToString() : null
                };

                sipSorceryEntities.CDRs.Add(entityCDR);
                sipSorceryEntities.SaveChanges();
            }
        }

        /// <summary>
        /// Updates an existing CDR.
        /// </summary>
        /// <param name="cdr">The CDR to update.</param>
        public void Update(SIPCDR cdr)
        {
            using (var db = new SIPSorceryEntities())
            {
                string cdrID = cdr.CDRId.ToString();
                var existingCDR = (from cd in db.CDRs where cd.ID == cdrID select cd).SingleOrDefault();

                if (existingCDR == null)
                {
                    throw new ApplicationException("The CDR to update could not be found");
                }
                else
                {
                    // Fields that are not permitted to be updated.
                    // ID
                    // Inserted
                    // Direction
                    // Created
                    // Destination
                    // From
                    // Call-ID

                    existingCDR.Owner = cdr.Owner;
                    existingCDR.AdminMemberID = cdr.AdminMemberId;
                    existingCDR.BridgeID = (cdr.BridgeId != Guid.Empty) ? cdr.BridgeId.ToString() : null;
                    existingCDR.InProgressTime = (cdr.ProgressTime != null) ? cdr.ProgressTime.Value.ToString("o") : null;
                    existingCDR.InProgressStatus = cdr.ProgressStatus;
                    existingCDR.InProgressReason = cdr.ProgressReasonPhrase;
                    existingCDR.RingDuration = (cdr.ProgressTime != null && cdr.AnswerTime != null) ? Convert.ToInt32(cdr.AnswerTime.Value.Subtract(cdr.ProgressTime.Value).TotalSeconds) : 0;
                    existingCDR.AnsweredTime = (cdr.AnswerTime != null) ? cdr.AnswerTime.Value.ToString("o") : null;
                    existingCDR.AnsweredStatus = cdr.AnswerStatus;
                    existingCDR.AnsweredReason = cdr.AnswerReasonPhrase;
                    existingCDR.Duration = (cdr.AnswerTime != null && cdr.HangupTime != null) ? Convert.ToInt32(cdr.HangupTime.Value.Subtract(cdr.AnswerTime.Value).TotalSeconds) : 0; ;
                    existingCDR.HungupTime = (cdr.HangupTime != null) ? cdr.HangupTime.Value.ToString("o") : null;
                    existingCDR.HungupReason = cdr.HangupReason;
                    existingCDR.AnsweredAt = cdr.AnsweredAt;
                    existingCDR.DialPlanContextID = (cdr.DialPlanContextID != Guid.Empty) ? cdr.DialPlanContextID.ToString() : null;
                    existingCDR.RemoteSocket = (cdr.RemoteEndPoint != null) ? cdr.RemoteEndPoint.ToString() : null;
                    existingCDR.LocalSocket = (cdr.LocalSIPEndPoint != null) ? cdr.LocalSIPEndPoint.ToString() : null;

                    db.SaveChanges();
                }
            }
        }

        public Rate Get(string owner, string id)
        {
            if (id.IsNullOrBlank())
            {
                return null;
            }

            using (var db = new SIPSorceryEntities())
            {
                return (from ra in db.Rates
                        where
                            ra.Owner == owner.ToLower() &&
                            ra.ID.ToLower() == id.ToLower()
                        select ra).SingleOrDefault();
            }
        }
    }
}
