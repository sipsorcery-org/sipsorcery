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
// 14 Nov 2008	Aaron Clauson	Created.
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
// ============================================================================

using System;
using System.Collections.Generic;
using System.Net;

namespace SIPSorcery.SIP
{
    // SIP Channel delegates.
    public delegate void SIPMessageSentDelegate(SIPChannel sipChannel, SIPEndPoint remoteEndPoint, byte[] buffer);
    public delegate void SIPMessageReceivedDelegate(SIPChannel sipChannel, SIPEndPoint remoteEndPoint, byte[] buffer);

    // SIP Transport delegates.
    public delegate void SIPTransportRequestDelegate(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest);
    public delegate void SIPTransportResponseDelegate(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse);
    public delegate void SIPTransportSIPBadMessageDelegate(SIPEndPoint localSIPEndPoint, SIPEndPoint remotePoint, string message, SIPValidationFieldsEnum errorField, string rawMessage);
    public delegate void STUNRequestReceivedDelegate(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, byte[] buffer, int bufferLength);
    public delegate SIPDNSLookupResult ResolveSIPEndPointDelegate(SIPURI uri, bool async);

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
