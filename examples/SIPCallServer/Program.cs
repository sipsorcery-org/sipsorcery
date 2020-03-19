//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example SIP server program to accept and initiate calls.
//
// Author(s):
// Aaron Clauson  (aaron@sipsorcery.com)
// 
// History:
// 19 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    struct SIPRegisterAccount
    {
        public string Username;
        public string Password;
        public string Domain;
        public int Expiry;

        public SIPRegisterAccount(string username, string password, string domain, int expiry)
        {
            Username = username;
            Password = password;
            Domain = domain;
            Expiry = expiry;
        }
    }

    class Program
    {
        private static string DEFAULT_CALL_DESTINATION = "sip:*61@192.168.11.48";
        private static string DEFAULT_TRANSFER_DESTINATION = "sip:*61@192.168.11.48";
        private static int SIP_LISTEN_PORT = 5060;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        /// <summary>
        /// The set of SIP accounts available for registering and/or authenticating calls.
        /// </summary>
        private static readonly List<SIPRegisterAccount> _sipAccounts = new List<SIPRegisterAccount>
        {
            new SIPRegisterAccount( "softphonesample", "password", "sipsorcery.com", 120)
        };

        private static SIPTransport _sipTransport;

        /// <summary>
        /// Keeps track of the current active calls. It includes both received and placed calls.
        /// </summary>
        private static ConcurrentDictionary<string, SIPUserAgent> _calls = new ConcurrentDictionary<string, SIPUserAgent>();

        /// <summary>
        /// Keeps track of the SIP account registrations.
        /// </summary>
        private static ConcurrentDictionary<string, SIPRegistrationUserAgent> _registrations = new ConcurrentDictionary<string, SIPRegistrationUserAgent>();

        static async Task Main(string[] args)
        {
            Console.WriteLine("SIPSorcery SIP Call Server example.");
            Console.WriteLine("Press 'c' to place a call to the default destination.");
            Console.WriteLine("Press 'd' to send a random DTMF tone to the newest call.");
            Console.WriteLine("Press 'h' to hangup the oldest call.");
            Console.WriteLine("Press 'l' to list current calls.");
            Console.WriteLine("Press 'r' to list current registrations.");
            Console.WriteLine("Press 't' to transfer the newest call to the default destination.");
            Console.WriteLine("Press 'q' to quit.");

            AddConsoleLogger();

            // Set up a default SIP transport.
            _sipTransport = new SIPTransport();
            _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));
            EnableTraceLogs(_sipTransport);

            _sipTransport.SIPTransportRequestReceived += OnRequest;

            // Uncomment to enable registrations.
            //StartRegistrations(_sipTransport, _sipAccounts);

            CancellationTokenSource exitCts = new CancellationTokenSource();
            await Task.Run(() => OnKeyPress(exitCts.Token));

            Log.LogInformation("Exiting...");

            SIPSorcery.Net.DNSManager.Stop();

            if (_sipTransport != null)
            {
                Log.LogInformation("Shutting down SIP transport...");
                _sipTransport.Shutdown();
            }
        }

        /// <summary>
        /// Process user key presses.
        /// </summary>
        /// <param name="exit">The cancellation token to set if the user requests to quit the application.</param>
        private static async Task OnKeyPress(CancellationToken exit)
        {
            try
            {
                while (!exit.WaitHandle.WaitOne(0))
                {
                    var keyProps = Console.ReadKey();

                    if (keyProps.KeyChar == 'c')
                    {
                        // Place an outgoing call.
                        var ua = new SIPUserAgent(_sipTransport, null);
                        ua.ClientCallTrying += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
                        ua.ClientCallRinging += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
                        ua.ClientCallFailed += (uac, err) => Log.LogWarning($"{uac.CallDescriptor.To} Failed: {err}");
                        ua.ClientCallAnswered += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                        ua.OnDtmfTone += (key, duration) => OnDtmfTone(ua, key, duration);
                        ua.OnCallHungup += OnHangup;

                        var callResult = await ua.Call(DEFAULT_CALL_DESTINATION, null, null, new RtpAudioSession(AddressFamily.InterNetwork));
                        if (callResult)
                        {
                            _calls.TryAdd(ua.Dialogue.CallId, ua);
                        }
                    }
                    else if (keyProps.KeyChar == 'd')
                    {
                        if (_calls.Count == 0)
                        {
                            Log.LogWarning("There are no active calls.");
                        }
                        else
                        {
                            var newestCall = _calls.OrderByDescending(x => x.Value.Dialogue.Inserted).First();
                            byte randomDtmf = (byte)Crypto.GetRandomInt(0, 15);
                            Log.LogInformation($"Sending DTMF {randomDtmf} to {newestCall.Key}.");
                            await newestCall.Value.SendDtmf(randomDtmf);
                        }
                    }
                    else if (keyProps.KeyChar == 'h')
                    {
                        if (_calls.Count == 0)
                        {
                            Log.LogWarning("There are no active calls.");
                        }
                        else
                        {
                            var oldestCall = _calls.OrderBy(x => x.Value.Dialogue.Inserted).First();
                            Log.LogInformation($"Hanging up call {oldestCall.Key}.");
                            oldestCall.Value.OnCallHungup -= OnHangup;
                            oldestCall.Value.Hangup();
                            _calls.TryRemove(oldestCall.Key, out _);
                        }
                    }
                    else if (keyProps.KeyChar == 'l')
                    {
                        if (_calls.Count == 0)
                        {
                            Log.LogInformation("There are no active calls.");
                        }
                        else
                        {
                            Log.LogInformation("Current call list:");
                            foreach (var call in _calls)
                            {
                                Log.LogInformation($"{call.Key}: {call.Value.Dialogue.RemoteTarget}");
                            }
                        }
                    }
                    else if (keyProps.KeyChar == 'r')
                    {
                        if (_registrations.Count == 0)
                        {
                            Log.LogInformation("There are no active registrations.");
                        }
                        else
                        {
                            Log.LogInformation("Current registration list:");
                            foreach (var registration in _registrations)
                            {
                                Log.LogInformation($"{registration.Key}: is registered {registration.Value.IsRegistered}, last attempt at {registration.Value.LastRegisterAttemptAt}");
                            }
                        }
                    }
                    else if (keyProps.KeyChar == 't')
                    {
                        if (_calls.Count == 0)
                        {
                            Log.LogWarning("There are no active calls.");
                        }
                        else
                        {
                            var newestCall = _calls.OrderByDescending(x => x.Value.Dialogue.Inserted).First();
                            Log.LogInformation($"Transferring call {newestCall.Key} to {DEFAULT_TRANSFER_DESTINATION}.");
                            bool transferResult = await newestCall.Value.BlindTransfer(SIPURI.ParseSIPURI(DEFAULT_TRANSFER_DESTINATION), TimeSpan.FromSeconds(3), exit);

                            if(transferResult)
                            {
                                Log.LogInformation($"Transferring succeeded.");

                                newestCall.Value.OnCallHungup -= OnHangup;
                                newestCall.Value.Hangup();
                                if(!_calls.TryRemove(newestCall.Key, out _))
                                {
                                    Log.LogError("Failed to remove hungup call.");
                                }
                            }
                            else
                            {
                                Log.LogWarning($"Transfer attempt failed.");
                            }
                        }
                    }
                    else if (keyProps.KeyChar == 'q')
                    {
                        // Quit application.
                        Log.LogInformation("Quitting");
                        break;
                    }
                }
            }
            catch (Exception excp)
            {
                Log.LogError($"Exception OnKeyPress. {excp.Message}.");
            }
        }

        /// <summary>
        /// Event handler for receiving a DTMF tone.
        /// </summary>
        /// <param name="ua">The user agent that received the DTMF tone.</param>
        /// <param name="key">The DTMF tone.</param>
        /// <param name="duration">The duration in milliseconds of the tone.</param>
        private static void OnDtmfTone(SIPUserAgent ua, byte key, int duration)
        {
            string callID = ua.Dialogue.CallId;
            Log.LogInformation($"Call {callID} received DTMF tone {key}, duration {duration}ms.");
        }

        // Because this is a server user agent the SIP transport must start listening for client user agents.
        private static async Task OnRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    Log.LogInformation($"Incoming call request: {localSIPEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");

                    SIPUserAgent ua = new SIPUserAgent(_sipTransport, null);
                    ua.OnCallHungup += OnHangup;
                    ua.ServerCallCancelled += (uas) => Log.LogDebug("Incoming call cancelled by remote party.");
                    ua.OnDtmfTone += (key, duration) => OnDtmfTone(ua, key, duration);

                    var uas = ua.AcceptCall(sipRequest);
                    RtpAudioSession rtpAudio = new RtpAudioSession(AddressFamily.InterNetwork);
                    await ua.Answer(uas, rtpAudio);

                    if(ua.IsCallActive)
                    {
                        _calls.TryAdd(ua.Dialogue.CallId, ua);
                    }
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
                else if (sipRequest.Method == SIPMethodsEnum.OPTIONS || sipRequest.Method == SIPMethodsEnum.REGISTER)
                {
                    SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await _sipTransport.SendResponseAsync(optionsResponse);
                }
            }
            catch (Exception reqExcp)
            {
                Log.LogWarning($"Exception handling {sipRequest.Method}. {reqExcp.Message}");
            }
        }

        /// <summary>
        /// Remove call from the active calls list.
        /// </summary>
        /// <param name="dialogue">The dialogue that was hungup.</param>
        private static void OnHangup(SIPDialogue dialogue)
        {
            // If the dialogue is null it means the hangup was initiated from our end.
            if (dialogue != null)
            {
                string callID = dialogue.CallId;
                Log.LogInformation($"Call hungup by remote party {callID}.");
                if (_calls.ContainsKey(callID))
                {
                    _calls.TryRemove(callID, out _);
                }
            }
        }

        /// <summary>
        /// Starts a registration agent for each of the supplied SIP accounts.
        /// </summary>
        /// <param name="sipTransport">The SIP transport to use for the registrations.</param>
        /// <param name="sipAccounts">The list of SIP accounts to create a registration for.</param>
        private static void StartRegistrations(SIPTransport sipTransport, List<SIPRegisterAccount> sipAccounts)
        {
            foreach(var sipAccount in sipAccounts)
            {
                var regUserAgent = new SIPRegistrationUserAgent(sipTransport, sipAccount.Username, sipAccount.Password, sipAccount.Domain, sipAccount.Expiry);

                // Event handlers for the different stages of the registration.
                regUserAgent.RegistrationFailed += (uri, err) => Log.LogError($"{uri.ToString()}: {err}");
                regUserAgent.RegistrationTemporaryFailure += (uri, msg) => Log.LogWarning($"{uri.ToString()}: {msg}");
                regUserAgent.RegistrationRemoved += (uri) => Log.LogError($"{uri.ToString()} registration failed.");
                regUserAgent.RegistrationSuccessful += (uri) => Log.LogInformation($"{uri.ToString()} registration succeeded.");

                // Start the thread to perform the initial registration and then periodically resend it.
                regUserAgent.Start();

                _registrations.TryAdd($"{sipAccount.Username}@{sipAccount.Domain}", regUserAgent);
            }
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console(theme: AnsiConsoleTheme.Code)
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }

        /// <summary>
        /// Enable detailed SIP log messages.
        /// </summary>
        private static void EnableTraceLogs(SIPTransport sipTransport)
        {
            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request received: {localEP}<-{remoteEP}");
                Log.LogDebug(req.ToString());
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request sent: {localEP}->{remoteEP}");
                Log.LogDebug(req.ToString());
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response received: {localEP}<-{remoteEP}");
                Log.LogDebug(resp.ToString());
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response sent: {localEP}->{remoteEP}");
                Log.LogDebug(resp.ToString());
            };

            sipTransport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
            {
                Log.LogDebug($"Request retransmit {count} for request {req.StatusLine}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };

            sipTransport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
            {
                Log.LogDebug($"Response retransmit {count} for response {resp.ShortDescription}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };
        }
    }
}
