//-----------------------------------------------------------------------------
// Filename: SIPDialogue.cs
//
// Description: Base class for SIP dialogues. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 20 Oct 2005	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public enum SIPDialogueStateEnum
    {
        Unknown = 0,
        Early = 1,
        Confirmed = 2,
        Terminated = 3
    }

    public enum SIPDialogueTransferModesEnum
    {
        Default = 0,
        PassThru = 1,           // REFER requests will be treated as an in-dialogue request and passed through to user agents.
        NotAllowed = 2,         // REFER requests will be blocked.
        BlindPlaceCall = 3,     // REFER requests without a replaces parameter will initiate a new call.
    }

    /// <summary>
    /// See "Chapter 12 Dialogs" in RFC3261.
    /// </summary>
    /// <remarks>
    /// The standard states that there are two independent CSeq's for a dialogue: one for requests from the UAC and for requests
    /// from the UAS. In practice it's been noted that if a UAS (initial UAS) sends an in-dialogue request with a CSeq less than the
    /// UAC's CSeq it can cause problems. To avoid this issue when generating requests the remote CSeq is always used.
    /// </remarks>
    public class SIPDialogue
    {
        protected static ILogger logger = Log.Logger;

        public Guid Id { get; set; }                                // Id for persistence, NOT used for SIP call purposes.
        public string CallId { get; set; }
        public SIPRouteSet RouteSet { get; set; }
        public SIPUserField LocalUserField { get; set; }            // To header for a UAS, From header for a UAC.
        public string LocalTag { get; set; }
        public SIPUserField RemoteUserField { get; set; }           // To header for a UAC, From header for a UAS.    
        public string RemoteTag { get; set; }
        public int CSeq { get; set; }                               // CSeq being used by the remote UA for sending requests.
        public int RemoteCSeq { get; set; }                         // Latest CSeq received from the remote UA.
        public SIPURI RemoteTarget { get; set; }                    // This will be the Contact URI in the INVITE request or in the 2xx INVITE response and is where subsequent dialogue requests should be sent.
        public Guid CDRId { get; set; }                             // Call detail record for call the dialogue belongs to.
        public string ContentType { get; private set; }             // The content type on the request or response that created this dialogue. This is not part of or required for the dialogue and is kept for info and consumer app. purposes only.
        public string SDP { get; set; }                             // The sessions description protocol payload. This is not part of or required for the dialogue and is kept for info and consumer app. purposes only.
        public string RemoteSDP { get; set; }                       // The sessions description protocol payload from the remote end. This is not part of or required for the dialogue and is kept for info and consumer app. purposes only.
        public Guid BridgeId { get; set; }                          // If this dialogue gets bridged by a higher level application server the id for the bridge can be stored here.                   
        public int CallDurationLimit { get; set; }                  // If non-zero indicates the dialogue established should only be permitted to stay up for this many seconds.
        public string ProxySendFrom { get; set; }                   // If set this is the socket the upstream proxy received the call on.
        public SIPDialogueTransferModesEnum TransferMode { get; set; }  // Specifies how the dialogue will handle REFER requests (transfers).
        public SIPEndPoint RemoteSIPEndPoint { get; set; }          // The SIP end point for the remote party.

        /// <summary>
        /// Indicates whether the dialogue was created by a ingress or egress call.
        /// </summary>
        public SIPCallDirection Direction { get; set; }

        /// <summary>
        /// Used as a flag to indicate whether to send an immediate or slightly delayed re-INVITE request 
        /// when a call is answered as an attempt to help solve audio issues.
        /// </summary>
        public int ReinviteDelay = 0;

        public string DialogueName
        {
            get
            {
                string dialogueName = "L(??)";
                if (LocalUserField != null && !LocalUserField.URI.User.IsNullOrBlank())
                {
                    dialogueName = "L(" + LocalUserField.URI.ToString() + ")";
                }

                dialogueName += "-";

                if (RemoteUserField != null && !RemoteUserField.URI.User.IsNullOrBlank())
                {
                    dialogueName += "R(" + RemoteUserField.URI.ToString() + ")";
                }
                else
                {
                    dialogueName += "R(??)";
                }

                return dialogueName;
            }
        }

        private DateTime m_inserted;
        public DateTime Inserted
        {
            get { return m_inserted; }
            set { m_inserted = value.ToUniversalTime(); }
        }

        public SIPDialogueStateEnum DialogueState = SIPDialogueStateEnum.Unknown;

        internal SIPNonInviteTransaction m_byeTransaction;

        public SIPDialogue() { }

        public SIPDialogue(
            string callId,
            SIPRouteSet routeSet,
            SIPUserField localUser,
            SIPUserField remoteUser,
            int cseq,
            SIPURI remoteTarget,
            string localTag,
            string remoteTag,
            Guid cdrId,
            string sdp,
            string remoteSDP,
            SIPEndPoint remoteEndPoint)
        {
            Id = Guid.NewGuid();

            CallId = callId;
            RouteSet = routeSet;
            LocalUserField = localUser;
            LocalTag = localTag;
            RemoteUserField = remoteUser;
            RemoteTag = remoteTag;
            CSeq = cseq;
            RemoteTarget = remoteTarget;
            CDRId = cdrId;
            SDP = sdp;
            RemoteSDP = remoteSDP;
            Inserted = DateTime.UtcNow;
            Direction = SIPCallDirection.None;
            RemoteSIPEndPoint = remoteEndPoint?.CopyOf();
        }

        /// <summary>
        /// This constructor is used by server user agents or SIP elements acting in a server user agent role. When
        /// acting as a server user agent the local fields are contained in the To header and the remote fields are 
        /// in the From header.
        /// </summary>
        public SIPDialogue(UASInviteTransaction uasInviteTransaction)
        {
            Id = Guid.NewGuid();

            CallId = uasInviteTransaction.TransactionRequest.Header.CallId;
            //RouteSet = (uasInviteTransaction.TransactionFinalResponse != null && uasInviteTransaction.TransactionFinalResponse.Header.RecordRoutes != null) ? uasInviteTransaction.TransactionFinalResponse.Header.RecordRoutes.Reversed() : null;
            RouteSet = (uasInviteTransaction.TransactionFinalResponse != null && uasInviteTransaction.TransactionFinalResponse.Header.RecordRoutes != null) ? uasInviteTransaction.TransactionFinalResponse.Header.RecordRoutes : null;
            LocalUserField = uasInviteTransaction.TransactionFinalResponse.Header.To.ToUserField;
            LocalTag = uasInviteTransaction.TransactionFinalResponse.Header.To.ToTag;
            RemoteUserField = uasInviteTransaction.TransactionFinalResponse.Header.From.FromUserField;
            RemoteTag = uasInviteTransaction.TransactionFinalResponse.Header.From.FromTag;
            CSeq = uasInviteTransaction.TransactionRequest.Header.CSeq;
            CDRId = uasInviteTransaction.CDR != null ? uasInviteTransaction.CDR.CDRId : Guid.Empty;
            ContentType = uasInviteTransaction.TransactionFinalResponse.Header.ContentType;
            SDP = uasInviteTransaction.TransactionFinalResponse.Body;
            RemoteSDP = uasInviteTransaction.TransactionRequest.Body ?? uasInviteTransaction.AckRequest.Body;
            Inserted = DateTime.UtcNow;
            Direction = SIPCallDirection.In;

            if (uasInviteTransaction.m_gotPrack)
            {
                CSeq++;
            }

            var inviteReq = uasInviteTransaction.TransactionRequest;

            // Set the dialogue remote target taking into account optional Proxy header fields.
            // No mangling takes place. All the information is recorded to allow an application to perform mangling 
            // if so required.
            SIPEndPoint remoteEndPointViaProxy = SIPEndPoint.ParseSIPEndPoint(inviteReq.Header.ProxyReceivedFrom);
            RemoteSIPEndPoint = remoteEndPointViaProxy ?? inviteReq.RemoteSIPEndPoint.CopyOf();
            ProxySendFrom = inviteReq.Header.ProxyReceivedOn;

            if (inviteReq.Header.Contact != null && inviteReq.Header.Contact.Count > 0)
            {
                RemoteTarget = inviteReq.Header.Contact[0].ContactURI.CopyOf();
            }
            else
            {
                RemoteTarget = new SIPURI(inviteReq.URI.Scheme, inviteReq.RemoteSIPEndPoint);
            }

            //if (uasInviteTransaction.TransactionRequest.Header.Contact != null && uasInviteTransaction.TransactionRequest.Header.Contact.Count > 0)
            //{
            //    RemoteTarget = uasInviteTransaction.TransactionRequest.Header.Contact[0].ContactURI.CopyOf();
            //    if (!uasInviteTransaction.TransactionRequest.Header.ProxyReceivedFrom.IsNullOrBlank())
            //    {
            //        // Setting the Proxy-ReceivedOn header is how an upstream proxy will let an agent know it should mangle the contact. 
            //        // Don't mangle private contacts if there is a Record-Route header. If a proxy is putting private IP's in a Record-Route header that's its problem.
            //        if (RouteSet == null && IPSocket.IsPrivateAddress(RemoteTarget.HostAddress))
            //        {
            //            SIPEndPoint remoteUASSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(uasInviteTransaction.TransactionRequest.Header.ProxyReceivedFrom);
            //            RemoteTarget.Host = remoteUASSIPEndPoint.GetIPEndPoint().ToString();
            //        }
            //    }
            //}
        }

        /// <summary>
        /// This constructor is used by client user agents or SIP elements acting in a client user agent role. When
        /// acting as a client user agent the local fields are contained in the From header and the remote fields are 
        /// in the To header.
        /// </summary>
        public SIPDialogue(UACInviteTransaction uacInviteTransaction)
        {
            Id = Guid.NewGuid();

            CallId = uacInviteTransaction.TransactionRequest.Header.CallId;
            RouteSet = (uacInviteTransaction.TransactionFinalResponse != null && uacInviteTransaction.TransactionFinalResponse.Header.RecordRoutes != null) ? uacInviteTransaction.TransactionFinalResponse.Header.RecordRoutes.Reversed() : null;
            LocalUserField = uacInviteTransaction.TransactionFinalResponse.Header.From.FromUserField;
            LocalTag = uacInviteTransaction.TransactionFinalResponse.Header.From.FromTag;
            RemoteUserField = uacInviteTransaction.TransactionFinalResponse.Header.To.ToUserField;
            RemoteTag = uacInviteTransaction.TransactionFinalResponse.Header.To.ToTag;
            CSeq = uacInviteTransaction.TransactionRequest.Header.CSeq;
            CDRId = (uacInviteTransaction.CDR != null) ? uacInviteTransaction.CDR.CDRId : Guid.Empty;
            ContentType = uacInviteTransaction.TransactionRequest.Header.ContentType;
            SDP = uacInviteTransaction.TransactionRequest.Body;
            RemoteSDP = uacInviteTransaction.TransactionFinalResponse.Body;
            Inserted = DateTime.UtcNow;
            Direction = SIPCallDirection.Out;

            if (uacInviteTransaction.m_sentPrack)
            {
                CSeq++;
            }

            // Set the dialogue remote target taking into account optional Proxy header fields.
            // No mangling takes place. All the information is recorded to allow an application to perform mangling 
            // if so required.
            var finalResponse = uacInviteTransaction.TransactionFinalResponse;
            if (finalResponse.Header.Contact != null && finalResponse.Header.Contact.Count > 0)
            {
                RemoteTarget = finalResponse.Header.Contact[0].ContactURI.CopyOf();
            }
            else
            {
                // No contact header supplied by remote party. Best option is to use the original INVITE request URI.
                RemoteTarget = uacInviteTransaction.TransactionRequest.URI.CopyOf();
            }

            SIPEndPoint remoteEndPointViaProxy = SIPEndPoint.ParseSIPEndPoint(finalResponse.Header.ProxyReceivedFrom);
            RemoteSIPEndPoint = remoteEndPointViaProxy ?? finalResponse.RemoteSIPEndPoint.CopyOf();

            ProxySendFrom = uacInviteTransaction.TransactionFinalResponse.Header.ProxyReceivedOn;

            //if (uacInviteTransaction.TransactionFinalResponse.Header.Contact != null && uacInviteTransaction.TransactionFinalResponse.Header.Contact.Count > 0)
            //{
            //    RemoteTarget = uacInviteTransaction.TransactionFinalResponse.Header.Contact[0].ContactURI.CopyOf();
            //    if (!uacInviteTransaction.TransactionFinalResponse.Header.ProxyReceivedFrom.IsNullOrBlank())
            //    {
            //        // Setting the Proxy-ReceivedOn header is how an upstream proxy will let an agent know it should mangle the contact. 
            //        // Don't mangle private contacts if there is a Record-Route header. If a proxy is putting private IP's in a Record-Route header that's its problem.
            //        if (RouteSet == null && IPSocket.IsPrivateAddress(RemoteTarget.Host))
            //        {
            //            SIPEndPoint remoteUASSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(uacInviteTransaction.TransactionFinalResponse.Header.ProxyReceivedFrom);
            //            RemoteTarget.Host = remoteUASSIPEndPoint.GetIPEndPoint().ToString();
            //        }
            //    }
            //}
        }

        /// <summary>
        /// This constructor is used to create non-INVITE dialogues for example the dialogues used in SIP event interactions
        /// where the dialogue is created based on a SUBSCRIBE request.
        /// </summary>
        public SIPDialogue(
          SIPRequest nonInviteRequest,
          string toTag)
        {
            Id = Guid.NewGuid();

            CallId = nonInviteRequest.Header.CallId;
            RouteSet = (nonInviteRequest.Header.RecordRoutes != null) ? nonInviteRequest.Header.RecordRoutes.Reversed() : null;
            RemoteUserField = nonInviteRequest.Header.From.FromUserField;
            RemoteTag = nonInviteRequest.Header.From.FromTag;
            LocalUserField = nonInviteRequest.Header.To.ToUserField;
            LocalUserField.Parameters.Set("tag", toTag);
            LocalTag = toTag;
            CSeq = nonInviteRequest.Header.CSeq;
            Inserted = DateTime.UtcNow;
            Direction = SIPCallDirection.Out;

            SIPEndPoint remoteEndPointViaProxy = SIPEndPoint.ParseSIPEndPoint(nonInviteRequest.Header.ProxyReceivedFrom);
            RemoteSIPEndPoint = remoteEndPointViaProxy ?? nonInviteRequest.RemoteSIPEndPoint.CopyOf();

            // Set the dialogue remote target taking into account optional proxy header fields.
            // No mangling takes place. All the information is recorded to allow an application to perform mangling 
            // if so required.
            if (nonInviteRequest.Header.Contact != null && nonInviteRequest.Header.Contact.Count > 0)
            {
                RemoteTarget = nonInviteRequest.Header.Contact[0].ContactURI.CopyOf();
            }
            else
            {
                // No contact header supplied by remote party. Best option is to use the SIP end point the request came from.
                RemoteTarget = new SIPURI(nonInviteRequest.URI.Scheme, RemoteSIPEndPoint);
            }

            ProxySendFrom = nonInviteRequest.Header.ProxyReceivedOn;

            //if (!nonInviteRequest.Header.ProxyReceivedFrom.IsNullOrBlank())
            //{
            //    // Setting the Proxy-ReceivedOn header is how an upstream proxy will let an agent know it should mangle the contact.
            //    // Don't mangle private contacts if there is a Record-Route header. If a proxy is putting private IP's in a Record-Route header that's its problem.
            //    if (RouteSet == null && IPSocket.IsPrivateAddress(RemoteTarget.Host))
            //    {
            //        SIPEndPoint remoteUASIPEndPoint = SIPEndPoint.ParseSIPEndPoint(nonInviteRequest.Header.ProxyReceivedFrom);
            //        RemoteTarget.Host = remoteUASIPEndPoint.GetIPEndPoint().ToString();
            //    }
            //}
        }

        /// <summary>
        /// Generates a BYE request for this dialog and forwards it to the remote call party.
        /// This has the effect of hanging up the call.
        /// </summary>
        /// <param name="sipTransport">The transport layer to use for sending the request.</param>
        /// <param name="outboundProxy">Optional. If set an end point that the BYE request will be directly 
        /// forwarded to.</param>
        /// <param name="target">Optional. If set this will be set as the in-dialog request URI instead of
        /// the dialogue's remote target field. The primary purpose of setting a custom target is to allow
        /// an application to attempt to deal with IPv4 NATs.</param>
        public void Hangup(SIPTransport sipTransport, SIPEndPoint outboundProxy, SIPURI target = null)
        {
            try
            {
                DialogueState = SIPDialogueStateEnum.Terminated;

                SIPEndPoint byeOutboundProxy = null;
                if (outboundProxy != null && IPAddress.IsLoopback(outboundProxy.Address))
                {
                    byeOutboundProxy = outboundProxy;
                }
                else if (!ProxySendFrom.IsNullOrBlank())
                {
                    byeOutboundProxy = SIPEndPoint.ParseSIPEndPoint(ProxySendFrom);
                }
                else if (outboundProxy != null)
                {
                    byeOutboundProxy = outboundProxy;
                }

                SIPRequest byeRequest = GetInDialogRequest(SIPMethodsEnum.BYE, target);
                m_byeTransaction = new SIPNonInviteTransaction(sipTransport, byeRequest, byeOutboundProxy);
                m_byeTransaction.SendRequest();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPDialogue Hangup. " + excp.Message);
            }
        }

        /// <summary>
        /// Builds a basic SIP request with the header fields set to correctly identify it as an 
        /// in dialog request. Calling this method also increments the dialog's local CSeq counter.
        /// This is safe to do even if the request does not end up being sent.
        /// </summary>
        /// <param name="method">The method of the SIP request to create.</param>
        /// <param name="target">Optional. If set this will be set as the in-dialog request URI instead of 
        /// the dialogue's remote target field. The primary purpose of setting a custom target is to allow 
        /// an application to attempt to deal with IPv4 NATs.</param>
        /// <returns>An in dialog SIP request.</returns>
        public SIPRequest GetInDialogRequest(SIPMethodsEnum method, SIPURI target = null)
        {
            CSeq++;

            SIPRequest inDialogRequest = new SIPRequest(method, target ?? RemoteTarget);
            SIPFromHeader fromHeader = SIPFromHeader.ParseFromHeader(LocalUserField.ToString());
            SIPToHeader toHeader = SIPToHeader.ParseToHeader(RemoteUserField.ToString());
            int cseq = CSeq;

            SIPHeader header = new SIPHeader(fromHeader, toHeader, cseq, CallId);
            header.CSeqMethod = method;
            inDialogRequest.Header = header;
            inDialogRequest.Header.Routes = RouteSet;
            inDialogRequest.Header.ProxySendFrom = ProxySendFrom;
            inDialogRequest.Header.Vias.PushViaHeader(SIPViaHeader.GetDefaultSIPViaHeader());

            return inDialogRequest;
        }
    }
}
