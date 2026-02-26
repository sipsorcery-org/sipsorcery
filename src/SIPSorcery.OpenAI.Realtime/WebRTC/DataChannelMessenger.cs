//-----------------------------------------------------------------------------
// Filename: DataChannelMessenger.cs
//
// Description: Manages messages to control or intiate actions on the OpenAI
// WebRTC session via data channel messages.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 31 May 2025  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using LanguageExt;
using LanguageExt.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Net;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using SIPSorcery.OpenAI.Realtime.Models;       

namespace SIPSorcery.OpenAI.Realtime;

/// <summary>
/// Facilitates sending OpenAI control messages (session updates, response requests, etc.) 
/// over the established WebRTC data channel to manage and orchestrate the OpenAI-powered media session.
/// </summary>
public class DataChannelMessenger
{
    private readonly WebRTCEndPoint _endpoint;
    private readonly ILogger _logger = NullLogger.Instance;

    public DataChannelMessenger(
        WebRTCEndPoint endpoint,
        ILogger<DataChannelMessenger> logger)
    {
        _endpoint = endpoint;
        _logger = logger ?? _logger;
    }

    public DataChannelMessenger(
        WebRTCEndPoint endpoint,
        ILogger? logger)
    {
        _endpoint = endpoint;
        _logger = logger ?? _logger;
    }

    /// <summary>
    /// Sends an OpenAI session‐update event over the data channel.
    /// </summary>
    public Either<Error, Unit> SendSessionUpdate(
        RealtimeVoicesEnum voice,
        string? instructions = null,
        RealtimeModelsEnum? model = null,
        TranscriptionModelEnum? transcriptionModel = null)
    {
        // Validate PeerConnection and retrieve the first data channel
        var dcResult = ValidateDataChannel();
        if (dcResult.IsLeft)
        {
            return dcResult.LeftToList().First();
        }

        var dc = dcResult.RightToList().First();

        var message = new RealtimeClientEventSessionUpdate
        {
            EventID = Guid.NewGuid().ToString(),
            Session = new RealtimeSession
            {
                Voice = voice,
                Instructions = instructions
            }
        };

        if (model != null)
        {
            message.Session.Model = model;
        }

        if (transcriptionModel != null)
        {
            message.Session.InputAudioTranscription = new RealtimeInputAudioTranscription
            {
                Model = transcriptionModel
            };
        }

        _logger.LogTrace(
            "Sending session‐update message on data channel “{Label}”: {Json}",
            dc.label,
            message.ToJson());

        dc.send(message.ToJson());
        return Unit.Default;
    }

    /// <summary>
    /// Sends an OpenAI response‐create event over the data channel.
    /// </summary>
    public Either<Error, Unit> SendResponseCreate(
        RealtimeVoicesEnum voice,
        string instructions)
    {
        // Validate PeerConnection and retrieve the first data channel
        var dcResult = ValidateDataChannel();
        if (dcResult.IsLeft)
        {
            return dcResult.LeftToList().First();
        }

        var dc = dcResult.RightToList().First();

        var message = new RealtimeClientEventResponseCreate
        {
            EventID = Guid.NewGuid().ToString(),
            Response = new RealtimeResponseCreateParams
            {
                Voice = voice,
                Instructions = instructions
            }
        };

        _logger.LogTrace(
            "Sending response‐create message on data channel “{Label}”: {Json}",
            dc.label,
            message.ToJson());

        dc.send(message.ToJson());
        return Unit.Default;
    }

    /// <summary>
    /// Handles any incoming raw data from the WebRTC data channel. Parses the JSON,
    /// turns it into the appropriate <see cref="RealtimeEventBase"/> subtype, and
    /// then invokes the endpoint's <see cref="IWebRTCEndPoint.OnDataChannelMessage"/> event.
    /// </summary>
    public void HandleIncomingData(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
    {
        string msgText = Encoding.UTF8.GetString(data);

        // Attempt a base‐type JSON deserialize
        RealtimeEventBase? baseEvent = JsonSerializer.Deserialize<RealtimeEventBase>(msgText, JsonOptions.Default);

        if (baseEvent == null)
        {
            _logger.LogWarning("Received non‐OpenAI event on data channel: {Payload}", msgText);
            return;
        }

        if (baseEvent is RealtimeUnknown unknownEvent)
        {
            _logger.LogWarning("Unexpected event type '{Type}' received on OpenAI data channel.", unknownEvent.OriginalType);
        }
        else
        {
            _endpoint.InvokeOnDataChannelMessage(dc, baseEvent);
        }
    }

    /// <summary>
    /// Validates that the PeerConnection is non-null and connected, and that at least one data channel exists.
    /// Returns Either an Error describing the failure, or the first RTCDataChannel.
    /// </summary>
    private Either<Error, RTCDataChannel> ValidateDataChannel()
    {
        return _endpoint.PeerConnection.Match<Either<Error, RTCDataChannel>>(
               pc =>
               {
                   if (pc.connectionState != RTCPeerConnectionState.connected)
                   {
                       return Error.New("Peer connection not connected.");
                   }

                   var dc = pc.DataChannels
                              .FirstOrDefault(x => x.label == WebRTCEndPoint.OPENAI_DATACHANNEL_NAME);
                   if (dc == null)
                   {
                       return Error.New("No OpenAI data channel available.");
                   }

                   return dc;
               },
               () => Error.New("Peer connection not established.")
           );
    }
}
