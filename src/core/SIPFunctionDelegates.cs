// ============================================================================
// FileName: SIPFunctionDelegates.cs
//
// Description:
// A list of function delegates that are used by the SIP Server Agents.
//
// Author(s):
// Aaron Clauson
//
// History:
// 14 Nov 2008	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System.Net;

namespace SIPSorcery.SIP
{
    // SIP Channel delegates.
    public delegate void SIPMessageSentDelegate(SIPChannel sipChannel, SIPEndPoint remoteEndPoint, byte[] buffer);
    public delegate void SIPMessageReceivedDelegate(SIPChannel sipChannel, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, byte[] buffer);

    // SIP Transport delegates.
    public delegate void SIPTransportRequestDelegate(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest);
    public delegate void SIPTransportResponseDelegate(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse);
    public delegate void SIPTransportSIPBadMessageDelegate(SIPEndPoint localSIPEndPoint, SIPEndPoint remotePoint, string message, SIPValidationFieldsEnum errorField, string rawMessage);
    public delegate void STUNRequestReceivedDelegate(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, byte[] buffer, int bufferLength);
    public delegate SIPDNSLookupResult ResolveSIPEndPointDelegate(SIPURI uri, bool async, bool? preferIPv6);

    // SIP Transaction delegates.
    public delegate void SIPTransactionStateChangeDelegate(SIPTransaction sipTransaction);
    public delegate void SIPTransactionResponseReceivedDelegate(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse);
    public delegate void SIPTransactionRequestReceivedDelegate(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest);
    public delegate void SIPTransactionCancelledDelegate(SIPTransaction sipTransaction);
    public delegate void SIPTransactionTimedOutDelegate(SIPTransaction sipTransaction);
    public delegate void SIPTransactionRequestRetransmitDelegate(SIPTransaction sipTransaction, SIPRequest sipRequest, int retransmitNumber);
    public delegate void SIPTransactionResponseRetransmitDelegate(SIPTransaction sipTransaction, SIPResponse sipResponse, int retransmitNumber);
    public delegate void SIPTransactionRemovedDelegate(SIPTransaction sipTransaction);
    public delegate void SIPTransactionTraceMessageDelegate(SIPTransaction sipTransaction, string message);
}
