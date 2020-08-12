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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace SIPSorcery.SoftPhone
{
    public class SIPClient
    {
        private static string _sdpMimeContentType = SDP.SDP_MIME_CONTENTTYPE;
        private static int TRANSFER_RESPONSE_TIMEOUT_SECONDS = 10;
        private static int VIDEO_LIVE_FRAMES_PER_SECOND = 30;
        private static int VIDEO_ONHOLD_FRAMES_PER_SECOND = 3;
        private const string MUSIC_FILE_PCMU = "media/Macroform_-_Simplicity.ulaw";
        private const string MUSIC_FILE_PCMA = "media/Macroform_-_Simplicity.alaw";
        private const string MUSIC_FILE_G722 = "media/Macroform_-_Simplicity.g722";

        private string m_sipUsername = SIPSoftPhoneState.SIPUsername;
        private string m_sipPassword = SIPSoftPhoneState.SIPPassword;
        private string m_sipServer = SIPSoftPhoneState.SIPServer;
        private string m_sipFromName = SIPSoftPhoneState.SIPFromName;
        private int m_audioOutDeviceIndex = SIPSoftPhoneState.AudioOutDeviceIndex;

        private SIPTransport m_sipTransport;
        private SIPUserAgent m_userAgent;
        private SIPServerUserAgent m_pendingIncomingCall;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public event Action<SIPClient> CallAnswer;                 // Fires when an outgoing SIP call is answered.
        public event Action<SIPClient> CallEnded;                  // Fires when an incoming or outgoing call is over.
        public event Action<SIPClient, string> StatusMessage;      // Fires when the SIP client has a status message it wants to inform the UI about.

        public event Action<SIPClient> RemotePutOnHold;            // Fires when the remote call party puts us on hold.	
        public event Action<SIPClient> RemoteTookOffHold;          // Fires when the remote call party takes us off hold.

        /// <summary>
        /// Once a call is established this holds the properties of the established SIP dialogue.
        /// </summary>
        public SIPDialogue Dialogue
        {
            get { return m_userAgent.Dialogue; }
        }

        public RtpAVSession MediaSession { get; private set; }

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
        /// <param name="destination">The SIP URI to place a call to. The destination can be a full SIP URI in which case the all will
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
                callURI = SIPURI.ParseSIPURIRelaxed(destination + "@" + m_sipServer);
                sipUsername = m_sipUsername;
                sipPassword = m_sipPassword;
                fromHeader = (new SIPFromHeader(m_sipFromName, new SIPURI(m_sipUsername, m_sipServer, null), null)).ToString();
            }

            StatusMessage(this, $"Starting call to {callURI}.");

            var dstEndpoint = await Task.Run(() =>
            {
                return SIPDns.Resolve(callURI, false);
            });

            if (dstEndpoint == null)
            {
                StatusMessage(this, $"Call failed, could not resolve {callURI}.");
            }
            else
            {
                StatusMessage(this, $"Call progressing, resolved {callURI} to {dstEndpoint}.");
                System.Diagnostics.Debug.WriteLine($"DNS lookup result for {callURI}: {dstEndpoint}.");
                SIPCallDescriptor callDescriptor = new SIPCallDescriptor(sipUsername, sipPassword, callURI.ToString(), fromHeader, null, null, null, null, SIPCallDirection.Out, _sdpMimeContentType, null, null);

                var audioSrcOpts = new AudioOptions
                {
                    AudioSource = AudioSourcesEnum.CaptureDevice,
                    OutputDeviceIndex = m_audioOutDeviceIndex
                };
                var videoSrcOpts = new VideoOptions
                {
                    VideoSource = VideoSourcesEnum.TestPattern,
                    SourceFile = RtpAVSession.VIDEO_TESTPATTERN,
                    SourceFramesPerSecond = VIDEO_LIVE_FRAMES_PER_SECOND
                };
                MediaSession = new RtpAVSession(audioSrcOpts, videoSrcOpts);

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
            StatusMessage(this, "Cancelling SIP call to " + m_userAgent.CallDescriptor?.Uri + ".");
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

                SDP offerSDP = SDP.ParseSDPDescription(sipRequest.Body);
                bool hasAudio = offerSDP.Media.Any(x => x.Media == SDPMediaTypesEnum.audio && x.MediaStreamStatus != MediaStreamStatusEnum.Inactive);
                bool hasVideo = offerSDP.Media.Any(x => x.Media == SDPMediaTypesEnum.video && x.MediaStreamStatus != MediaStreamStatusEnum.Inactive);

                AudioOptions audioOpts = new AudioOptions { AudioSource = AudioSourcesEnum.None };
                if (hasAudio)
                {
                    audioOpts = new AudioOptions
                    {
                        AudioSource = AudioSourcesEnum.CaptureDevice,
                        OutputDeviceIndex = m_audioOutDeviceIndex,
                        AudioCodecs = new List<SDPMediaFormatsEnum> { SDPMediaFormatsEnum.PCMU, SDPMediaFormatsEnum.PCMA  }
                    };
                }

                VideoOptions videoOpts = new VideoOptions { VideoSource = VideoSourcesEnum.None };
                if (hasVideo)
                {
                    videoOpts = new VideoOptions
                    {
                        VideoSource = VideoSourcesEnum.TestPattern,
                        SourceFile = RtpAVSession.VIDEO_TESTPATTERN,
                        SourceFramesPerSecond = VIDEO_LIVE_FRAMES_PER_SECOND
                    };
                }

                MediaSession = new RtpAVSession(audioOpts, videoOpts);

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
        public async void PutOnHold()
        {
            bool hasAudio = MediaSession.HasAudio;
            bool hasVideo = MediaSession.HasVideo;

            m_userAgent.PutOnHold();

            AudioOptions audioOnHold = (!hasAudio) ? null :
                new AudioOptions
                {
                    AudioSource = AudioSourcesEnum.Music,
                    OutputDeviceIndex = m_audioOutDeviceIndex,
                    SourceFiles = new Dictionary<SDPMediaFormatsEnum, string>
                    {
                        { SDPMediaFormatsEnum.PCMU, MUSIC_FILE_PCMU },
                        { SDPMediaFormatsEnum.PCMA, MUSIC_FILE_PCMA }
                    }
                };
            VideoOptions videoOnHold = null;

            if (hasVideo)
            {
                videoOnHold = new VideoOptions
                {
                    VideoSource = VideoSourcesEnum.TestPattern,
                    SourceFile = RtpAVSession.VIDEO_ONHOLD_TESTPATTERN,
                    SourceFramesPerSecond = VIDEO_ONHOLD_FRAMES_PER_SECOND
                };
            }

            await MediaSession.SetSources(audioOnHold, videoOnHold);

            // At this point we could stop listening to the remote party's RTP and play something 
            // else and also stop sending our microphone output and play some music.
            StatusMessage(this, "Local party put on hold");
        }

        /// <summary>
        /// Takes the remote call party off hold.
        /// </summary>
        public async void TakeOffHold()
        {
            bool hasAudio = MediaSession.HasAudio;
            bool hasVideo = MediaSession.HasVideo;

            m_userAgent.TakeOffHold();

            AudioOptions audioOnHold = (!hasAudio) ? null : new AudioOptions
            {
                AudioSource = AudioSourcesEnum.CaptureDevice,
                OutputDeviceIndex = m_audioOutDeviceIndex
            };
            VideoOptions videoOnHold = (!hasVideo) ? null : new VideoOptions
            {
                VideoSource = VideoSourcesEnum.TestPattern,
                SourceFile = RtpAVSession.VIDEO_TESTPATTERN,
                SourceFramesPerSecond = VIDEO_LIVE_FRAMES_PER_SECOND
            };
            await MediaSession.SetSources(audioOnHold, videoOnHold);

            // At this point we should reverse whatever changes we made to the media stream when we
            // put the remote call part on hold.
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
        /// A trying response has been received from the remote SIP UAS on an outgoing call.
        /// </summary>
        private void CallTrying(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            StatusMessage(this, "Call trying: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
        }

        /// <summary>
        /// A ringing response has been received from the remote SIP UAS on an outgoing call.
        /// </summary>
        private void CallRinging(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            StatusMessage(this, "Call ringing: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
        }

        /// <summary>
        /// An outgoing call was rejected by the remote SIP UAS on an outgoing call.
        /// </summary>
        private void CallFailed(ISIPClientUserAgent uac, string errorMessage, SIPResponse failureResponse)
        {
            StatusMessage(this, "Call failed: " + errorMessage + ".");
            CallFinished(null);
        }

        /// <summary>
        /// An outgoing call was successfully answered.
        /// </summary>
        /// <param name="uac">The local SIP user agent client that initiated the call.</param>
        /// <param name="sipResponse">The SIP answer response received from the remote party.</param>
        private async void CallAnswered(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            StatusMessage(this, "Call answered: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");

            // This call will allow the session source to be adjusted depending on whether the underlying
            // session has support for audio or video.
            await MediaSession.SetSources(null, null).ConfigureAwait(false);

            CallAnswer?.Invoke(this);
        }

        /// <summary>
        /// Cleans up after a SIP call has completely finished.
        /// </summary>
        private void CallFinished(SIPDialogue dialogue)
        {
            m_pendingIncomingCall = null;
            CallEnded(this);
        }

        /// <summary>
        /// An incoming call was cancelled by the caller.
        /// </summary>
        private void IncomingCallCancelled(ISIPServerUserAgent uas)
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
                Match statusCodeMatch = Regex.Match(sipFrag, @"^SIP/2\.0 (?<statusCode>\d{3})");
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
    }
}
