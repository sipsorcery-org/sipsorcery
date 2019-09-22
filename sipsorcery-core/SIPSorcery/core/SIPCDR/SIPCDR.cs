//-----------------------------------------------------------------------------
// Filename: SIPCDR.cs
//
// Description: SIP Call Detail Records. 
// 
// History:
// 17 Feb 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Runtime.Serialization;
using SIPSorcery.Sys;
using log4net;

#if !SILVERLIGHT
using System.Data;
#endif

namespace SIPSorcery.SIP
{
    public delegate void CDRReadyDelegate(SIPCDR cdr);                // Used to inform CDR handlers when a CDR has been udpated.

    public enum SIPCallDirection
    {
        None = 0,
        In = 1,
        Out = 2,
        Redirect = 3,
    }

    /// <summary>
    /// Call detail record for a SIP call.
    /// </summary>
    [DataContract]
    public class SIPCDR
    {
        private static ILog logger = AppState.logger;
        private static string m_newLine = AppState.NewLine;

        public static event CDRReadyDelegate CDRCreated = c => { };
        public static event CDRReadyDelegate CDRUpdated = c => { };
        public static event CDRReadyDelegate CDRAnswered = c => { };
        public static event CDRReadyDelegate CDRHungup = c => { };

        [DataMember]
        public Guid CDRId { get; set; }

        [DataMember]
        public string Owner { get; set; }

        [DataMember]
        public string AdminMemberId { get; set; }

        [DataMember]
        public DateTimeOffset Created { get; set; }

        [DataMember]
        public SIPCallDirection CallDirection { get; set; }

        [DataMember]
        public SIPURI Destination { get; set; }

        //[DataMember]
        public SIPFromHeader From { get; set; }

        [DataMember]
        public int ProgressStatus { get; set; }

        [DataMember]
        public string ProgressReasonPhrase { get; set; }

        private DateTimeOffset? m_progressTime;
        [DataMember]
        public DateTimeOffset? ProgressTime
        {
            get { return m_progressTime; }
            set
            {
                if (value != null)
                {
                    m_progressTime = value.Value.ToUniversalTime();
                }
                else
                {
                    m_progressTime = null;
                }
            }
        }

        [DataMember]
        public int AnswerStatus { get; set; }

        [DataMember]
        public string AnswerReasonPhrase { get; set; }

        private DateTimeOffset? m_answerTime;
        [DataMember]
        public DateTimeOffset? AnswerTime
        {
            get { return m_answerTime; }
            set
            {
                if (value != null)
                {
                    m_answerTime = value.Value.ToUniversalTime();
                }
                else
                {
                    m_answerTime = null;
                }
            }
        }

        private DateTimeOffset? m_hangupTime;
        [DataMember]
        public DateTimeOffset? HangupTime
        {
            get { return m_hangupTime; }
            set
            {
                if (value != null)
                {
                    m_hangupTime = value.Value.ToUniversalTime();
                }
                else
                {
                    m_hangupTime = null;
                }
            }
        }

        [DataMember]
        public string HangupReason { get; set; }

        [DataMember]
        public Guid BridgeId { get; set; }                              // If the call formed part of a bridge this will be set to the bridge id.

        [DataMember]
        public DateTime? AnsweredAt { get; set; }

        [DataMember]
        public Guid DialPlanContextID { get; set; }                     // If the call is received into or initiated from a dialplan then this will hold the dialplan context ID.

        public string CallId { get; set; }
        public SIPEndPoint LocalSIPEndPoint { get; set; }
        public SIPEndPoint RemoteEndPoint { get; set; }
        public bool InProgress { get; set; }
        public bool IsAnswered { get; set; }
        public bool IsHungup { get; set; }

        public SIPCDR() { }

        public SIPCDR(
            SIPCallDirection callDirection,
            SIPURI destination,
            SIPFromHeader from,
            string callId,
            SIPEndPoint localSIPEndPoint,
            SIPEndPoint remoteEndPoint)
        {
            CDRId = Guid.NewGuid();
            Created = DateTimeOffset.UtcNow;
            CallDirection = callDirection;
            Destination = destination;
            From = from;
            CallId = callId;
            LocalSIPEndPoint = localSIPEndPoint;
            RemoteEndPoint = remoteEndPoint;
            InProgress = false;
            IsAnswered = false;
            IsHungup = false;

            CDRCreated(this);
        }

        public void Progress(SIPResponseStatusCodesEnum progressStatus, string progressReason, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint)
        {
            InProgress = true;
            ProgressTime = DateTimeOffset.UtcNow;
            ProgressStatus = (int)progressStatus;
            ProgressReasonPhrase = progressReason;

            if (localEndPoint != null)
            {
                LocalSIPEndPoint = localEndPoint;
            }

            if (remoteEndPoint != null)
            {
                RemoteEndPoint = remoteEndPoint;
            }
        }

        public void Answered(int answerStatusCode, SIPResponseStatusCodesEnum answerStatus, string answerReason, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint)
        {
            try
            {
                IsAnswered = true;
                AnswerTime = DateTimeOffset.UtcNow;
                AnswerStatus = (int)answerStatus;
                AnswerReasonPhrase = answerReason;
                AnsweredAt = DateTime.Now;

                if (localEndPoint != null)
                {
                    LocalSIPEndPoint = localEndPoint;
                }

                if (remoteEndPoint != null)
                {
                    RemoteEndPoint = remoteEndPoint;
                }

                CDRAnswered(this);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCDR Answered. " + excp);
            }
        }

        public void Cancelled(string cancelReason = null)
        {
            try
            {
                HangupReason = (cancelReason != null) ? cancelReason : "Client cancelled";
                CDRAnswered(this);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCDR Cancelled. " + excp);
            }
        }

        public void TimedOut()
        {
            try
            {
                HangupReason = "Timed out";
                CDRAnswered(this);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCDR TimedOut. " + excp);
            }
        }

        public void Hungup(string hangupReason)
        {
            try
            {
                IsHungup = true;
                HangupTime = DateTimeOffset.UtcNow;
                HangupReason = hangupReason;

                CDRHungup(this);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCDR Hungup. " + excp);
            }
        }

        public int GetProgressDuration()
        {
            return (ProgressTime != null && AnswerTime != null) ? Convert.ToInt32(AnswerTime.Value.Subtract(ProgressTime.Value).TotalSeconds) : 0;
        }

        public int GetAnsweredDuration()
        {
            return (AnswerTime != null && HangupTime != null) ? Convert.ToInt32(HangupTime.Value.Subtract(AnswerTime.Value).TotalSeconds) : 0;
        }

        public void Updated()
        {
            try
            {
                CDRUpdated(this);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCDR Updated. " + excp);
            }
        }
    }
}
