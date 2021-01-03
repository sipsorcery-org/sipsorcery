//-----------------------------------------------------------------------------
// Filename: CDRDataLayer.cs
//
// Description: Data access methods for the Call Detail Records (CDR) entity. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 01 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;

namespace demo.DataAccess
{
    public class CDRDataLayer
    {
        private readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<CDRDataLayer>();

        public CDR Get(Guid id)
        {
            using (var db = new SIPAssetsDbContext())
            {
                return db.CDRs.Where(x => x.ID == id).FirstOrDefault();
            }
        }

        public void Add(SIPCDR sipCDR)
        {
            CDR cdr = new CDR(sipCDR);

            using (var db = new SIPAssetsDbContext())
            {
               cdr.Inserted = DateTime.UtcNow;

                db.CDRs.Add(cdr);
                db.SaveChanges();
            }
        }


        /// <summary>
        /// Updates an existing CDR.
        /// </summary>
        /// <param name="cdr">The CDR to update.</param>
        public void Update(SIPCDR sipCDR)
        {
            using (var db = new SIPAssetsDbContext())
            {
                var existing = (from cdr in db.CDRs where cdr.ID == sipCDR.CDRId select cdr).SingleOrDefault();

                if (existing == null)
                {
                    logger.LogWarning($"CDRDataLayer the CDR with ID {sipCDR.CDRId} could not be found for an Update operation.");
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

                    existing.BridgeID = (sipCDR.BridgeId != Guid.Empty) ? sipCDR.BridgeId : null;
                    existing.InProgressAt = sipCDR.ProgressTime;
                    existing.InProgressStatus = sipCDR.ProgressStatus;
                    existing.InProgressReason = sipCDR.ProgressReasonPhrase;
                    existing.RingDuration = sipCDR.GetProgressDuration();
                    existing.AnsweredAt = sipCDR.AnswerTime;
                    existing.AnsweredStatus = sipCDR.AnswerStatus;
                    existing.AnsweredReason = sipCDR.AnswerReasonPhrase;
                    existing.Duration = sipCDR.GetAnsweredDuration();
                    existing.HungupAt = sipCDR.HangupTime;
                    existing.HungupReason = sipCDR.HangupReason;
                    existing.AnsweredAt = sipCDR.AnsweredAt;
                    existing.RemoteSocket = sipCDR.RemoteEndPoint?.ToString();
                    existing.LocalSocket = sipCDR.LocalSIPEndPoint?.ToString();

                    db.SaveChanges();
                }
            }
        }

        public void Hangup(Guid id, string reason)
        {
            using (var db = new SIPAssetsDbContext())
            {
                var existing = db.CDRs.Where(x => x.ID == id).SingleOrDefault();

                if (existing == null)
                {
                    logger.LogWarning($"CDRDataLayer the CDR with ID {id} could not be found for a Hangup operation.");
                }
                else
                {
                    existing.HungupAt = DateTime.UtcNow;
                    existing.HungupReason = reason;
                    existing.Duration = Convert.ToInt32(existing.HungupAt.Value.Subtract(existing.AnsweredAt.Value).TotalSeconds);

                    db.SaveChanges();
                }
            }
        }

        public void UpdateBridgeID(Guid id, Guid bridgeID)
        {
            using (var db = new SIPAssetsDbContext())
            {
                var existing = db.CDRs.Where(x => x.ID == id).SingleOrDefault();

                if (existing == null)
                {
                    logger.LogWarning($"CDRDataLayer the CDR with ID {id} could not be found for a Update BridgeID operation.");
                }
                else
                {
                    existing.BridgeID = bridgeID;
                    db.SaveChanges();
                }
            }
        }
    }
}
