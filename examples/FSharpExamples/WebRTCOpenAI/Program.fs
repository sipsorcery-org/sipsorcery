//-----------------------------------------------------------------------------
// Filename: Program.fs
//
// Description: An example WebRTC application that can be used to interact with
// OpenAI's real-time API https://platform.openai.com/docs/guides/realtime-webrtc.
//
// NOTE: As of 28 Jan 2025 this example does work to establish an audio stream and is
// able to receive data channel messages. There is no echo cancellation feature in this
// demo so if not provided by the OS then the AI will end up talking to itself.
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

open AsyncResult
open demo
open Microsoft.Extensions.Logging
open Microsoft.FSharp.Core
open SIPSorcery.Media
open SIPSorcery.Net
open SIPSorceryMedia.Windows
open System
open System.Threading

type LanguageExt.Option<'T> with
    member this.ToFSharpOption() =
        if this.IsSome then
            Some this.Case
        else
            None

let OPENAI_REALTIME_SESSIONS_URL = "https://api.openai.com/v1/realtime/sessions"
let OPENAI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime"
let OPENAI_MODEL = "gpt-4o-realtime-preview-2024-12-17"
let OPENAI_VOICE = OpenAIVoicesEnum.shimmer
let OPENAI_DATACHANNEL_NAME = "oai-events"
 
/// Initialize the logger
let logger = 
    let factory = LoggerFactory.Create(fun builder -> 
        builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddConsole() |> ignore) 
    factory.CreateLogger("demo")

/// Convert Either<LError, string> to F# Result<string, string> by storing only error messages
let eitherToFSharpResult (either: LanguageExt.Either<LanguageExt.Common.Error, string>) : Result<string, string> =
    either.Match(
        (fun rightVal -> Ok rightVal),
        (fun (leftVal : LanguageExt.Common.Error) -> Error leftVal.Message)
    )

/// Retrieve the OpenAI API key from the command line arguments.
let getOpenAIKeyFromCmdLine (argv : string[]) : Result<string, string> =
    match argv |> Array.tryHead with
    | Some key when not (String.IsNullOrWhiteSpace(key)) -> 
        Ok key 
    | _ -> 
        Error """
❌ Please provide your OpenAI key as a command line argument.
It's required to obtain a single-use ephemeral secret to establish the WebRTC connection.

🔹 Recommended approach: Use an environment variable.
   Example: set OPENAIKEY=<your OpenAI API key>
   Then run: dotnet run %OPENAIKEY%
"""

/// Attempts to retrieve the OpenAI API ephemeral key. This is the key used to send the SDP offer and get the SDP answer.
/// from the OpenAI REST server.
let getEphemeralKey (url: string) (openAIKey: string) (model: string) (voice: OpenAIVoicesEnum) : Async<Result<string, string>> =
    async {
            let! eitherResult = OpenAIRealtimeRestClient.CreateEphemeralKeyAsync(url, openAIKey, model, voice) |> Async.AwaitTask 

            match eitherResult |> eitherToFSharpResult with
            | Ok key ->
                match key |> String.IsNullOrWhiteSpace with
                    | true -> 
                        return Error "Failed to obtain ephemeral key: key was empty."
                    | false -> return Ok key
            | Error error ->
                return Error (sprintf "Failed to obtain ephemeral key: %s" error)
        }

/// Attempts to get the SDP answer from the OpenAI REST server.
let getAnswerSdp (ephemeralKey: string) (url: string) (model: string) (offerSdp: string) : Async<Result<string, string>> =
    async {
        let! eitherResult = OpenAIRealtimeRestClient.GetOpenAIAnswerSdpAsync(ephemeralKey, url, model, offerSdp) |> Async.AwaitTask 
        return eitherResult |> eitherToFSharpResult
    }

/// Method to handle data channel messages.
let OnDataChannelMessage (logger: ILogger) (dc: RTCDataChannel) (protocol: DataChannelPayloadProtocols) (data: byte[]) =
    //let message = Encoding.UTF8.GetString(data)
    
    // Parse the message into a base event type
    let serverEventModel = OpenAIDataChannelManager.ParseDataChannelMessage(data).ToFSharpOption()

    match serverEventModel with
    | Some (:? OpenAIResponseAudioTranscriptDone as doneEvent) ->
        logger.LogDebug $"Transcript done: {doneEvent.Transcript}"
    | Some _ -> ()
    | None ->
        logger.LogWarning "Failed to parse the openai data channel message."

