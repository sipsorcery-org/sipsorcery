//-----------------------------------------------------------------------------
// Filename: SIPHostedService.cs
//
// Description: This class is designed to act as a singleton in an ASP.Net
// server application to manage the SIP transport and server cores. 
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using demo.DataAccess;

namespace demo
{
    /// <summary>
    /// A hosted service to manage a SIP transport layer. This class is designed to be a long running
    /// singleton. Once created the SIP transport channels listen for incoming messages.
    /// </summary>
    public class SIPHostedService : IHostedService
    {
        public const int DEFAULT_SIP_LISTEN_PORT = 5060;
        public const int MAX_REGISTRAR_BINDINGS = 10;
        public const int REGISTRAR_CORE_WORKER_THREADS = 1;
        public const int B2BUA_CORE_WORKER_THREADS = 1;

        private readonly ILogger<SIPHostedService> _logger;
        private readonly IConfiguration _config;

        private SIPTransport _sipTransport;
        private SIPDialPlan _sipDialPlan;
        private RegistrarCore _registrarCore;
        private SIPRegistrarBindingsManager _bindingsManager;
        private SIPB2BUserAgentCore _b2bUserAgentCore;
        private SIPCallManager _sipCallManager;
        private CDRDataLayer _cdrDataLayer;

        public SIPHostedService(
            ILogger<SIPHostedService> logger,
            IConfiguration config,
            IDbContextFactory<SIPAssetsDbContext> dbContextFactory)
        {
            _logger = logger;
            _config = config;

            _sipTransport = new SIPTransport();

            // Load dialplan script and make sure it can be compiled.
            _sipDialPlan = new SIPDialPlan();
            _sipDialPlan.Load();

            _bindingsManager = new SIPRegistrarBindingsManager(new SIPRegistrarBindingDataLayer(dbContextFactory), MAX_REGISTRAR_BINDINGS);
            _registrarCore = new RegistrarCore(_sipTransport, false, false, _bindingsManager, dbContextFactory);
            _b2bUserAgentCore = new SIPB2BUserAgentCore(_sipTransport, dbContextFactory, _sipDialPlan);
            _sipCallManager = new SIPCallManager(_sipTransport, null, dbContextFactory);
            _cdrDataLayer = new CDRDataLayer(dbContextFactory);

            SIPCDR.CDRCreated += _cdrDataLayer.Add;
            SIPCDR.CDRAnswered += _cdrDataLayer.Update;
            SIPCDR.CDRUpdated += _cdrDataLayer.Update;
            SIPCDR.CDRHungup += _cdrDataLayer.Update;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("SIP hosted service starting...");

            // Get application config settings.
            int listenPort = _config.GetValue<int>("SIPListenPort", DEFAULT_SIP_LISTEN_PORT);
            string publicIPAddress = _config.GetValue<string>("SIPPublicIPAddress", null);

            if(IPAddress.TryParse(publicIPAddress, out var ipAddr))
            {
                _sipTransport.ContactHost = ipAddr.ToString();
                _logger.LogInformation($"SIP transport contact address set to {_sipTransport.ContactHost}.");
            }

            _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, listenPort)));

            var listeningEP = _sipTransport.GetSIPChannels().First().ListeningSIPEndPoint;
            _logger.LogInformation($"SIP transport listening on {listeningEP}.");

            EnableTraceLogs(_sipTransport, true);

            _bindingsManager.Start();
            _registrarCore.Start(REGISTRAR_CORE_WORKER_THREADS);
            _b2bUserAgentCore.Start(B2BUA_CORE_WORKER_THREADS);

            _sipTransport.SIPTransportRequestReceived += OnRequest;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("SIP hosted service stopping...");

            _b2bUserAgentCore.Stop();
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
                    _sipCallManager.ProcessInDialogueRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                }
                else
                {
                    switch (sipRequest.Method)
                    {
                        case SIPMethodsEnum.BYE:
                        case SIPMethodsEnum.CANCEL:
                            // BYE's and CANCEL's should always have dialog fields set.
                            SIPResponse badResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BadRequest, null);
                            await _sipTransport.SendResponseAsync(badResponse);
                            break;

                        case SIPMethodsEnum.INVITE:
                            _logger.LogInformation($"Incoming call request: {localSIPEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");
                            _b2bUserAgentCore.AddInviteRequest(sipRequest);
                            break;

                        case SIPMethodsEnum.OPTIONS:
                            SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                            await _sipTransport.SendResponseAsync(optionsResponse);
                            break;

                        case SIPMethodsEnum.REGISTER:
                            _registrarCore.AddRegisterRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                            break;

                        default:
                            var notAllowedResp = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                            await _sipTransport.SendResponseAsync(notAllowedResp);
                            break;
                    }
                }
            }
            catch (Exception reqExcp)
            {
                _logger.LogWarning($"Exception handling {sipRequest.Method}. {reqExcp.Message}");
            }
        }

        /// <summary>
        /// Enable detailed SIP log messages.
        /// </summary>
        private void EnableTraceLogs(SIPTransport sipTransport, bool fullSIP)
        {
            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                _logger.LogDebug($"Request received: {localEP}<-{remoteEP}");

                if (!fullSIP)
                {
                    _logger.LogDebug(req.StatusLine);
                }
                else
                {
                    _logger.LogDebug(req.ToString());
                }
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                _logger.LogDebug($"Request sent: {localEP}->{remoteEP}");

                if (!fullSIP)
                {
                    _logger.LogDebug(req.StatusLine);
                }
                else
                {
                    _logger.LogDebug(req.ToString());
                }
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                _logger.LogDebug($"Response received: {localEP}<-{remoteEP}");

                if (!fullSIP)
                {
                    _logger.LogDebug(resp.ShortDescription);
                }
                else
                {
                    _logger.LogDebug(resp.ToString());
                }
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                _logger.LogDebug($"Response sent: {localEP}->{remoteEP}");

                if (!fullSIP)
                {
                    _logger.LogDebug(resp.ShortDescription);
                }
                else
                {
                    _logger.LogDebug(resp.ToString());
                }
            };

            sipTransport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
            {
                _logger.LogDebug($"Request retransmit {count} for request {req.StatusLine}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };

            sipTransport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
            {
                _logger.LogDebug($"Response retransmit {count} for response {resp.ShortDescription}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };
        }
    }
}
