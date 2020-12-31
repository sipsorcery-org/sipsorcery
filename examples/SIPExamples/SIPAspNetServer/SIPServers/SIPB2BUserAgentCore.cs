// ============================================================================
// FileName: SIPB2BUserAgentCore.cs
//
// Description:
// SIP server core that handles incoming call requests by acting as a 
// Back-to-Back User Agent (B2BUA).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 31 Dec 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;

namespace SIPAspNetServer
{
    /// <summary>
    /// This class acts as a server agent that processes incoming calls (INVITE requests)
    /// by acting as a Back-to-Back User Agent (B2BUA). 
    /// </summary>
    /// <remarks>
    /// The high level method of operations is:
    /// - Wait for an INVITE request from a User Agent Client (UAC),
    /// - Answer UAC by creating a User Agent Server (UAS),
    /// - Apply business logic to determine forwarding destination fof call from UAC,
    /// - Create a new UAC and "link" it to the UAS,
    /// - Start the UAC call to the forward destination.
    /// </remarks>
    public class SIPB2BUserAgentCore
    {
        private const int MAX_INVITE_QUEUE_SIZE = 5;
        private const int MAX_PROCESS_INVITE_SLEEP = 10000;
        private const string B2BUA_THREAD_NAME_PREFIX = "sipb2bua-core";

        private readonly ILogger Logger = SIPSorcery.LogFactory.CreateLogger<RegistrarCore>();

        private AutoResetEvent _inviteARE = new AutoResetEvent(false);
        private ConcurrentQueue<UASInviteTransaction> _inviteQueue = new ConcurrentQueue<UASInviteTransaction>();
        private bool _exit = false;

        private SIPTransport _sipTransport;

        public SIPB2BUserAgentCore(SIPTransport sipTransport)
        {
            _sipTransport = sipTransport;
        }

        public void Start(int threadCount)
        {
            Logger.LogInformation($"SIPB2BUserAgentCore starting with {threadCount} threads.");

            for (int index = 1; index <= threadCount; index++)
            {
                string threadSuffix = index.ToString();
                ThreadPool.QueueUserWorkItem(delegate { ProcessInviteRequest(B2BUA_THREAD_NAME_PREFIX + threadSuffix); });
            }
        }

        public void Stop()
        {
            if (!_exit)
            {
                _exit = true;
                Logger.LogInformation("SIPB2BUserAgentCore Stop called.");
            }
        }

        public void AddInviteRequest(SIPRequest inviteRequest)
        {
            if (inviteRequest.Method != SIPMethodsEnum.INVITE)
            {
                SIPResponse notSupportedResponse = SIPResponse.GetResponse(inviteRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, "Invite requests only");
                _sipTransport.SendResponseAsync(notSupportedResponse).Wait();
            }
            else
            {
                if (_inviteQueue.Count < MAX_INVITE_QUEUE_SIZE)
                {
                    UASInviteTransaction uasTransaction = new UASInviteTransaction(_sipTransport, inviteRequest, null);
                    var trying = SIPResponse.GetResponse(inviteRequest, SIPResponseStatusCodesEnum.Trying, null);
                    uasTransaction.SendProvisionalResponse(trying).Wait();

                    _inviteQueue.Enqueue(uasTransaction);
                }
                else
                {
                    Logger.LogWarning($"Invite queue exceeded max queue size {MAX_INVITE_QUEUE_SIZE} overloaded response sent.");
                    SIPResponse overloadedResponse = SIPResponse.GetResponse(inviteRequest, SIPResponseStatusCodesEnum.TemporarilyUnavailable, "B2BUA overloaded, please try again shortly");
                    _sipTransport.SendResponseAsync(overloadedResponse).Wait();
                }

                _inviteARE.Set();
            }
        }

        private void ProcessInviteRequest(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                while (!_exit)
                {
                    if (_inviteQueue.Count > 0)
                    {
                        try
                        {
                            if (_inviteQueue.TryDequeue(out var uasTransaction))
                            {
                                Forward(uasTransaction);
                            }
                        }
                        catch (Exception invExcp)
                        {
                            Logger.LogError("Exception ProcessInviteRequest Job. " + invExcp.Message);
                        }
                    }
                    else
                    {
                        _inviteARE.WaitOne(MAX_PROCESS_INVITE_SLEEP);
                    }
                }

                Logger.LogWarning("ProcessInviteRequest thread " + Thread.CurrentThread.Name + " stopping.");
            }
            catch (Exception excp)
            {
                Logger.LogError("Exception ProcessInviteRequest (" + Thread.CurrentThread.Name + "). " + excp);
            }
        }

        private void Forward(UASInviteTransaction uasTransaction)
        {
            var notFoundResp = SIPResponse.GetResponse(uasTransaction.TransactionRequest, SIPResponseStatusCodesEnum.NotFound, null);
            uasTransaction.SendFinalResponse(notFoundResp);
        }
    }
}