/// Method to create the local peer connection instance and data channel.
let createPeerConnection (logger: ILogger) (onConnectedSemaphore: SemaphoreSlim) : Async<RTCPeerConnection> = async {
    let pcConfig = RTCConfiguration(X_UseRtpFeedbackProfile = true)
    let peerConnection = new RTCPeerConnection(pcConfig)
    let! dataChannel = peerConnection.createDataChannel(OPENAI_DATACHANNEL_NAME) |> Async.AwaitTask

    // Sink (speaker) only audio end point.
    let windowsAudioEP = WindowsAudioEndPoint(AudioEncoder(includeOpus = true), -1, -1, false, false)
    windowsAudioEP.RestrictFormats(fun x -> x.FormatName = "OPUS")
    windowsAudioEP.add_OnAudioSinkError(fun err -> logger.LogWarning($"Audio sink error. {err}."))
    windowsAudioEP.add_OnAudioSourceEncodedSample(fun duration sample -> peerConnection.SendAudio(duration, sample))

    let audioTrack = MediaStreamTrack(windowsAudioEP.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv)
    peerConnection.addTrack(audioTrack)

    peerConnection.add_OnAudioFormatsNegotiated(fun audioFormats ->
        logger.LogDebug($"Audio format negotiated {audioFormats.Head().FormatName}.")
        windowsAudioEP.SetAudioSinkFormat(audioFormats.Head())
        windowsAudioEP.SetAudioSourceFormat(audioFormats.Head())
    )

    peerConnection.add_OnTimeout(fun mediaType -> logger.LogDebug($"Timeout on media {mediaType}."))
    peerConnection.add_oniceconnectionstatechange(fun state -> logger.LogDebug($"ICE connection state changed to {state}."))
    peerConnection.add_onconnectionstatechange(fun state -> 
        Async.Start(
            async {
                logger.LogDebug($"Peer connection state changed to {state}.")

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
            windowsAudioEP.GotAudioRtp(
                rep, 
                rtpPkt.Header.SyncSource, 
                uint32 rtpPkt.Header.SequenceNumber, 
                rtpPkt.Header.Timestamp, 
                rtpPkt.Header.PayloadType, 
                rtpPkt.Header.MarkerBit = 1, 
                rtpPkt.Payload)
    )

    dataChannel.add_onopen(fun () ->
        logger.LogDebug("OpenAI data channel opened.")
        onConnectedSemaphore.Release() |> ignore
    )

    dataChannel.add_onclose(fun () -> logger.LogDebug($"OpenAI data channel {dataChannel.label} closed."))
    dataChannel.add_onmessage(fun dc protocol data -> OnDataChannelMessage logger dc protocol data)

    return peerConnection
}

/// Method to set the SDP answer on the peer connection.
let setSdpAnswer (pc: RTCPeerConnection) answerSdp : Result<string, string> =

    let remoteDescriptionInit = RTCSessionDescriptionInit()
    remoteDescriptionInit.``type`` <- RTCSdpType.answer
    remoteDescriptionInit.sdp <- answerSdp
    let setAnswerResult = pc.setRemoteDescription(remoteDescriptionInit)

    if setAnswerResult = SetDescriptionResultEnum.OK then
        printfn "SDP answer successfully set on peer connection."
        Ok "Data channel connected successfully."
    else
        printfn "Failed to set the OpenAI SDP Answer."
        Error "Failed to set the OpenAI SDP Answer."

/// Method to send a response create message to the OpenAI data channel.
/// Response create messages trigger the OpenAI API to generate an audio response.
let sendResponseCreate (logger: ILogger) (dc: RTCDataChannel) (voice : OpenAIVoicesEnum) message =
    let responseCreate = 
        OpenAIResponseCreate(
            EventID = Guid.NewGuid().ToString(),
            Response = OpenAIResponseCreateResponse(
                Instructions = message,
                Voice = voice.ToString()
            )
        )

    logger.LogInformation($"Sending initial response create to first call data channel {dc.label}.");
    logger.LogDebug(responseCreate.ToJson());

    dc.send(responseCreate.ToJson());

/// Method to wait for the user to press ctrl-c before exiting.
let waitForCtrlCToExit () =
    let exitMre = new ManualResetEvent(false)

    let handler = 
        ConsoleCancelEventHandler(fun _ e ->
            e.Cancel <- true
            exitMre.Set() |> ignore
        )

    // Attach the event handler for Ctrl-C (CancelKeyPress)
    Console.CancelKeyPress.AddHandler(handler)

    // Wait until the ctrl-c signal is received
    exitMre.WaitOne() |> ignore

    exitMre.Dispose()

[<EntryPoint>]
let main argv =
    printfn "F# WebRTC OpenAI Demo Program"

    let workflow argv = asyncResult {
        let! openAIKey = getOpenAIKeyFromCmdLine argv |> ofResult

        printfn "STEP 1: Get ephemeral key from OpenAI."
        let! ephemeralKey = getEphemeralKey OPENAI_REALTIME_SESSIONS_URL openAIKey OPENAI_MODEL OPENAI_VOICE |> ofAsyncResult

        printfn "STEP 2: Create WebRTC PeerConnection & get local SDP offer."
        let dcConnectedSemaphore = new SemaphoreSlim(0, 1)
        let! pc = createPeerConnection logger dcConnectedSemaphore |> ofAsync
        let offer = pc.createOffer();
        pc.setLocalDescription(offer) |> ignore;

        printfn "STEP 3: Send SDP offer to OpenAI."
        let! answerSdp = getAnswerSdp ephemeralKey OPENAI_REALTIME_BASE_URL OPENAI_MODEL offer.sdp |> ofAsyncResult
        printfn "SDP Answer:\n%s" answerSdp

        printfn "STEP 4: Set remote SDP"
        let! _ = setSdpAnswer pc answerSdp |> ofResult

        printfn "STEP 5: Wait for data channel to connect."
        do! dcConnectedSemaphore.WaitAsync() |> Async.AwaitTask |> ofAsync

        printfn "STEP 6: Trigger the AI to start the conversation."
        do sendResponseCreate logger (pc.DataChannels.Head()) OPENAI_VOICE "Introduce urself. Keep it short."

        return "Workflow completed successfully."
    }

    let workflowResult = workflow argv |> Async.RunSynchronously

    match workflowResult with
        | Ok result -> 
            printfn "Success: %s" result
            printfn "Use ctrl-c to exit."
            do waitForCtrlCToExit()
            0
        | Error error -> 
            printfn "Error: %s" error
            -1
