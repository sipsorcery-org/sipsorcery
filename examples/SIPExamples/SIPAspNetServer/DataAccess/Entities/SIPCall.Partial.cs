// ============================================================================
// FileName: SIPCall.Partial.cs
//
// Description:
// Represents the SIPCall entity. This partial class is used to apply 
// additional properties or metadata to the audo generated SIPCall class.
//
// A SIPCall corresponds to the establishment of a SIP Dialogue between 
// two SIP user agents.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 01 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using SIPSorcery.SIP;

#nullable disable

namespace SIPAspNetServer.DataAccess
{
    public partial class SIPCall
    {
        public SIPCall()
        { }

        /// <summary>
        /// This constructor translates the SIP layer dialogue to a data access
        /// layer entity.
        /// </summary>
        /// <param name="dialogue">The SIP layer dialogue to translate.</param>
        public SIPCall(SIPDialogue dialogue)
        {
            ID = dialogue.Id;
            CDRID = null; // dialogue.CDRId != Guid.Empty ? dialogue.CDRId : null;
            LocalTag = dialogue.LocalTag;
            RemoteTag = dialogue.RemoteTag;
            CallID = dialogue.CallId;
            CSeq = dialogue.CSeq;
            BridgeID = dialogue.BridgeId;
            RemoteTarget = dialogue.RemoteTarget.ToString();
            LocalUserField = dialogue.LocalUserField.ToString();
            RemoteUserField = dialogue.RemoteUserField.ToString();
            ProxySIPSocket = dialogue.ProxySendFrom;
            RouteSet = dialogue.RouteSet?.ToString();
            CallDurationLimit = dialogue.CallDurationLimit;
            Direction = dialogue.Direction.ToString();
            Inserted = dialogue.Inserted;
        }

        /// <summary>
        /// Translates a data access layer SIPDialog entity to a SIP layer SIPDialogue.
        /// </summary>
        public SIPDialogue ToSIPDialogue()
        {
            SIPDialogue dialogue = new SIPDialogue();

            dialogue.Id = ID;
            dialogue.CDRId = CDRID.GetValueOrDefault();
            dialogue.LocalTag= LocalTag;
            dialogue.RemoteTag = RemoteTag;
            dialogue.CallId = CallID;
            dialogue.CSeq = CSeq;
            dialogue.BridgeId = BridgeID;
            dialogue.RemoteTarget = SIPURI.ParseSIPURIRelaxed(RemoteTarget);
            dialogue.LocalUserField = SIPUserField.ParseSIPUserField(LocalUserField);
            dialogue.RemoteUserField = SIPUserField.ParseSIPUserField(RemoteUserField);
            dialogue.ProxySendFrom = ProxySIPSocket;
            dialogue.RouteSet = string.IsNullOrWhiteSpace(RouteSet) ? null : SIPRouteSet.ParseSIPRouteSet(RouteSet);
            dialogue.CallDurationLimit = CallDurationLimit.GetValueOrDefault();
            dialogue.Direction = Enum.Parse<SIPCallDirection>(Direction, true);
            dialogue.Inserted = Inserted;

            return dialogue;
        }
    }
}
