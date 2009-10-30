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
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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

namespace SIPSorcery.SIP {
    public delegate void CDRReadyDelegate(SIPCDR cdr);                // Used to inform CDR handlers when a CDR has been udpated.

    public enum SIPCallDirection {
        None = 0,
        In = 1,
        Out = 2,
    }

    /// <summary>
    /// Call detail record for a SIP call.
    /// </summary>
    [DataContract]
    public class SIPCDR {
        private static ILog logger = AppState.logger;
        private static string m_newLine = AppState.NewLine;

        public static event CDRReadyDelegate NewCDR = c => { };
        public static event CDRReadyDelegate HungupCDR = c => { };
        public static event CDRReadyDelegate CancelledCDR = c => { };

        [DataMember]
        public Guid CDRId { get; set; }

        [DataMember]
        public string Owner { get; set; }

        [DataMember]
        public string AdminMemberId { get; set; }

        [DataMember]
        public DateTime Created { get; set; }

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

        private DateTime? m_progressTime;
        [DataMember]
        public DateTime? ProgressTime {
            get { return m_progressTime; }
            set {
                if (value != null) {
                    m_progressTime = value.Value.ToUniversalTime();
                }
                else {
                    m_progressTime = null;
                }
            }
        }

        [DataMember]
        public int AnswerStatus { get; set; }

        [DataMember]
        public string AnswerReasonPhrase { get; set; }

        private DateTime? m_answerTime;
        [DataMember]
        public DateTime? AnswerTime {
            get { return m_answerTime; }
            set {
                if (value != null) {
                    m_answerTime = value.Value.ToUniversalTime();
                }
                else {
                    m_answerTime = null;
                }
            }
        }

        private DateTime? m_hangupTime;
        [DataMember]
        public DateTime? HangupTime {
            get { return m_hangupTime; }
            set {
                if (value != null) {
                    m_hangupTime = value.Value.ToUniversalTime();
                }
                else {
                    m_hangupTime = null;
                }
            }
        }

        [DataMember]
        public string HangupReason { get; set; }

        [DataMember]
        public Guid BridgeId { get; set; }                              // If the call formed part of a bridge this will be set to the bridge id.

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
            SIPEndPoint remoteEndPoint) {
            CDRId = Guid.NewGuid();
            Created = DateTime.UtcNow;
            CallDirection = callDirection;
            Destination = destination;
            From = from;
            CallId = callId;
            LocalSIPEndPoint = localSIPEndPoint;
            RemoteEndPoint = remoteEndPoint;
            InProgress = false;
            IsAnswered = false;
            IsHungup = false;
        }

        public void Progress(SIPResponseStatusCodesEnum progressStatus, string progressReason) {
            InProgress = true;
            ProgressTime = DateTime.UtcNow;
            ProgressStatus = (int)progressStatus;
            ProgressReasonPhrase = progressReason;
        }

        public void Answered(int answerStatusCode, SIPResponseStatusCodesEnum answerStatus, string answerReason) {
            try {
                IsAnswered = true;
                AnswerTime = DateTime.UtcNow;
                AnswerStatus = (int)answerStatus;
                AnswerReasonPhrase = answerReason;

                NewCDR(this);
            }
            catch (Exception excp) {
                logger.Error("Exception SIPCDR Answered. " + excp.Message);
            }
        }

        public void Cancelled()
        {
            try
            {
                CancelledCDR(this);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCDR Cancelled. " + excp.Message);
            }
        }

        public void TimedOut()
        {
            try
            {
                NewCDR(this);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCDR TimedOut. " + excp.Message);
            }
        }

        public void Hungup(string hangupReason) {
            try {
                IsHungup = true;
                HangupTime = DateTime.UtcNow;
                HangupReason = hangupReason;

                HungupCDR(this);
            }
            catch (Exception excp) {
                logger.Error("Exception SIPCDR Hungup. " + excp.Message);
            }
        }

        public double GetProgressDuration() {
            return (ProgressTime != null && AnswerTime != null) ? AnswerTime.Value.Subtract(ProgressTime.Value).TotalSeconds : 0;
        }

        public double GetAnsweredDuration() {
            return (AnswerTime != null && HangupTime != null) ? HangupTime.Value.Subtract(AnswerTime.Value).TotalSeconds : 0;
        }
    }
}
