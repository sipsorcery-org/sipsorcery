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

namespace SIPSorcery.SIP
{
    public delegate void CDRUpdatedDelegate(SIPCDR cdr);                // Used to inform CDR handlers when a CDR has been udpated.

    public enum SIPCallDirection
    {
        None = 0,
        In = 1,
        Out = 2,
    }

    /// <summary>
    /// Call detail record for a SIP call.
    /// </summary>
    [DataContract]
    public class SIPCDR
	{
        private static ILog logger = AppState.logger;
        private static string m_newLine = AppState.NewLine;

        public static event CDRUpdatedDelegate CDRUpdated;
        
        [DataMember]
        public Guid CDRId { get; set; }

        [DataMember]
        public string Owner { get; set; }

        [DataMember]
        public string AdminMemberId { get; set; }

        [DataMember]
        public DateTime Created { get; set; }

        [DataMember]
        public SIPCallDirection CallDirection { get; set;}

        [DataMember]
        public SIPURI Destination { get; set; }

        //[DataMember]
        public SIPFromHeader From { get; set; }

        [DataMember]
        public int ProgressStatus { get; set; }

        [DataMember]
        public string ProgressReasonPhrase { get; set; }

        [DataMember]
        public DateTime? ProgressTime { get; set; }

        [DataMember]
        public int AnswerStatus { get; set; }

        [DataMember]
        public string AnswerReasonPhrase { get; set; }

        [DataMember]
        public DateTime? AnswerTime { get; set; }

        [DataMember]
        public DateTime? HangupTime { get; set; }

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
            SIPEndPoint remoteEndPoint)
        {
            CDRId = Guid.NewGuid();
            Created = DateTime.Now;
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

        public void Progress(SIPResponseStatusCodesEnum progressStatus, string progressReason)
        {
            InProgress = true;
            ProgressTime = DateTime.Now;
            ProgressStatus = (int)progressStatus;
            ProgressReasonPhrase = progressReason;
            FireCDRUpdated(this);
        }

        public void Answered(SIPResponseStatusCodesEnum answerStatus, string answerReason)
        {
            IsAnswered = true;
            AnswerTime = DateTime.Now;
            AnswerStatus = (int)answerStatus;
            AnswerReasonPhrase = answerReason;
            FireCDRUpdated(this);
        }

        public void Hungup(string hangupReason)
        {
            IsHungup = true;
            HangupTime = DateTime.Now;
            HangupReason = hangupReason;
            FireCDRUpdated(this);
        }

        public double GetProgressDuration()
        {
            return (ProgressTime != null && AnswerTime != null) ? AnswerTime.Value.Subtract(ProgressTime.Value).TotalSeconds : 0;
        }

        public double GetAnsweredDuration()
        {
            return (AnswerTime != null && HangupTime != null) ? HangupTime.Value.Subtract(AnswerTime.Value).TotalSeconds : 0;
        }

        private static void FireCDRUpdated(SIPCDR cdr)
        {
            try
            {
                if (CDRUpdated != null)
                {
                    CDRUpdated(cdr);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireCDRUpdated. " + excp.Message);
            }
        }
	}
}
