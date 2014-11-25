//-----------------------------------------------------------------------------
// Filename: ISIPServerUserAgent.cs
//
// Description: The interface definition for SIP Server User Agents (UAC).
// 
// History:
// 30 Aug 2009	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, London, UK (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
