//-----------------------------------------------------------------------------
// Filename: ISIPClientUserAgent.cs
//
// Description: The interface definition for SIP Client User Agents (UAC).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 30 Aug 2009	Aaron Clauson   Created, Hobart, Australia.
// rj2: need the original SIPRequest as return of Call-method
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// Interface for classes implementing SIP client user agent functionality. The
    /// main function of a SIP client user agent is the ability to initiate calls.
    /// </summary>
    public interface ISIPClientUserAgent
    {
        string Owner { get; }
        string AdminMemberId { get; }
        UACInviteTransaction ServerTransaction { get; }
        SIPDialogue SIPDialogue { get; }
        SIPCallDescriptor CallDescriptor { get; }
        bool IsUACAnswered { get; }

        event SIPCallResponseDelegate CallTrying;
        event SIPCallResponseDelegate CallRinging;
        event SIPCallResponseDelegate CallAnswered;
        event SIPCallFailedDelegate CallFailed;

        SIPRequest Call(SIPCallDescriptor sipCallDescriptor);
        SIPRequest Call(SIPCallDescriptor sipCallDescriptor, SIPEndPoint serverEndPoint);
        void Cancel();
        void Update(CRMHeaders crmHeaders);
    }
}
