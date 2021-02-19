//-----------------------------------------------------------------------------
// Filename: SIPCDR.cs
//
// Description: SIP Call Detail Records. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 17 Feb 2008	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

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
        private static ILogger logger = Log.Logger;

        public static event CDRReadyDelegate CDRCreated = c => { };
        public static event CDRReadyDelegate CDRUpdated = c => { };
        public static event CDRReadyDelegate CDRAnswered = c => { };
        public static event CDRReadyDelegate CDRHungup = c => { };

        [DataMember]
        public Guid CDRId { get; set; }

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
        public DateTime? ProgressTime
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

        private DateTime? m_answerTime;
        [DataMember]
        public DateTime? AnswerTime
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

        private DateTime? m_hangupTime;
        [DataMember]
        public DateTime? HangupTime
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

            CDRCreated?.Invoke(this);
        }

        public void Progress(SIPResponseStatusCodesEnum progressStatus, string progressReason, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint)
        {
            InProgress = true;
            ProgressTime = DateTime.UtcNow;
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
            IsAnswered = true;
            AnswerTime = DateTime.UtcNow;
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

            CDRAnswered?.Invoke(this);
        }

        public void Cancelled(string cancelReason = null)
        {
            HangupReason = (cancelReason != null) ? cancelReason : "Client cancelled";
            CDRAnswered?.Invoke(this);
        }

        public void TimedOut()
        {
            HangupReason = "Timed out";
            CDRAnswered?.Invoke(this);
        }

        public void Hungup(string hangupReason)
        {
            IsHungup = true;
            HangupTime = DateTime.UtcNow;
            HangupReason = hangupReason;

            CDRHungup?.Invoke(this);
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
            CDRUpdated?.Invoke(this);
        }
    }
}
