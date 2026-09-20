//-----------------------------------------------------------------------------
// Filename: SIPClient.cs
//
// Description: A SIP client for making and receiving calls. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//  
// History:
// 27 Mar 2012	Aaron Clauson	Refactored, Hobart, Australia.
// 03 Dec 2019  Aaron Clauson   Replace separate client and server user agents with full user agent.
// 19 Sep 2026  Aaron Clauson   Allow the audio scope to act as the outgoing video source when the
//                              remote party has video and this end has no camera.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using SIPSorceryMedia.Windows;
using AudioScope;

namespace SIPSorcery.SoftPhone
{
    public partial class SIPClient : IDisposable
    {
        private static string _sdpMimeContentType = SDP.SDP_MIME_CONTENTTYPE;
        private static int TRANSFER_RESPONSE_TIMEOUT_SECONDS = 10;

        private string m_sipUsername = SIPSoftPhoneState.Settings.SIPUsername;
        private string m_sipPassword = SIPSoftPhoneState.Settings.SIPPassword;
        private string m_sipServer = SIPSoftPhoneState.Settings.SIPServer;
        private string m_sipFromName = SIPSoftPhoneState.Settings.SIPFromName;
        private bool m_useWebRTCMedia = SIPSoftPhoneState.Settings.UseWebRTCMedia;

        private SIPTransport m_sipTransport;
        private SIPUserAgent m_userAgent;
        private SIPServerUserAgent m_pendingIncomingCall;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private int m_audioOutDeviceIndex = SIPSoftPhoneState.Settings.AudioOutDeviceIndex;

        private bool m_disableVideo = SIPSoftPhoneState.Settings.DisableVideo;
        private bool m_useAudioScope = SIPSoftPhoneState.Settings.UseAudioScope;

        // The scope video is rendered and encoded on its own timer rather than inside the audio
        // callback. Encoding a 640x480 frame takes several milliseconds and doing that synchronously
        // on the audio pacing thread pushes audio packets past their 20ms budget, which makes the
        // audio choppy. 33ms is ~30fps.
        private const int SCOPE_FRAME_INTERVAL_MS = 33;

        // Set when the audio scope is standing in for a camera as the outgoing video source. Null
        // when this end has a real camera, or when the scope is only being drawn locally.
        private FFmpegVideoEndPoint _scopeVideoEndPoint;

        // True once the scope is confirmed to have a negotiated video stream to go out on. Until
        // then, and whenever video is disabled, scope frames are drawn locally instead.
        private bool _scopeSendsVideo;

        // The renderer hands back its own internal pixel buffer and overwrites it on the next tick,
        // while the UI blits on the dispatcher some time later. Alternating between two buffers
        // means the frame being displayed is never the one being drawn into, without allocating a
        // 900KB (large object heap) array every frame.
        private readonly byte[][] _scopeDisplayBuffers = new byte[2][];
        private int _scopeDisplayBufferIndex;
        private AudioScopeRenderer _scopeRenderer;
        private Timer _scopeTimer;
        private readonly IAudioEncoder _scopeAudioDecoder = new AudioEncoder(includeOpus: true);
        private bool _scopeDecodeErrorReported;
        private readonly object _scopePcmLock = new object();
        private short[] _scopeLatestPcm;
        private int _scopeRenderInProgress;
        private AudioFormat? _scopeAudioFormat;

        [GeneratedRegex(@"^SIP/2\.0 (?<statusCode>\d{3})")]
        private static partial Regex SipFragStatusCodeRegex();

        public event Action<SIPClient> CallAnswer;                 // Fires when an outgoing SIP call is answered.
        public event Action<SIPClient> CallEnded;                  // Fires when an incoming or outgoing call is over.
        public event Action<SIPClient, string> StatusMessage;      // Fires when the SIP client has a status message it wants to inform the UI about.

        public event Action<SIPClient> RemotePutOnHold;            // Fires when the remote call party puts us on hold.	
        public event Action<SIPClient> RemoteTookOffHold;          // Fires when the remote call party takes us off hold.

        public event Action<EncodedAudioFrame> OnRemoteAudio;                        // Fires when a decoded audio sample from the remote peer is ready.
        public event VideoSinkSampleDecodedDelegate OnRemoteVideo;    // Fires when a decoded video sample from the remote peer is ready.
        public event VideoSinkSampleDecodedDelegate OnAudioScopeFrame; // Fires when an audio scope frame is ready to be drawn locally.

