//-----------------------------------------------------------------------------
// Filename: Program.fs
//
// Description: An example WebRTC application that can be used to interact with
// OpenAI's real-time API https://platform.openai.com/docs/guides/realtime-webrtc.
//
// NOTE: As of 24 Dec 2024 this example does work to establish an audio stream and is
// able to receive data channel messages. There is no echo cancellation feature in this
// demo so if not provided by the OS then ChatGPT will end up talking to itself.
//
// NOTE: As of 24 Dec 2024 the official OpenAI dotnet SDK is missing the realtime
// models that represent the JSON datachannel messages. As such some ruidimentary
// models have been created.
// The official SDK is available at https://github.com/openai/openai-dotnet.
// The OpenAI API realtime server events reference is available at
// https://platform.openai.com/docs/api-reference/realtime-server-events.
//
// Remarks:
// To get the ephemeral secret you first need an API key from OpenAI at
// https://platform.openai.com/settings/organization/api-keys.
//
// If you don't want to pass your OpenAI API key to this app an alternative approach is
// to create an ephemeral secret using the curl comamnd below and then hard code it into
// the application.
// NOTE each epehmeral key seems like it can ONLY be used once:
// curl -v https://api.openai.com/v1/realtime/sessions ^
//  --header "Authorization: Bearer %OPENAI_TOKEN%" ^
//  --header "Content-Type: application/json" ^
//  --data "{\"model\": \"gpt-4o-realtime-preview-2024-12-17\", \"voice\": \"verse\"}"
//
// Usage:
// set OPENAIKEY=your_openai_key
// dotnet run %OPENAIKEY%
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 26 Jan 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

open demo
open Microsoft.Extensions.Logging
open Microsoft.FSharp.Core
open Serilog
open System
open System.Threading
open SIPSorcery.Media
open SIPSorcery.Net
open SIPSorceryMedia.Windows

let OPENAI_REALTIME_SESSIONS_URL = "https://api.openai.com/v1/realtime/sessions"
let OPENAI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime"
let OPENAI_MODEL = "gpt-4o-realtime-preview-2024-12-17"
let OPENAI_VOICE = OpenAIVoicesEnum.shimmer
let OPENAI_DATACHANNEL_NAME = "oai-events"

/// Initialize the logger
let logger =
    LoggerFactory.Create(fun builder ->
        builder.AddSerilog(
            LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .CreateLogger()
        ) |> ignore
    ).CreateLogger("demo")

/// Convert Either<LError, string> to F# Result<string, string> by storing only error messages
let eitherToFSharpResult (either: LanguageExt.Either<LanguageExt.Common.Error, string>) : Result<string, string> =
    either.Match(
        (fun rightVal -> Ok rightVal),
        (fun (leftVal : LanguageExt.Common.Error) -> Error leftVal.Message)
    )

let getOpenAIKey (argv : string[]) : Option<string> =
    match argv |> Array.tryHead with
    | Some key when not (String.IsNullOrWhiteSpace(key)) -> 
        Some key
    | _ -> 
        None

/// Attempts to retrieve the OpenAI API key asynchronously.
/// Returns a string on success or raises an exception on failure.
let getEphemeralKey (url: string) (openAIKey: string) (model: string) (voice: OpenAIVoicesEnum) : Async<Option<string>> =
    async {
            let! eitherResult = OpenAIRealtimeRestClient.CreateEphemeralKeyAsync(url, openAIKey, model, voice) |> Async.AwaitTask 

            match eitherResult |> eitherToFSharpResult with
            | Ok key ->
                match key |> String.IsNullOrWhiteSpace with
                    | true -> 
                        printfn "Failed to obtain ephemeral key: key was empty."
                        return None
                    | false -> return Some key
            | Error error ->
                printfn "Failed to obtain ephemeral key: %s" error
                return None
        }

let OnDataChannelMessage (logger: ILogger) (dc: RTCDataChannel) (protocol: DataChannelPayloadProtocols) (data: byte[]) =
    match protocol with
    | DataChannelPayloadProtocols.WebRTC_Binary ->
        logger.Debug($"Data channel {dc.label} received binary message of length {data.Length}.")
    | DataChannelPayloadProtocols.WebRTC_String ->
        let message = System.Text.Encoding.UTF8.GetString(data)
        logger.Debug($"Data channel {dc.label} received text message {message}.")
    | _ -> ()

