//-----------------------------------------------------------------------------
// Filename: SIPnonInviteTransaction.cs
//
// Description: SIP Transaction for all non-INVITE transactions where no dialog is required.
// 
// History:
// 18 May 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
using System.Text.RegularExpressions;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
	public class SIPNonInviteTransaction : SIPTransaction
	{
        public event SIPTransactionResponseReceivedDelegate NonInviteTransactionInfoResponseReceived;
        public event SIPTransactionResponseReceivedDelegate NonInviteTransactionFinalResponseReceived;
        public event SIPTransactionTimedOutDelegate NonInviteTransactionTimedOut;
        public event SIPTransactionRequestReceivedDelegate NonInviteRequestReceived;
        public event SIPTransactionRequestRetransmitDelegate NonInviteTransactionRequestRetransmit;

        internal SIPNonInviteTransaction(SIPTransport sipTransport, SIPRequest sipRequest, SIPEndPoint dstEndPoint, SIPEndPoint localSIPEndPoint, SIPEndPoint outboundProxy)
            : base(sipTransport, sipRequest, dstEndPoint, localSIPEndPoint, outboundProxy)
        {
            TransactionType = SIPTransactionTypesEnum.NonInvite;
            TransactionRequestReceived += SIPNonInviteTransaction_TransactionRequestReceived;
            TransactionInformationResponseReceived += SIPNonInviteTransaction_TransactionInformationResponseReceived;
            TransactionFinalResponseReceived += SIPNonInviteTransaction_TransactionFinalResponseReceived;
            TransactionTimedOut += SIPNonInviteTransaction_TransactionTimedOut;
            TransactionRemoved += SIPNonInviteTransaction_TransactionRemoved;
            TransactionRequestRetransmit += SIPNonInviteTransaction_TransactionRequestRetransmit;
        }

        private void SIPNonInviteTransaction_TransactionRemoved(SIPTransaction transaction)
        {
            // Remove all event handlers.
            NonInviteTransactionInfoResponseReceived = null;
            NonInviteTransactionFinalResponseReceived = null;
            NonInviteTransactionTimedOut = null;
            NonInviteRequestReceived = null;
        }

        private void SIPNonInviteTransaction_TransactionTimedOut(SIPTransaction sipTransaction)
        {
            if (NonInviteTransactionTimedOut != null)
            {
                NonInviteTransactionTimedOut(this);
            }
        }

        private void SIPNonInviteTransaction_TransactionRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest)
        {
            if (NonInviteRequestReceived != null)
            {
                NonInviteRequestReceived(localSIPEndPoint, remoteEndPoint, this, sipRequest);
            }
        }

        private void SIPNonInviteTransaction_TransactionInformationResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            if (NonInviteTransactionInfoResponseReceived != null)
            {
                NonInviteTransactionInfoResponseReceived(localSIPEndPoint, remoteEndPoint, this, sipResponse);
            }
        }

        private void SIPNonInviteTransaction_TransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            if (NonInviteTransactionFinalResponseReceived != null)
            {
                NonInviteTransactionFinalResponseReceived(localSIPEndPoint, remoteEndPoint, this, sipResponse);
            }
        }

        private void SIPNonInviteTransaction_TransactionRequestRetransmit(SIPTransaction sipTransaction, SIPRequest sipRequest, int retransmitNumber)
        {
            if (NonInviteTransactionRequestRetransmit != null)
            {
                NonInviteTransactionRequestRetransmit(sipTransaction, sipRequest, retransmitNumber);
            }
        }
    }
}
