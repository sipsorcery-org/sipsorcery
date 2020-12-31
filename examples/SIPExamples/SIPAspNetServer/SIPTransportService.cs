//-----------------------------------------------------------------------------
// Filename: SIPTransportService.cs
//
// Description: This class is designed to act as a singleton in an ASP.Net
// server application to manage the SIP transport. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPAspNetServer.DataAccess;

namespace SIPAspNetServer
{
    /// <summary>
    /// A hosted service to manage a SIP transport layer. This class is designed to be a long running
    /// singleton. Once created the SIP transport channels listen for incoming messages.
    /// </summary>
    public class SIPTransportService : IHostedService
    {
        public const int DEFAULT_SIP_LISTEN_PORT = 5060;
        public const int MAX_REGISTRAR_BINDINGS = 10;

        private readonly ILogger<SIPTransportService> Logger;
        private readonly IConfiguration Configuration;
        //private readonly SIPAssetsContext SIPAssetsCtx;

        private SIPTransport _sipTransport;
        private RegistrarCore _registrarCore;
        private SIPRegistrarBindingsManager _bindingsManager;

        public SIPTransportService(ILogger<SIPTransportService> logger, IConfiguration config)
        {
            Logger = logger;
            Configuration = config;

            _sipTransport = new SIPTransport();
            _bindingsManager = new SIPRegistrarBindingsManager(MAX_REGISTRAR_BINDINGS);
            _registrarCore = new RegistrarCore(_sipTransport, false, false);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("SIP hosted service starting...");

            int listenPort = Configuration.GetValue<int>("SIPListenPort", DEFAULT_SIP_LISTEN_PORT);
            _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, listenPort)));

            var listeningEP = _sipTransport.GetSIPChannels().First().ListeningSIPEndPoint;
            Logger.LogInformation($"SIP transport listening on {listeningEP}.");

            EnableTraceLogs(_sipTransport, true);

            _bindingsManager.Start();
            _registrarCore.Start(1);

            _sipTransport.SIPTransportRequestReceived += OnRequest;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("SIP hosted service stopping...");

            _registrarCore.Stop = true;
            _bindingsManager.Stop();

            _sipTransport?.Shutdown();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Because this is a server user agent the SIP transport must start listening for client user agents.
        /// </summary>
        private async Task OnRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                if (sipRequest.Header.From != null &&
                sipRequest.Header.From.FromTag != null &&
                sipRequest.Header.To != null &&
                sipRequest.Header.To.ToTag != null)
                {
                    // This is an in-dialog request that will be handled directly by a user agent instance.
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    Logger.LogInformation($"Incoming call request: {localSIPEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");

                    //SIPUserAgent ua = new SIPUserAgent(_sipTransport, null);
                    //ua.OnCallHungup += OnHangup;
                    //ua.ServerCallCancelled += (uas) => Log.LogDebug("Incoming call cancelled by remote party.");
                    //ua.OnDtmfTone += (key, duration) => OnDtmfTone(ua, key, duration);
                    //ua.OnRtpEvent += (evt, hdr) => Log.LogDebug($"rtp event {evt.EventID}, duration {evt.Duration}, end of event {evt.EndOfEvent}, timestamp {hdr.Timestamp}, marker {hdr.MarkerBit}.");
                    //ua.OnTransactionTraceMessage += (tx, msg) => Log.LogDebug($"uas tx {tx.TransactionId}: {msg}");
                    //ua.ServerCallRingTimeout += (uas) =>
                    //{
                    //    Log.LogWarning($"Incoming call timed out in {uas.ClientTransaction.TransactionState} state waiting for client ACK, terminating.");
                    //    ua.Hangup();
                    //};

                    //var uas = ua.AcceptCall(sipRequest);
                    //var rtpSession = CreateRtpSession(ua, sipRequest.URI.User);

                    //// Insert a brief delay to allow testing of the "Ringing" progress response.
                    //// Without the delay the call gets answered before it can be sent.
                    //await Task.Delay(500);

                    //await ua.Answer(uas, rtpSession);

                    //if (ua.IsCallActive)
                    //{
                    //    await rtpSession.Start();
                    //    _calls.TryAdd(ua.Dialogue.CallId, ua);
                    //}
                }
                else if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    SIPResponse byeResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    await _sipTransport.SendResponseAsync(byeResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.SUBSCRIBE)
                {
                    SIPResponse notAllowededResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                    await _sipTransport.SendResponseAsync(notAllowededResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.REGISTER)
                {
                    _registrarCore.AddRegisterRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                }
                else if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
                {
                    SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await _sipTransport.SendResponseAsync(optionsResponse);
                }
                else
                {
                    var notAllowedResp = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                    await _sipTransport.SendResponseAsync(notAllowedResp);
                }
            }
            catch (Exception reqExcp)
            {
                Logger.LogWarning($"Exception handling {sipRequest.Method}. {reqExcp.Message}");
            }
        }

        /// <summary>
        /// Enable detailed SIP log messages.
        /// </summary>
        private void EnableTraceLogs(SIPTransport sipTransport, bool fullSIP)
        {
            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Logger.LogDebug($"Request received: {localEP}<-{remoteEP}");

                if (!fullSIP)
                {
                    Logger.LogDebug(req.StatusLine);
                }
                else
                {
                    Logger.LogDebug(req.ToString());
                }
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Logger.LogDebug($"Request sent: {localEP}->{remoteEP}");

                if (!fullSIP)
                {
                    Logger.LogDebug(req.StatusLine);
                }
                else
                {
                    Logger.LogDebug(req.ToString());
                }
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Logger.LogDebug($"Response received: {localEP}<-{remoteEP}");

                if (!fullSIP)
                {
                    Logger.LogDebug(resp.ShortDescription);
                }
                else
                {
                    Logger.LogDebug(resp.ToString());
                }
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Logger.LogDebug($"Response sent: {localEP}->{remoteEP}");

                if (!fullSIP)
                {
                    Logger.LogDebug(resp.ShortDescription);
                }
                else
                {
                    Logger.LogDebug(resp.ToString());
                }
            };

            sipTransport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
            {
                Logger.LogDebug($"Request retransmit {count} for request {req.StatusLine}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };

            sipTransport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
            {
                Logger.LogDebug($"Response retransmit {count} for response {resp.ShortDescription}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };
        }
    }
}