        /// <summary>
        /// Once a call is established this holds the properties of the established SIP dialogue.
        /// </summary>
        public SIPDialogue Dialogue
        {
            get { return m_userAgent.Dialogue; }
        }

        private RTPSession MediaSession;
        //private RTCPeerConnection _rtcPeerConnection;

        /// <summary>
        /// Returns true of this SIP client is on an active call.
        /// </summary>
        public bool IsCallActive
        {
            get { return m_userAgent.IsCallActive; }
        }

        /// <summary>
        /// Returns true if this call is known to be on hold.
        /// </summary>
        public bool IsOnHold
        {
            get { return m_userAgent.IsOnLocalHold || m_userAgent.IsOnRemoteHold; }
        }

        /// <summary>
        /// True once the call has negotiated a video stream from the remote party, which is what
        /// decides whether the UI has remote video to display. Only meaningful after the call has
        /// been answered, since until then there is no negotiated session to ask.
        /// </summary>
        public bool HasVideo => MediaSession?.VideoRemoteTrack != null;

        public SIPClient(SIPTransport sipTransport)
        {
            m_sipTransport = sipTransport;

            m_userAgent = new SIPUserAgent(m_sipTransport, null);
            m_userAgent.ClientCallTrying += CallTrying;
            m_userAgent.ClientCallRinging += CallRinging;
            m_userAgent.ClientCallAnswered += CallAnswered;
            m_userAgent.ClientCallFailed += CallFailed;
            m_userAgent.OnCallHungup += CallFinished;
            m_userAgent.ServerCallCancelled += IncomingCallCancelled;
            m_userAgent.OnTransferNotify += OnTransferNotify;
            m_userAgent.OnDtmfTone += OnDtmfTone;
        }

        /// <summary>
        /// Places an outgoing SIP call.
        /// </summary>
        /// <param name="destination">The SIP URI to place a call to. The destination can be a full SIP URI in which case the call will
        /// be placed anonymously directly to that URI. Alternatively it can be just the user portion of a URI in which case it will
        /// be sent to the configured SIP server.</param>
        public async Task Call(string destination)
        {
            // Determine if this is a direct anonymous call or whether it should be placed using the pre-configured SIP server account. 
            SIPURI callURI = null;
            string sipUsername = null;
            string sipPassword = null;
            string fromHeader = null;

            if (destination.Contains("@") || m_sipServer == null)
            {
                // Anonymous call direct to SIP server specified in the URI.
                callURI = SIPURI.ParseSIPURIRelaxed(destination);
                fromHeader = (new SIPFromHeader(m_sipFromName, SIPURI.ParseSIPURI(SIPFromHeader.DEFAULT_FROM_URI), null)).ToString();
            }
            else
            {
                // This call will use the pre-configured SIP account.
                callURI = SIPURI.ParseSIPURIRelaxed($"{destination}@{m_sipServer}");
                sipUsername = m_sipUsername;
                sipPassword = m_sipPassword;
                fromHeader = (new SIPFromHeader(m_sipFromName, new SIPURI(m_sipUsername, m_sipServer, null), null)).ToString();
            }

            StatusMessage(this, $"Starting call to {callURI}.");

            var dstEndpoint = await SIPDns.ResolveAsync(callURI, false, _cts.Token);

            if (dstEndpoint == null)
            {
                StatusMessage(this, $"Call failed, could not resolve {callURI}.");
            }
            else
            {
                StatusMessage(this, $"Call progressing, resolved {callURI} to {dstEndpoint}.");
                System.Diagnostics.Debug.WriteLine($"DNS lookup result for {callURI}: {dstEndpoint}.");
                SIPCallDescriptor callDescriptor = new SIPCallDescriptor(sipUsername, sipPassword, callURI.ToString(), fromHeader, null, null, null, null, SIPCallDirection.Out, _sdpMimeContentType, null, null);

                // On an outgoing call there is no way to know whether the remote party has video
                // until the answer arrives, so offer it and decide what to do with it afterwards.
                MediaSession = !m_useWebRTCMedia ? CreateVoIPMediaSession() : CreateWebRtcMediaSession(offerVideo: true);

                m_userAgent.RemotePutOnHold += OnRemotePutOnHold;
                m_userAgent.RemoteTookOffHold += OnRemoteTookOffHold;

                await m_userAgent.InitiateCallAsync(callDescriptor, MediaSession);
            }
        }

