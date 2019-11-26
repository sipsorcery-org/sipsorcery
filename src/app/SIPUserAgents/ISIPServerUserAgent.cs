//-----------------------------------------------------------------------------
// Filename: ISIPServerUserAgent.cs
//
// Description: The interface definition for SIP Server User Agents (UAC).
// 
// History:
// 30 Aug 2009	Aaron Clauson   Created (aaron@sipsorcery.com), SIP Sorcery Ltd, London, UK (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.SIP.App
{
    public delegate void SIPUASDelegate(ISIPServerUserAgent uas);

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
