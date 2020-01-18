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

        protected static string m_CRLF = SIPConstants.CRLF;
        protected static string m_sipVersion = SIPConstants.SIP_VERSION_STRING;
        private static readonly int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;

        public Guid Id { get; set; }                                // Id for persistence, NOT used for SIP call purposes.
        public string Owner { get; set; }                           // In cases where ownership needs to be set on the dialogue this value can be used. Does not have any effect on the operation of the dialogue and is for info only.
        public string AdminMemberId { get; set; }
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

        /// <summary>
        /// Indicates whether the dialogue was created by a ingress or egress call.
        /// </summary>
        public SIPCallDirection Direction { get; set; }

        public string CRMPersonName { get; set; }
        public string CRMCompanyName { get; set; }
        public string CRMPictureURL { get; set; }

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

        private DateTimeOffset m_inserted;
        public DateTimeOffset Inserted
        {
            get { return m_inserted; }
            set { m_inserted = value.ToUniversalTime(); }
        }

        public SIPDialogueStateEnum DialogueState = SIPDialogueStateEnum.Unknown;

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
            string owner,
            string adminMemberId,
            string sdp,
            string remoteSDP)
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
            Owner = owner;
            AdminMemberId = adminMemberId;
            SDP = sdp;
            RemoteSDP = remoteSDP;
            Inserted = DateTimeOffset.UtcNow;
            Direction = SIPCallDirection.None;
        }

        /// <summary>
        /// This constructor is used by server user agents or SIP elements acting in a server user agent role. When
        /// acting as a server user agent the local fields are contained in the To header and the remote fields are 
        /// in the From header.
        /// </summary>
        public SIPDialogue(
            UASInviteTransaction uasInviteTransaction,
            string owner,
            string adminMemberId)
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
            CDRId = uasInviteTransaction.CDR.CDRId;
            Owner = owner;
            AdminMemberId = adminMemberId;
            ContentType = uasInviteTransaction.TransactionFinalResponse.Header.ContentType;
            SDP = uasInviteTransaction.TransactionFinalResponse.Body;
            RemoteSDP = uasInviteTransaction.TransactionRequest.Body;
            Inserted = DateTimeOffset.UtcNow;
            Direction = SIPCallDirection.In;

            RemoteTarget = new SIPURI(uasInviteTransaction.TransactionRequest.URI.Scheme, uasInviteTransaction.TransactionRequest.RemoteSIPEndPoint.CopyOf());
            ProxySendFrom = uasInviteTransaction.TransactionRequest.Header.ProxyReceivedOn;
            if (uasInviteTransaction.TransactionRequest.Header.Contact != null && uasInviteTransaction.TransactionRequest.Header.Contact.Count > 0)
            {
                RemoteTarget = uasInviteTransaction.TransactionRequest.Header.Contact[0].ContactURI.CopyOf();
                if (!uasInviteTransaction.TransactionRequest.Header.ProxyReceivedFrom.IsNullOrBlank())
                {
                    // Setting the Proxy-ReceivedOn header is how an upstream proxy will let an agent know it should mangle the contact. 
                    // Don't mangle private contacts if there is a Record-Route header. If a proxy is putting private IP's in a Record-Route header that's its problem.
                    if (RouteSet == null && IPSocket.IsPrivateAddress(RemoteTarget.Host))
                    {
                        SIPEndPoint remoteUASSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(uasInviteTransaction.TransactionRequest.Header.ProxyReceivedFrom);
                        RemoteTarget.Host = remoteUASSIPEndPoint.GetIPEndPoint().ToString();
                    }
                }
            }
        }

        /// <summary>
        /// This constructor is used by client user agents or SIP elements acting in a client user agent role. When
        /// acting as a client user agent the local fields are contained in the From header and the remote fields are 
        /// in the To header.
        /// </summary>
        public SIPDialogue(
          UACInviteTransaction uacInviteTransaction,
          string owner,
          string adminMemberId)
        {
            Id = Guid.NewGuid();

            CallId = uacInviteTransaction.TransactionRequest.Header.CallId;
            RouteSet = (uacInviteTransaction.TransactionFinalResponse != null && uacInviteTransaction.TransactionFinalResponse.Header.RecordRoutes != null) ? uacInviteTransaction.TransactionFinalResponse.Header.RecordRoutes.Reversed() : null;
            LocalUserField = uacInviteTransaction.TransactionFinalResponse.Header.From.FromUserField;
            LocalTag = uacInviteTransaction.TransactionFinalResponse.Header.From.FromTag;
            RemoteUserField = uacInviteTransaction.TransactionFinalResponse.Header.To.ToUserField;
            RemoteTag = uacInviteTransaction.TransactionFinalResponse.Header.To.ToTag;
            CSeq = uacInviteTransaction.TransactionRequest.Header.CSeq;
            CDRId = uacInviteTransaction.CDR.CDRId;
            Owner = owner;
            AdminMemberId = adminMemberId;
            ContentType = uacInviteTransaction.TransactionRequest.Header.ContentType;
            SDP = uacInviteTransaction.TransactionRequest.Body;
            RemoteSDP = uacInviteTransaction.TransactionFinalResponse.Body;
            Inserted = DateTimeOffset.UtcNow;
            Direction = SIPCallDirection.Out;

            // Set the dialogue remote target and take care of mangling if an upstream proxy has indicated it's required.
            if (uacInviteTransaction.TransactionFinalResponse != null)
            {
                RemoteTarget = new SIPURI(uacInviteTransaction.TransactionRequest.URI.Scheme, uacInviteTransaction.TransactionFinalResponse.RemoteSIPEndPoint.CopyOf());
            }
            else
            {
                RemoteTarget = new SIPURI(uacInviteTransaction.TransactionRequest.URI.Scheme, uacInviteTransaction.TransactionRequest.RemoteSIPEndPoint.CopyOf());
            }
            ProxySendFrom = uacInviteTransaction.TransactionFinalResponse.Header.ProxyReceivedOn;
            if (uacInviteTransaction.TransactionFinalResponse.Header.Contact != null && uacInviteTransaction.TransactionFinalResponse.Header.Contact.Count > 0)
            {
                RemoteTarget = uacInviteTransaction.TransactionFinalResponse.Header.Contact[0].ContactURI.CopyOf();
                if (!uacInviteTransaction.TransactionFinalResponse.Header.ProxyReceivedFrom.IsNullOrBlank())
                {
                    // Setting the Proxy-ReceivedOn header is how an upstream proxy will let an agent know it should mangle the contact. 
                    // Don't mangle private contacts if there is a Record-Route header. If a proxy is putting private IP's in a Record-Route header that's its problem.
                    if (RouteSet == null && IPSocket.IsPrivateAddress(RemoteTarget.Host))
                    {
                        SIPEndPoint remoteUASSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(uacInviteTransaction.TransactionFinalResponse.Header.ProxyReceivedFrom);
                        RemoteTarget.Host = remoteUASSIPEndPoint.GetIPEndPoint().ToString();
                    }
                }
            }
        }

        /// <summary>
        /// This constructor is used to create non-INVITE dialogues for example the dialogues used in SIP event interactions
        /// where the dialogue is created based on a SUBSCRIBE request.
        /// </summary>
        public SIPDialogue(
          SIPRequest nonInviteRequest,
          string owner,
          string adminMemberId,
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
            Owner = owner;
            AdminMemberId = adminMemberId;
            Inserted = DateTimeOffset.UtcNow;
            Direction = SIPCallDirection.Out;

            // Set the dialogue remote target and take care of mangling if an upstream proxy has indicated it's required.
            RemoteTarget = nonInviteRequest.Header.Contact[0].ContactURI;
            ProxySendFrom = nonInviteRequest.Header.ProxyReceivedOn;

            if (!nonInviteRequest.Header.ProxyReceivedFrom.IsNullOrBlank())
            {
                // Setting the Proxy-ReceivedOn header is how an upstream proxy will let an agent know it should mangle the contact.
                // Don't mangle private contacts if there is a Record-Route header. If a proxy is putting private IP's in a Record-Route header that's its problem.
                if (RouteSet == null && IPSocket.IsPrivateAddress(RemoteTarget.Host))
                {
                    SIPEndPoint remoteUASIPEndPoint = SIPEndPoint.ParseSIPEndPoint(nonInviteRequest.Header.ProxyReceivedFrom);
                    RemoteTarget.Host = remoteUASIPEndPoint.GetIPEndPoint().ToString();
                }
            }
        }

        /// <summary>
        /// Generates a BYE request for this dialog and forwards it to the remote cal party.
        /// This has the effect of hanging up the call.
        /// </summary>
        /// <param name="sipTransport">The transport layer to use for sending the request.</param>
        /// <param name="outboundProxy">Optional. If set an end point that the BYE request will be directly forwarded to.</param>
        public void Hangup(SIPTransport sipTransport, SIPEndPoint outboundProxy)
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
                    byeOutboundProxy = new SIPEndPoint(new IPEndPoint(SIPEndPoint.ParseSIPEndPoint(ProxySendFrom).Address, m_defaultSIPPort));
                }
                else if (outboundProxy != null)
                {
                    byeOutboundProxy = outboundProxy;
                }

                SIPRequest byeRequest = GetInDialogRequest(SIPMethodsEnum.BYE);
                SIPNonInviteTransaction byeTransaction = new SIPNonInviteTransaction(sipTransport, byeRequest, byeOutboundProxy);
                byeTransaction.SendRequest();
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
        /// <returns>An in dialog SIP request.</returns>
        public SIPRequest GetInDialogRequest(SIPMethodsEnum method)
        {
            CSeq++;

            SIPRequest inDialogRequest = new SIPRequest(method, RemoteTarget);
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