        /// <summary>
        /// Cancels an outgoing SIP call that hasn't yet been answered.
        /// </summary>
        public void Cancel()
        {
            StatusMessage(this, $"Cancelling SIP call to {m_userAgent.CallDescriptor?.Uri}.");
            m_userAgent.Cancel();
        }

        /// <summary>
        /// Accepts an incoming call. This is the first step in answering a call.
        /// From this point the call can still be rejected, redirected or answered.
        /// </summary>
        /// <param name="sipRequest">The SIP request containing the incoming call request.</param>
        public void Accept(SIPRequest sipRequest)
        {
            m_pendingIncomingCall = m_userAgent.AcceptCall(sipRequest);
        }

        /// <summary>
        /// Answers an incoming SIP call.
        /// </summary>
        public async Task<bool> Answer()
        {
            if (m_pendingIncomingCall == null)
            {
                StatusMessage(this, $"There was no pending call available to answer.");
                return false;
            }
            else
            {
                var sipRequest = m_pendingIncomingCall.ClientTransaction.TransactionRequest;

                // Assume that if the INVITE request does not contain an SDP offer that it will be an 
                // audio only call.
                bool hasAudio = true;
                bool hasVideo = false;

                if (sipRequest.Body != null)
                {
                    SDP offerSDP = SDP.ParseSDPDescription(sipRequest.Body);
                    hasAudio = offerSDP.Media.Any(x => x.Media == SDPMediaTypesEnum.audio && x.MediaStreamStatus != MediaStreamStatusEnum.Inactive);
                    hasVideo = offerSDP.Media.Any(x => x.Media == SDPMediaTypesEnum.video && x.MediaStreamStatus != MediaStreamStatusEnum.Inactive);
                }

                // The offer already says whether the remote party has video, and an answer must not
                // add an m-line the offer did not contain, so only take a video stream if it did.
                MediaSession = !m_useWebRTCMedia ? CreateVoIPMediaSession() : CreateWebRtcMediaSession(offerVideo: hasVideo);

                m_userAgent.RemotePutOnHold += OnRemotePutOnHold;
                m_userAgent.RemoteTookOffHold += OnRemoteTookOffHold;

                bool result = await m_userAgent.Answer(m_pendingIncomingCall, MediaSession);
                m_pendingIncomingCall = null;

                return result;
            }
        }

        /// <summary>
        /// Redirects an incoming SIP call.
        /// </summary>
        public void Redirect(string destination)
        {
            m_pendingIncomingCall?.Redirect(SIPResponseStatusCodesEnum.MovedTemporarily, SIPURI.ParseSIPURIRelaxed(destination));
        }

        /// <summary>
        /// Puts the remote call party on hold.
        /// </summary>
        public async Task PutOnHold()
        {
            //await MediaSession.PutOnHold();
            m_userAgent.PutOnHold();
            StatusMessage(this, "Local party put on hold");
        }

        /// <summary>
        /// Takes the remote call party off hold.
        /// </summary>
        public async Task TakeOffHold()
        {
            //await MediaSession.TakeOffHold();
            m_userAgent.TakeOffHold();
            StatusMessage(this, "Local party taken off on hold");
        }

        /// <summary>
        /// Rejects an incoming SIP call.
        /// </summary>
        public void Reject()
        {
            m_pendingIncomingCall?.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
        }

        /// <summary>
        /// Hangs up an established SIP call.
        /// </summary>
        public void Hangup()
        {
            if (m_userAgent.IsCallActive)
            {
                m_userAgent.Hangup();
                CallFinished(null);
            }
        }

