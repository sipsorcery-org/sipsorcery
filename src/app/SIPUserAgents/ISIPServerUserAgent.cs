//-----------------------------------------------------------------------------
// Filename: ISIPServerUserAgent.cs
//
// Description: The interface definition for SIP Server User Agents (UAC).

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 30 Aug 2009	Aaron Clauson   Created, Hobart, Asutralia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.SIP.App
{
    public delegate void SIPUASDelegate(ISIPServerUserAgent uas);

    /// <summary>
    /// Interface for classes implementing SIP server user agent functionality. The
    /// main function of a SIP client user agent is the ability to receive calls.
    /// </summary>
    public interface ISIPServerUserAgent
    {
        SIPCallDirection CallDirection { get; }
        SIPDialogue SIPDialogue { get; }
        SIPAccount SIPAccount { get; set; }
        bool IsAuthenticated { get; set; }
        bool IsB2B { get; }
        bool IsInvite { get; }                      // Set to true for server user agents that are handling an INVITE reqquest.
        SIPRequest CallRequest { get; }
        string CallDestination { get; }
        bool IsUASAnswered { get; }
        string Owner { get; }

        event SIPUASDelegate CallCancelled;
        event SIPUASDelegate NoRingTimeout;
        event SIPUASDelegate TransactionComplete;
        event SIPUASStateChangedDelegate UASStateChanged;

        bool LoadSIPAccountForIncomingCall();
        bool AuthenticateCall();
        void Progress(SIPResponseStatusCodesEnum progressStatus, string reasonPhrase, string[] customHeaders, string progressContentType, string progressBody);
        SIPDialogue Answer(string contentType, string body, SIPDialogue answeredDialogue, SIPDialogueTransferModesEnum transferMode);
        SIPDialogue Answer(string contentType, string body, string toTag, SIPDialogue answeredDialogue, SIPDialogueTransferModesEnum transferMode);
        void Reject(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase, string[] customHeaders);
        void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI);
        void NoCDR();
        void SetTraceDelegate(SIPTransactionTraceMessageDelegate traceDelegate);
        void SetOwner(string owner, string adminMemberId);
        void AnswerNonInvite(SIPResponseStatusCodesEnum answerStatus, string reasonPhrase, string[] customHeaders, string contentType, string body);
        void SetDialPlanContextID(Guid dialPlanContextID);
    }
}
