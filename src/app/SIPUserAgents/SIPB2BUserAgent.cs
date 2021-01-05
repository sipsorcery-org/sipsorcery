//-----------------------------------------------------------------------------
// Filename: SIPB2BUserAgent.cs
//
// Description: Implementation of a SIP Back-to-back User Agent.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 21 Jul 2009  Aaron Clauson   Created, Hobart, Australia.
// 28 Dec 2020  Aaron Clauson   Added back into library and updated for new API. 
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// This class represents a back-to-back (B2B) user agent (UA) that is used 
    /// to attach an outgoing call (UAC) to an incoming (UAS) call.
    /// </summary>
    public class SIPB2BUserAgent : SIPServerUserAgent, ISIPClientUserAgent
    {
        private static ILogger logger = Log.Logger;

        private SIPClientUserAgent m_uac;
        private SIPCallDescriptor m_uacCallDescriptor;

        // User Agent Client Events.
        public event SIPCallResponseDelegate CallTrying;
        public event SIPCallFailedDelegate CallFailed;
        public event SIPCallResponseDelegate CallRinging;
        public event SIPCallResponseDelegate CallAnswered;

        public UACInviteTransaction ServerTransaction => m_uac?.ServerTransaction;
        public SIPCallDescriptor CallDescriptor => m_uacCallDescriptor;
        public bool IsUACAnswered => (m_uac != null) ? m_uac.IsUACAnswered : false;

        public SIPB2BUserAgent(
          SIPTransport sipTransport,
          SIPEndPoint outboundProxy,
          UASInviteTransaction uasTransaction,
          ISIPAccount sipAccount) :
            base(sipTransport, outboundProxy, uasTransaction, sipAccount)
        {
            IsB2B = true;
            base.CallCancelled += SIPServerUserAgent_CallCancelled;
        }

        private void SIPServerUserAgent_CallCancelled(ISIPServerUserAgent uas)
        {
            logger.LogDebug("B2BUserAgent server call was cancelled.");
            m_uac?.Cancel();
        }

        public SIPRequest Call(SIPCallDescriptor sipCallDescriptor)
        {
            return Call(sipCallDescriptor, null);
        }

        public SIPRequest Call(SIPCallDescriptor sipCallDescriptor, SIPEndPoint serverEndPoint)
        {
            m_uacCallDescriptor = sipCallDescriptor;

            m_uac = new SIPClientUserAgent(m_sipTransport, m_outboundProxy);
            m_uac.CallFailed += ClientCallFailed;
            m_uac.CallTrying += (uac, resp) => CallTrying?.Invoke(uac, resp);
            m_uac.CallRinging += (uac, resp) => CallRinging?.Invoke(uac, resp);
            m_uac.CallAnswered += ClientCallAnswered;

            return m_uac.Call(m_uacCallDescriptor);
        }

        public void Cancel()
        {
            logger.LogDebug("SIPB2BUserAgent Cancel.");
            m_uac.Cancel();

            var busyResp = SIPResponse.GetResponse(m_uasTransaction.TransactionRequest, SIPResponseStatusCodesEnum.BusyHere, null);
            m_uasTransaction.SendFinalResponse(busyResp);
        }

        private void ClientCallFailed(ISIPClientUserAgent uac, string error, SIPResponse errResponse)
        {
            if (!base.IsCancelled)
            {
                logger.LogDebug($"B2BUserAgent client call failed {error}.");

                var status = (errResponse != null) ? errResponse.Status : SIPResponseStatusCodesEnum.Decline;
                var errResp = SIPResponse.GetResponse(m_uasTransaction.TransactionRequest, status, errResponse?.ReasonPhrase);
                m_uasTransaction.SendFinalResponse(errResp);

                CallFailed?.Invoke(uac, error, errResponse);
            }
        }

        private void ClientCallAnswered(ISIPClientUserAgent uac, SIPResponse resp)
        {
            logger.LogDebug($"B2BUserAgent client call answered {resp.ShortDescription}.");

            if (resp.Status == SIPResponseStatusCodesEnum.Ok)
            {
                base.Answer(resp.Header.ContentType, resp.Body, SIPDialogueTransferModesEnum.NotAllowed);

                CallAnswered?.Invoke(uac, resp);
            }
            else
            {
                var failureResponse = SIPResponse.GetResponse(m_uasTransaction.TransactionRequest, resp.Status, resp.ReasonPhrase);
                m_uasTransaction.SendFinalResponse(failureResponse);

                CallFailed?.Invoke(uac, failureResponse.ReasonPhrase, failureResponse);
            }
        }
    }
}