        /// <summary>
        /// Sends a request to the remote call party to initiate a blind transfer to the
        /// supplied destination.
        /// </summary>
        /// <param name="destination">The SIP URI of the blind transfer destination.</param>
        /// <returns>True if the transfer was accepted or false if not.</returns>
        public Task<bool> BlindTransfer(string destination)
        {
            if (SIPURI.TryParse(destination, out var uri))
            {
                return m_userAgent.BlindTransfer(uri, TimeSpan.FromSeconds(TRANSFER_RESPONSE_TIMEOUT_SECONDS), _cts.Token);
            }
            else
            {
                StatusMessage(this, $"The transfer destination was not a valid SIP URI.");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Sends a request to the remote call party to initiate an attended transfer.
        /// </summary>
        /// <param name="transferee">The dialog that will be replaced on the initial call party.</param>
        /// <returns>True if the transfer was accepted or false if not.</returns>
        public Task<bool> AttendedTransfer(SIPDialogue transferee)
        {
            return m_userAgent.AttendedTransfer(transferee, TimeSpan.FromSeconds(TRANSFER_RESPONSE_TIMEOUT_SECONDS), _cts.Token);
        }

        /// <summary>
        /// Shuts down the SIP client.
        /// </summary>
        public void Shutdown()
        {
            Hangup();
        }

        /// <summary>
        /// Creates the media session to use with the SIP call.
        /// </summary>
        /// <returns>A new media session object.</returns>
        private VoIPMediaSession CreateVoIPMediaSession()
        {
            var windowsAudioEndPoint = new WindowsAudioEndPoint(new AudioEncoder(), m_audioOutDeviceIndex);

            MediaEndPoints mediaEndPoints = new MediaEndPoints
            {
                AudioSink = windowsAudioEndPoint,
                AudioSource = windowsAudioEndPoint
            };

            if (m_disableVideo)
            {
                var audioOnlyMediaSession = new VoIPMediaSession(mediaEndPoints);
                audioOnlyMediaSession.AcceptRtpFromAny = true;
                return audioOnlyMediaSession;
            }
            else
            {
                var windowsVideoEndPoint = new WindowsVideoEndPoint(new FFmpegVideoEncoder());

                mediaEndPoints.VideoSink = windowsVideoEndPoint;
                mediaEndPoints.VideoSource = windowsVideoEndPoint;

                // Fallback video source if a Windows webcam cannot be accessed.
                var testPatternSource = new VideoTestPatternSource(new FFmpegVideoEncoder());

                var audioAndVideoMediaSession = new VoIPMediaSession(mediaEndPoints, testPatternSource);
                audioAndVideoMediaSession.AcceptRtpFromAny = true;

                //mediaEndPoints.VideoSink.VideoSinkSampleDecodedFasterDelegate += OnRemoteVideo;

                return audioAndVideoMediaSession;
            }
        }

        /// <summary>
        /// Creates the media session to use with the SIP call.
        /// </summary>
        /// <param name="offerVideo">True if a video stream should be part of this session. For an
        /// outgoing call that is always the case since the remote party's video can only be
        /// discovered by offering. For an incoming call it reflects the offer we received.</param>
        /// <returns>A new media session object.</returns>
        private RTCPeerConnection CreateWebRtcMediaSession(bool offerVideo)
        {
            var windowsAudioEndPoint = new WindowsAudioEndPoint(new AudioEncoder(), m_audioOutDeviceIndex);
            IVideoEndPoint videoEndPoint = null;

            var pc = new RTCPeerConnection();
            pc.AcceptRtpFromAny = true;

            MediaStreamTrack audioTrack = new MediaStreamTrack(windowsAudioEndPoint.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
            pc.addTrack(audioTrack);

            pc.OnAudioFormatsNegotiated += (formats) =>
            {
                windowsAudioEndPoint.SetAudioSinkFormat(formats.First());

                // Stashed so the scope can decode the microphone's encoded samples back to PCM.
                _scopeAudioFormat = formats.First();
            };
            windowsAudioEndPoint.OnAudioSourceEncodedSample += pc.SendAudio;
            pc.OnAudioFrameReceived += (audioFrame) => OnRemoteAudio?.Invoke(audioFrame);
            pc.OnAudioFrameReceived += windowsAudioEndPoint.GotEncodedMediaFrame;

            if (!m_disableVideo && offerVideo)
            {
                if (m_useAudioScope)
                {
                    // The audio scope takes the camera's place as this call's video source. It is
                    // also the sink, so the remote party's video is decoded for display here.
                    _scopeVideoEndPoint = new FFmpegVideoEndPoint();
                    videoEndPoint = _scopeVideoEndPoint;
                }
                else
                {
                    videoEndPoint = new WindowsVideoEndPoint(new FFmpegVideoEncoder());
                    //videoEndPoint.RestrictFormats(f => f.Codec == VideoCodecsEnum.H265);
                }
            }

            if (videoEndPoint != null)
            {
                var videoTrack = new MediaStreamTrack(videoEndPoint.GetVideoSourceFormats());
                pc.addTrack(videoTrack);

                pc.OnVideoFormatsNegotiated += (formats) => videoEndPoint.SetVideoSinkFormat(formats.First());
                pc.OnVideoFormatsNegotiated += (formats) => videoEndPoint.SetVideoSourceFormat(formats.First());
                videoEndPoint.OnVideoSourceEncodedSample += pc.SendVideo;
                //windowsVideoEndPoint.OnVideoSourceError += VideoSource_OnVideoSourceError;
                pc.OnVideoFrameReceived += videoEndPoint.GotVideoFrame;
                videoEndPoint.OnVideoSinkDecodedSample += (byte[] sample, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
                    OnRemoteVideo?.Invoke(sample, width, height, stride, pixelFormat);
            }

            if (m_useAudioScope)
            {
                // Tap the audio the scope visualises. These handlers run on the audio pacing threads
                // so they only decode and stash; the render, and the encode when there is one, happen
                // on the scope timer. Both bail out while _scopeRenderer is null, so nothing is
                // decoded until the scope is actually running.
                if (m_disableVideo)
                {
                    // Drawn locally only, so visualise this end's microphone.
                    windowsAudioEndPoint.OnAudioSourceEncodedSample += (durationRtpUnits, sample) =>
                        StashScopePcm(sample, _scopeAudioFormat);
                }
                else
                {
                    // Sent to the remote party as video, so visualise their own audio.
                    pc.OnAudioFrameReceived += (audioFrame) =>
                        StashScopePcm(audioFrame?.EncodedAudio, audioFrame?.AudioFormat);
                }
            }

            pc.onconnectionstatechange += async (state) =>
            {
                if (state == RTCPeerConnectionState.connected)
                {
                    await windowsAudioEndPoint.Start();

                    if(videoEndPoint != null)
                    {
                        await videoEndPoint.StartVideo();
                        await videoEndPoint.StartVideoSink();

                        RequestPeerConnectionKeyFrame(pc);
                    }

                    if (m_useAudioScope)
                    {
                        // The scope only goes out on the wire if there is a negotiated video stream
                        // to carry it. Otherwise it is drawn locally, which is both the video
                        // disabled case and the fallback when the remote party answered without
                        // video.
                        StartAudioScopeVideo(sendsVideo: _scopeVideoEndPoint != null && pc.VideoRemoteTrack != null);
                    }
                }
                else if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
                {
                    StopAudioScopeVideo();

                    await windowsAudioEndPoint.Close();

                    if (videoEndPoint != null)
                    {
                        await videoEndPoint.Close();
                    }
                }
            };

            return pc;
        }

        /// <summary>
        /// Decodes a block of encoded audio and stashes it for the scope timer to render. Called on
        /// the audio source or receive thread, so it deliberately does no rendering or encoding.
        /// </summary>
        private void StashScopePcm(byte[] encodedSample, AudioFormat? format)
        {
            if (_scopeRenderer == null || encodedSample == null || format == null)
            {
                return;
            }

            try
            {
                var decoded = _scopeAudioDecoder.DecodeAudio(encodedSample, format.Value);

                lock (_scopePcmLock)
                {
                    _scopeLatestPcm = decoded;
                }
            }
            catch (Exception excp)
            {
                // This would repeat for every audio packet, so say it once rather than flooding the
                // status bar and the UI dispatcher ~50 times a second.
                if (!_scopeDecodeErrorReported)
                {
                    _scopeDecodeErrorReported = true;
                    StatusMessage(this, $"Audio scope could not decode an audio sample. {excp.Message}");
                }
            }
        }

        /// <summary>
        /// Starts the clock that renders the audio scope.
        /// </summary>
        /// <param name="sendsVideo">True to encode each frame and send it to the remote party as this
        /// call's video stream, false to hand it to the UI to draw locally.</param>
        private void StartAudioScopeVideo(bool sendsVideo)
        {
            if (_scopeTimer != null)
            {
                return;
            }

            _scopeSendsVideo = sendsVideo;
            _scopeDecodeErrorReported = false;
            _scopeRenderer = new AudioScopeRenderer();

            int frameSize = AudioScopeRenderer.Width * AudioScopeRenderer.Height * AudioScopeRenderer.PIXEL_STRIDE;
            _scopeDisplayBuffers[0] ??= new byte[frameSize];
            _scopeDisplayBuffers[1] ??= new byte[frameSize];

            _scopeTimer = new Timer(OnScopeTimer, null, 0, SCOPE_FRAME_INTERVAL_MS);

            StatusMessage(this, sendsVideo
                ? "Sending an audio scope of the remote party's audio as this call's video stream."
                : "Showing an audio scope of the microphone.");
        }

        /// <summary>
        /// Stops the scope video clock. Safe to call when the scope was never started.
        /// </summary>
        private void StopAudioScopeVideo()
        {
            _scopeTimer?.Dispose();
            _scopeTimer = null;
            _scopeRenderer = null;
            _scopeSendsVideo = false;

            lock (_scopePcmLock)
            {
                _scopeLatestPcm = null;
            }
        }

        /// <summary>
        /// Fires every SCOPE_FRAME_INTERVAL_MS on a thread pool thread and renders a scope frame from
        /// the most recently stashed audio. If the previous render and encode is still running the
        /// tick is dropped rather than queued, so a slow encode can never build a backlog.
        /// </summary>
        private void OnScopeTimer(object state)
        {
            if (Interlocked.CompareExchange(ref _scopeRenderInProgress, 1, 0) != 0)
            {
                return;
            }

            try
            {
                short[] pcm;
                lock (_scopePcmLock)
                {
                    pcm = _scopeLatestPcm;
                }

                // Taken into locals because a call ending concurrently nulls both.
                var renderer = _scopeRenderer;
                var scopeVideoEndPoint = _scopeSendsVideo ? _scopeVideoEndPoint : null;

                if (pcm != null && renderer != null)
                {
                    var frame = renderer.ProcessAudioSample(pcm.Select(s => new Complex(s / 32768f, 0f)).ToArray());

                    if (scopeVideoEndPoint != null)
                    {
                        scopeVideoEndPoint.ExternalVideoSourceRawSample(
                            SCOPE_FRAME_INTERVAL_MS,
                            AudioScopeRenderer.Width,
                            AudioScopeRenderer.Height,
                            frame,
                            VideoPixelFormatsEnum.Bgr);
                    }
                    else
                    {
                        RaiseAudioScopeFrame(frame);
                    }
                }
            }
            catch (Exception excp)
            {
                StatusMessage(this, $"Audio scope render failed. {excp.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _scopeRenderInProgress, 0);
            }
        }

        /// <summary>
        /// Hands a rendered scope frame to the UI to draw locally. The frame is copied into one of
        /// two alternating buffers first, because the renderer reuses its own buffer on the next tick
        /// while the UI is still blitting this one on the dispatcher.
        /// </summary>
        private void RaiseAudioScopeFrame(byte[] frame)
        {
            var handler = OnAudioScopeFrame;

            if (handler == null || frame == null)
            {
                return;
            }

            _scopeDisplayBufferIndex ^= 1;
            var display = _scopeDisplayBuffers[_scopeDisplayBufferIndex];

            if (display == null || display.Length < frame.Length)
            {
                return;
            }

            Buffer.BlockCopy(frame, 0, display, 0, frame.Length);

            handler(display,
                AudioScopeRenderer.Width,
                AudioScopeRenderer.Height,
                AudioScopeRenderer.Width * AudioScopeRenderer.PIXEL_STRIDE,
                VideoPixelFormatsEnum.Bgr);
        }

        private void RequestPeerConnectionKeyFrame(RTCPeerConnection pc)
        {
            // Both tracks have to be present. A video track is offered whenever this end has
            // something to send, including the audio scope, but the remote party is free to answer
            // without video, which leaves VideoRemoteTrack null.
            if (pc != null && pc.connectionState == RTCPeerConnectionState.connected &&
                pc.VideoLocalTrack != null && pc.VideoRemoteTrack != null)
            {
                var localVideoSsrc = pc.VideoLocalTrack.Ssrc;
                var remoteVideoSsrc = pc.VideoRemoteTrack.Ssrc;

                RTCPFeedback pli = new RTCPFeedback(localVideoSsrc, remoteVideoSsrc, PSFBFeedbackTypesEnum.PLI);
                pc.SendRtcpFeedback(SDPMediaTypesEnum.video, pli);
            }
        }

        /// <summary>
        /// A trying response has been received from the remote SIP UAS on an outgoing call.
        /// </summary>
        private void CallTrying(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            StatusMessage(this, $"Call trying: {sipResponse.StatusCode} {sipResponse.ReasonPhrase}.");
        }

        /// <summary>
        /// A ringing response has been received from the remote SIP UAS on an outgoing call.
        /// </summary>
        private void CallRinging(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            StatusMessage(this, $"Call ringing: {sipResponse.StatusCode} {sipResponse.ReasonPhrase}.");
        }

        /// <summary>
        /// An outgoing call was rejected by the remote SIP UAS on an outgoing call.
        /// </summary>
        private void CallFailed(ISIPClientUserAgent uac, string errorMessage, SIPResponse failureResponse)
        {
            StatusMessage(this, $"Call failed: {errorMessage}.");
            CallFinished(null);
        }

        /// <summary>
        /// An outgoing call was successfully answered.
        /// </summary>
        /// <param name="uac">The local SIP user agent client that initiated the call.</param>
        /// <param name="sipResponse">The SIP answer response received from the remote party.</param>
        private void CallAnswered(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            StatusMessage(this, $"Call answered: {sipResponse.StatusCode} {sipResponse.ReasonPhrase}.");
            CallAnswer?.Invoke(this);
        }

        /// <summary>
        /// Cleans up after a SIP call has completely finished.
        /// </summary>
        private void CallFinished(SIPDialogue dialogue)
        {
            m_pendingIncomingCall = null;
            StopAudioScopeVideo();
            CallEnded(this);
        }

        /// <summary>
        /// An incoming call was cancelled by the caller.
        /// </summary>
        private void IncomingCallCancelled(ISIPServerUserAgent uas, SIPRequest cancelRequest)
        {
            //SetText(m_signallingStatus, "incoming call cancelled for: " + uas.CallDestination + ".");
            CallFinished(null);
        }

        /// <summary>
        /// Event handler for NOTIFY requests that provide updates about the state of a 
        /// transfer.
        /// </summary>
        /// <param name="sipFrag">The SIP snippet containing the transfer status update.</param>
        private void OnTransferNotify(string sipFrag)
        {
            if (sipFrag?.Contains("SIP/2.0 200") == true)
            {
                // The transfer attempt got a successful answer. Can hangup the call.
                Hangup();
            }
            else
            {
                var statusCodeMatch = SipFragStatusCodeRegex().Match(sipFrag);
                if (statusCodeMatch.Success)
                {
                    int statusCode = Int32.Parse(statusCodeMatch.Result("${statusCode}"));
                    SIPResponseStatusCodesEnum responseStatusCode = (SIPResponseStatusCodesEnum)statusCode;
                    StatusMessage(this, $"Transfer failed {responseStatusCode}");
                }
            }
        }

        /// <summary>
        /// Event handler for DTMF events on the remote call party's RTP stream.
        /// </summary>
        /// <param name="dtmfKey">The DTMF key pressed.</param>
        private void OnDtmfTone(byte dtmfKey, int duration)
        {
            StatusMessage(this, $"DTMF event from remote call party {dtmfKey} duration {duration}.");
        }

        /// <summary>	
        /// Event handler that notifies us the remote party has put us on hold.	
        /// </summary>	
        private void OnRemotePutOnHold()
        {
            RemotePutOnHold?.Invoke(this);
        }

        /// <summary>	
        /// Event handler that notifies us the remote party has taken us off hold.	
        /// </summary>	
        private void OnRemoteTookOffHold()
        {
            RemoteTookOffHold?.Invoke(this);
        }

        /// <summary>
        /// Requests the RTP session to send a RTP event representing a DTMF tone to the
        /// remote party.
        /// </summary>
        /// <param name="tone">A byte representing the tone to send. Must be between 0 and 15.</param>
        public Task SendDTMF(byte tone)
        {
            if (m_userAgent != null)
            {
                return m_userAgent.SendDtmf(tone);
            }
            else
            {
                return Task.FromResult(0);
            }
        }

        public void Dispose()
        {
            StopAudioScopeVideo();
            _scopeVideoEndPoint?.Dispose();
            _scopeVideoEndPoint = null;
            //MediaSession?.VideoSink.OnVideoSinkDecodedSampleFaster -= OnVideoRemoteSampleReady;
        }
    }
}