/// <summary>
/// Method to create the local peer connection instance and data channel.
/// </summary>
/// <param name="onConnectedSemaphore">A semaphore that will get set when the data channel on the peer connection is opened. Since the data channel
/// can only be opened once the peer connection is open this indicates both are ready for use.</param>
let createPeerConnection (logger: ILogger) (onConnectedSemaphore: SemaphoreSlim) : Async<RTCPeerConnection> = async {
    let pcConfig = RTCConfiguration(X_UseRtpFeedbackProfile = true)
    let peerConnection = new RTCPeerConnection(pcConfig)
    let! dataChannel = peerConnection.createDataChannel(OPENAI_DATACHANNEL_NAME) |> Async.AwaitTask

    // Sink (speaker) only audio end point.
    let windowsAudioEP = WindowsAudioEndPoint(AudioEncoder(includeOpus = true), -1, -1, false, false)
    windowsAudioEP.RestrictFormats(fun x -> x.FormatName = "OPUS")
    windowsAudioEP.add_OnAudioSinkError(fun err -> logger.Warning($"Audio sink error. {err}."))
    windowsAudioEP.add_OnAudioSourceEncodedSample(fun duration sample -> peerConnection.SendAudio(duration, sample))

    let audioTrack = MediaStreamTrack(windowsAudioEP.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv)
    peerConnection.addTrack(audioTrack)

    peerConnection.add_OnAudioFormatsNegotiated(fun audioFormats ->
        logger.Debug($"Audio format negotiated {audioFormats.Head().FormatName}.")
        windowsAudioEP.SetAudioSinkFormat(audioFormats.Head())
        windowsAudioEP.SetAudioSourceFormat(audioFormats.Head())
    )

    peerConnection.add_OnTimeout(fun mediaType -> logger.Debug($"Timeout on media {mediaType}."))
    peerConnection.add_oniceconnectionstatechange(fun state -> logger.Debug($"ICE connection state changed to {state}."))
    peerConnection.add_onconnectionstatechange(fun state -> 
        Async.Start(
            async {
                logger.Debug($"Peer connection state changed to {state}.")

                match state with
                | RTCPeerConnectionState.connected ->
                    do! windowsAudioEP.StartAudio() |> Async.AwaitTask
                    do! windowsAudioEP.StartAudioSink() |> Async.AwaitTask
                | RTCPeerConnectionState.closed | RTCPeerConnectionState.failed ->
                    do! windowsAudioEP.CloseAudio() |> Async.AwaitTask
                | _ -> ()
            }
        )
    )

    peerConnection.add_OnRtpPacketReceived(fun rep media rtpPkt ->
        if media = SDPMediaTypesEnum.audio then
            windowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, uint32 rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit = 1, rtpPkt.Payload)
    )

    dataChannel.add_onopen(fun () ->
        logger.Debug("OpenAI data channel opened.")
        onConnectedSemaphore.Release() |> ignore
    )

    dataChannel.add_onclose(fun () -> logger.Debug($"OpenAI data channel {dataChannel.label} closed."))
    dataChannel.add_onmessage(fun dc protocol data -> OnDataChannelMessage logger dc protocol data)

    return peerConnection
}

[<EntryPoint>]
let main argv =
    printfn "WebRTC OpenAI Demo Program"

    match getOpenAIKey argv with

    | None ->
        printfn "Please provide your OpenAI key as a command line argument." 
        printfn "It's used to get a single use ephemeral secret to establish the WebRTC connection."
        printfn "The recommended approach is to use an environment variable, for example: set OPENAIKEY=<your openai api key>"
        printfn "Then execute the application using: dotnet run %%OPENAIKEY%%"

    | Some openAIKey ->

        printfn "STEP 1: Get ephemeral key from OpenAI."

        // Start the async block
        async {
            let! ephemeralKeyOption = getEphemeralKey OPENAI_REALTIME_SESSIONS_URL openAIKey OPENAI_MODEL OPENAI_VOICE
            
            match ephemeralKeyOption with
            | Some ephemeralKey ->
                printfn "Ephemeral Key: %s" ephemeralKey
            | None ->
                printfn "Failed to obtain an ephemeral key."
        }
        |> Async.Start // This will start the async operation


    printfn "STEP 6: Wait for ctrl-c to indicate user exit."

    let exitMre = new ManualResetEvent(false)

    let handler = 
        ConsoleCancelEventHandler(fun _ e ->
            e.Cancel <- true
            exitMre.Set() |> ignore
        )

    // Attach the event handler for Ctrl-C (CancelKeyPress)
    Console.CancelKeyPress.AddHandler(handler)

    // Wait until the signal is received
    exitMre.WaitOne() |> ignore

    exitMre.Dispose()

    0
