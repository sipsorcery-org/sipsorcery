//-----------------------------------------------------------------------------
// Filename: ISIPClientUserAgent.cs
//
// Description: The interface definition for SIP Client User Agents (UAC).
// 
// History:
// 30 Aug 2009	Aaron Clauson   Created (aaron@sipsorcery.com), SIP Sorcery Ltd, London, UK (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.SIP.App
{

    public interface ISIPClientUserAgent
    {

        string Owner { get; }
        string AdminMemberId { get; }
        UACInviteTransaction ServerTransaction { get; }
        SIPDialogue SIPDialogue { get; }
        SIPCallDescriptor CallDescriptor { get; }
        bool IsUACAnswered { get; }

        // Real-time call control properties.
        //string AccountCode { get; set; }
        //decimal ReservedCredit { get; set; }
        //int ReservedSeconds { get; set; }
        //decimal Rate { get; set; }

        event SIPCallResponseDelegate CallTrying;
        event SIPCallResponseDelegate CallRinging;
        event SIPCallResponseDelegate CallAnswered;
        event SIPCallFailedDelegate CallFailed;

        void Call(SIPCallDescriptor sipCallDescriptor);
        void Cancel();
        void Update(CRMHeaders crmHeaders);
    }
}
