// ============================================================================
// FileName: CDR.Partial.cs
//
// Description:
// Represents the Call Detail Record (CDR) entity. This partial class is used 
// to apply additional properties or metadata to the audo generated CDR class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 02 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using SIPSorcery.SIP;

#nullable disable

namespace demo.DataAccess
{
    public partial class CDR
    {
        /// <summary>
        /// This constructor translates the SIP layer SIPCDR to a data access
        /// layer entity.
        /// </summary>
        /// <param name="sipCDR">The SIP layer CDR to translate.</param>
        public CDR(SIPCDR sipCDR)
        {
            ID = sipCDR.CDRId;
            Direction = sipCDR.CallDirection.ToString();
            created = sipCDR.Created;
            DstUser = sipCDR.Destination.User;
            DstHost = sipCDR.Destination.Host;
            DstUri = sipCDR.Destination.ToString();
            FromUser = sipCDR.From.FromURI.User;
            FromName = sipCDR.From.FromName;
            FromHeader = sipCDR.From.ToString();
            CallID = sipCDR.CallId;
            LocalSocket = sipCDR.LocalSIPEndPoint?.ToString();
            RemoteSocket = sipCDR.RemoteEndPoint?.ToString();
            BridgeID = (sipCDR.BridgeId != Guid.Empty) ? sipCDR.BridgeId : null;
            InProgressAt = sipCDR.ProgressTime;
            InProgressStatus = sipCDR.ProgressStatus;
            InProgressReason = sipCDR.ProgressReasonPhrase;
            RingDuration = sipCDR.GetProgressDuration();
            AnsweredAt = sipCDR.AnswerTime;
            AnsweredStatus = sipCDR.AnswerStatus;
            AnsweredReason = sipCDR.AnswerReasonPhrase;
            Duration = sipCDR.GetAnsweredDuration();
            HungupAt = sipCDR.HangupTime;
            HungupReason = sipCDR.HangupReason;
        }
    }
}
