//-----------------------------------------------------------------------------
// Filename: SIPCancelTransaction.cs
//
// Description: SIP Transaction created in response to a CANCEL request.
// 
// History:
// 14 Feb 2006	Aaron Clauson	Created.
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
	public class SIPCancelTransaction : SIPTransaction
	{
        public event SIPTransactionResponseReceivedDelegate CancelTransactionFinalResponseReceived;

        private UASInviteTransaction m_originalTransaction;

        internal SIPCancelTransaction(SIPTransport sipTransport, SIPRequest sipRequest, SIPEndPoint dstEndPoint, SIPEndPoint localSIPEndPoint, UASInviteTransaction originalTransaction)
            : base(sipTransport, sipRequest, dstEndPoint, localSIPEndPoint, originalTransaction.OutboundProxy)
        {
            m_originalTransaction = originalTransaction;
            TransactionType = SIPTransactionTypesEnum.NonInvite;
            TransactionRequestReceived += SIPCancelTransaction_TransactionRequestReceived;
            TransactionFinalResponseReceived += SIPCancelTransaction_TransactionFinalResponseReceived;
            TransactionRemoved += SIPCancelTransaction_TransactionRemoved;
        }

        private void SIPCancelTransaction_TransactionRemoved(SIPTransaction transaction)
        {
            // Remove event handlers.
            CancelTransactionFinalResponseReceived = null;
        }

        private void SIPCancelTransaction_TransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            if (sipResponse.StatusCode < 200)
            {
                logger.Warn("A SIP CANCEL transaction received an unexpected SIP information response " + sipResponse.ReasonPhrase + ".");
            }
            else
            {
                if (CancelTransactionFinalResponseReceived != null)
                {
                    CancelTransactionFinalResponseReceived(localSIPEndPoint, remoteEndPoint, sipTransaction, sipResponse);
                }
            }
        }

        private void SIPCancelTransaction_TransactionRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest)
        {
            try
            {
                //logger.Debug("CANCEL request received, attempting to locate and cancel transaction.");

                //UASInviteTransaction originalTransaction = (UASInviteTransaction)GetTransaction(GetRequestTransactionId(sipRequest.Header.Via.TopViaHeader.Branch, SIPMethodsEnum.INVITE));

                SIPResponse cancelResponse;

                if (m_originalTransaction != null)
                {
                    //logger.Debug("Transaction found to cancel " + originalTransaction.TransactionId + " type " + originalTransaction.TransactionType + ".");
                    m_originalTransaction.CancelCall();
                    cancelResponse = GetCancelResponse(sipRequest, SIPResponseStatusCodesEnum.Ok);
                }
                else
                {
                    cancelResponse = GetCancelResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist);
                }

                //UpdateTransactionState(SIPTransactionStatesEnum.Completed);
                SendFinalResponse(cancelResponse);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCancelTransaction GotRequest. " + excp.Message);
            }
        }

        private SIPResponse GetCancelResponse(SIPRequest sipRequest, SIPResponseStatusCodesEnum sipResponseCode)
        {
            try
            {
                SIPResponse cancelResponse = new SIPResponse(sipResponseCode, null, sipRequest.LocalSIPEndPoint);

                SIPHeader requestHeader = sipRequest.Header;
                cancelResponse.Header = new SIPHeader(requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);
                cancelResponse.Header.CSeqMethod = SIPMethodsEnum.CANCEL;
                cancelResponse.Header.Vias = requestHeader.Vias;
                cancelResponse.Header.MaxForwards = Int32.MinValue;

                return cancelResponse;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetCancelResponse. " + excp.Message);
                throw excp;
            }
        }
    }
}
