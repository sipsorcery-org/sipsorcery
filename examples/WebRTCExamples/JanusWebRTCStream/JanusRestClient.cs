//-----------------------------------------------------------------------------
// Filename: JanusRestClient.cs
//
// Description: Minimal client to connect to the Janus WebRTC Server's REST
// interface and establish an Echo WebRTC connection.
//
// The HTTP REST interface is defined at:
// https://janus.conf.meetecho.com/docs/rest.html
//
// The Echo plugin operations are defined at:
// https://janus.conf.meetecho.com/docs/echotest.html
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 04 Feb 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;
using RestSharp.Serializers.NewtonsoftJson;

namespace demo
{
    public class JanusRestClient
    {
        private readonly string _serverUrl;
        private readonly ILogger _logger;
        private ulong _sessionID;
        private CancellationToken _ct; 

        public event Action<JanusResponse> OnJanusEvent;

        public JanusRestClient(string serverURL,
             ILogger logger,
            CancellationToken ct)
        {
            _serverUrl = serverURL;
            _logger = logger;
            _ct = ct;
        }

        /// <summary>
        /// Gets generic server properties from the Janus instance.
        /// </summary>
        public async Task<ServerInfo> GetServerInfo()
        {
            _logger.LogDebug("Creating Janus session...");

            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            var infoReq = new RestRequest("info", DataFormat.Json);
            return await client.GetAsync<ServerInfo>(infoReq, _ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a new session with the Janus server. A session is required for 
        /// most operations including being able to use a plugin to create a WebRTC
        /// session.
        /// </summary>
        public async Task StartSession()
        {
            _sessionID = await CreateSession(_ct).ConfigureAwait(false);

            if(_sessionID == 0)
            {
                throw new ApplicationException("Janus session creation failed.");
            }
            else
            {
                _ = Task.Factory.StartNew(StartLongPoll, TaskCreationOptions.LongRunning);
            }
        }

        /// <summary>
        /// Creates a new session with the Echo plugin. This will request Janus to create a 
        /// new WebRTC session. The SDP answer will be supplied as a response on the HTTP long
        /// poll connection.
        /// </summary>
        /// <param name="sdpOffer">The SDP offer from a the WebRTC peer that wants to connect
        /// to the Echo plugin.</param>
        public async Task StartEcho(string sdpOffer)
        {
            var pluginID = await AttachPlugin(JanusPlugins.ECHO_TEST).ConfigureAwait(false);

            if (pluginID == 0)
            {
                throw new ApplicationException("Janus session failed to create echo plugin.");
            }
            else
            {
                await StartEcho(pluginID, sdpOffer).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Closes and destroys a Janus session allowing the server to free any
        /// resources or plugins attached to it.
        /// </summary>
        public async Task DestroySession()
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            var destroyReqBody = new JanusRequest { janus = JanusOperationsEnum.destroy };
            var destroyReq = new RestRequest(_sessionID.ToString(), Method.POST, DataFormat.Json);
            destroyReq.AddJsonBody(destroyReqBody);
            var destroyResp = await client.ExecutePostAsync<JanusResponse>(destroyReq).ConfigureAwait(false);

            _logger.LogDebug($"Destroy response: {destroyResp.Data.janus}.");
        }

        /// <summary>
        /// Attempts to create a new Janus session. This is the first step to do anything
        /// with the Janus WebRTC or other features.
        /// </summary>
        /// <returns>A non-zero session ID. A zero value indicates a failure.</returns>
        private async Task<ulong> CreateSession(CancellationToken ct)
        {
            _logger.LogDebug("Creating Janus session...");

            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            // Create session.
            var createSessionReq = new JanusRequest { janus = JanusOperationsEnum.create };
            var sessReq = new RestRequest(string.Empty, Method.POST, DataFormat.Json);
            sessReq.AddJsonBody(createSessionReq);
            var sessResp = await client.ExecutePostAsync<JanusResponse>(sessReq, ct).ConfigureAwait(false);

            ulong sessionID = sessResp.Data.data.id;

            _logger.LogDebug($"Result={sessResp.Data.janus}.");
            _logger.LogDebug($"Transaction={sessResp.Data.transaction}.");
            _logger.LogDebug($"SessionID={sessionID}.");

            return sessionID;
        }

        /// <summary>
        /// Janus requires a HTTP long poll mechanism to let it know that the client is still active.
        /// In addition any session events or responses to async REST requests will be provided as
        /// responses to the long poll GET request.
        /// </summary>
        private async Task StartLongPoll()
        {
            try
            {
                var longPollClient = new RestClient(_serverUrl);
                longPollClient.UseNewtonsoftJson();

                while (!_ct.IsCancellationRequested)
                {
                    var getEventReq = new RestRequest(_sessionID.ToString());

                    _logger.LogDebug($"Sending long poll GET to {_serverUrl}{_sessionID}.");
                    var getEventResp = await longPollClient.GetAsync<JanusResponse>(getEventReq, _ct);

                    _logger.LogDebug($"get event result={getEventResp.janus}.");

                    if (getEventResp.JanusOp != JanusOperationsEnum.keepalive)
                    {
                        OnJanusEvent?.Invoke(getEventResp);
                    }
                }
            }
            catch (TaskCanceledException)
            { }
            catch (Exception excp)
            {
                _logger.LogError($"Exception processing long poll. {excp}");
            }

            _logger.LogDebug("Long poll thread exiting.");
        }

        /// <summary>
        /// Requests Janus to attach a new instance of a plugin to the current session.
        /// </summary>
        /// <param name="pluginType">The string ID of the plugin to attach.</param>
        /// <returns>The ID of the plugin session. A zero value indicates a failure.</returns>
        private async Task<ulong> AttachPlugin(string pluginType)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            _logger.LogDebug($"Sending attach to {pluginType}.");
            var attachPluginReqBody = new AttachPluginRequest(pluginType);
            _logger.LogDebug(JsonConvert.SerializeObject(attachPluginReqBody));
            var attachReq = new RestRequest(_sessionID.ToString(), Method.POST, DataFormat.Json);
            attachReq.AddJsonBody(attachPluginReqBody);
            var attachResp = await client.ExecutePostAsync<JanusResponse>(attachReq, _ct).ConfigureAwait(false);

            _logger.LogDebug($"Attach response result={attachResp.Data.janus}.");
            _logger.LogDebug($"Attach response plugin id={attachResp.Data.data.id}.");

            return attachResp.Data.data.id;
        }

        /// <summary>
        /// Sends the SDP offer to the Echo plugin instance which results in Janus starting the 
        /// WebRTC connection.
        /// </summary>
        /// <param name="echoPluginID">THe ID of the echo plugin instance.</param>
        /// <param name="offer">The WebRTC SDP offer to send to the Echo plugin.</param>
        private async Task StartEcho(ulong echoPluginID, string offer)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            // Send SDP offer to janus streaming plugin.
            _logger.LogDebug("Send SDP offer to Janus echo test plugin.");
            var echoTestReqBody = new EchoTestRequest("offer", offer);
            //_logger.LogDebug(JsonConvert.SerializeObject(echoTestReqBody));
            var echoOfferReq = new RestRequest($"{_sessionID}/{echoPluginID}", Method.POST, DataFormat.Json);
            echoOfferReq.AddJsonBody(echoTestReqBody);
            var offerResp = await client.ExecutePostAsync<JanusResponse>(echoOfferReq, _ct).ConfigureAwait(false);

            if (offerResp.Data.JanusOp == JanusOperationsEnum.error)
            {
                var errResp = offerResp.Data;
                _logger.LogWarning($"Error, code={errResp.error.code}, reason={errResp.error.reason}.");
            }
            else
            {
                _logger.LogDebug($"Offer response result={offerResp.Data.janus}.");
            }
        }
    }
}
