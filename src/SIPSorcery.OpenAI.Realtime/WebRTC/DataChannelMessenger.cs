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
    /// Unrecognised event types are surfaced as <see cref="RealtimeUnknown"/> with the
    /// original JSON intact, so callers can handle events SIPSorcery does not yet have
    /// a typed class for (e.g. when OpenAI ships a new Realtime event type).
    ///
    /// Deserialisation failures (e.g. an unknown enum value in a nested field) are
    /// caught and logged. We do NOT propagate the exception — this method is on
    /// the SCTP receive path, and an unhandled throw here would terminate the
    /// SCTP association and tear down the entire data channel for a single
    /// malformed (from our perspective) event.
    /// </summary>
    public void HandleIncomingData(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
    {
        string msgText = Encoding.UTF8.GetString(data);

        RealtimeEventBase? baseEvent;
        try
        {
            baseEvent = JsonSerializer.Deserialize<RealtimeEventBase>(msgText, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            // Most common cause: an enum value the typed class doesn't know about
            // (e.g. a new RealtimeConversationContentTypeEnum value introduced by
            // OpenAI). Forward the payload as a synthetic RealtimeUnknown so the
            // application can decide how to handle it, and keep the data channel
            // alive. Crashing the SCTP thread here would kill audio too.
            _logger.LogWarning(
                ex,
                "Failed to deserialise event on OpenAI data channel; forwarding raw JSON via RealtimeUnknown. Payload: {Payload}",
                msgText);

            string? typeField = TryExtractTypeField(msgText);
            string? eventId = TryExtractEventIdField(msgText);
            var fallback = new RealtimeUnknown
            {
                EventID = eventId,
                OriginalType = typeField,
                OriginalJson = msgText,
            };
            _endpoint.InvokeOnDataChannelMessage(dc, fallback);
            return;
        }

        if (baseEvent == null)
        {
            _logger.LogWarning("Received non‐OpenAI event on data channel: {Payload}", msgText);
            return;
        }

        if (baseEvent is RealtimeUnknown unknownEvent)
        {
            _logger.LogWarning(
                "Unrecognised event type '{Type}' on OpenAI data channel; forwarding as RealtimeUnknown with original JSON in OriginalJson.",
                unknownEvent.OriginalType);
        }

        // Always raise the event, including for RealtimeUnknown — callers can inspect
        // OriginalType / OriginalJson on the unknown variant to handle new event types
        // before SIPSorcery has typed support for them. Previously RealtimeUnknown was
        // silently dropped, which made OpenAI Realtime API renames (e.g. the GA event
        // type changes) appear as a total data-channel blackout to consumers.
        _endpoint.InvokeOnDataChannelMessage(dc, baseEvent);
    }

    /// <summary>
    /// Best-effort extraction of the top-level "type" string from an OpenAI
    /// event JSON payload, used to populate <see cref="RealtimeUnknown"/>
    /// when the strongly-typed deserialiser failed. Returns null if the
    /// JSON is malformed enough that even this lookup throws.
    /// </summary>
    private static string? TryExtractTypeField(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("type", out var prop)
                ? prop.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Best-effort extraction of the top-level "event_id" string.
    /// </summary>
    private static string? TryExtractEventIdField(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("event_id", out var prop)
                ? prop.GetString()
                : null;
        }
        catch
        {
            return null;
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
