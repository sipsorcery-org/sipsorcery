using Org.BouncyCastle.Security;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;

namespace RtspToWebRtcRestreamer
{   
    /// <summary>
    /// Listne RTP streams created by ffmpeg process
    /// </summary>
    internal class FFmpegListener
    {
        private Thread _videoThread;
        private Thread _audioThread;
        private RTPSession _videoRTP;
        private RTPSession _audioRTP;      
        private DemuxerConfig _dc;
        public bool ready = false;
        public MediaStreamTrack? videoTrack { get; private set; }
        public MediaStreamTrack? audioTrack { get; private set; }
        public SDPAudioVideoMediaFormat videoFormatRTP { get; private set; }
        public SDPAudioVideoMediaFormat audioFormatRTP { get; private set; }
        

        public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket> OnAudioRtpPacketReceived;
        public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket> OnVideoRtpPacketReceived;

        public FFmpegListener( DemuxerConfig demuxConfig)
        {         
            _dc = demuxConfig;
        }

        public async void Run(CancellationToken token)
        {
            switch (_dc.outputStream)
            {
                case StreamsEnum.videoAndAudio:
                case StreamsEnum.none:
                {
                    ListenAudio();
                    ListenVideo();
                    break;
                }
                case StreamsEnum.audio:
                {
                    ListenAudio();
                    break;
                }
                case StreamsEnum.video:
                {
                    ListenVideo();
                    break;
                }
            }
            ready = true;
        }

        private void ListenAudio()
        {
            var sdpAudio = SDP.ParseSDPDescription(File.ReadAllText(_dc.sdpPath));
            var videoAnn = sdpAudio.Media.Find(x => x.Media == SDPMediaTypesEnum.video);
            var audioAnn = sdpAudio.Media.Find(x => x.Media == SDPMediaTypesEnum.audio);
            sdpAudio.Media.Remove(videoAnn);
            // configure audio listener
            audioFormatRTP = audioAnn.MediaFormats.Values.First();
            audioTrack = new MediaStreamTrack(
                                        SDPMediaTypesEnum.audio,
                                        false,
                                        new List<SDPAudioVideoMediaFormat> { audioFormatRTP },
                                        MediaStreamStatusEnum.SendRecv);
            audioTrack.Ssrc = _dc.audioSsrc;
            _audioRTP = new RTPSession(false, false, false, IPAddress.Loopback, _dc.audioPort);
            _audioRTP.AcceptRtpFromAny = true;
            _audioRTP.SetRemoteDescription(SIPSorcery.SIP.App.SdpType.answer, sdpAudio);
            _audioRTP.addTrack(audioTrack);

            _audioRTP.OnRtpPacketReceived += HndlAudioPacketReceived;

            _audioThread = new Thread(() => _audioRTP.Start());
            _audioThread.Start();
        }

        private void ListenVideo()
        {
            // create sdpVideo            
            var sdpVideo = SDP.ParseSDPDescription(File.ReadAllText(_dc.sdpPath));
            var videoAnn = sdpVideo.Media.Find(x => x.Media == SDPMediaTypesEnum.video);
            // !- its necessary to delete audio announcment from whole sdp file otherwise RTP session will not catch frames
            var audioAnn = sdpVideo.Media.Find(x => x.Media == SDPMediaTypesEnum.audio);
            sdpVideo.Media.Remove(audioAnn);

            // configure video listener
            videoFormatRTP = videoAnn.MediaFormats.Values.First();
            videoTrack = new MediaStreamTrack(
                                        SDPMediaTypesEnum.video,
                                        false,
                                        new List<SDPAudioVideoMediaFormat> { videoFormatRTP },
                                        MediaStreamStatusEnum.RecvOnly);
            videoTrack.Ssrc = _dc.videoSsrc;
            _videoRTP = new RTPSession(false, false, false, IPAddress.Loopback, _dc.videoPort);
            _videoRTP.AcceptRtpFromAny = true;
            _videoRTP.SetRemoteDescription(SIPSorcery.SIP.App.SdpType.answer, sdpVideo);
            _videoRTP.addTrack(videoTrack);

            _videoRTP.OnRtpPacketReceived += HndlVideoPacketReceived;
            _videoThread = new Thread(() => _videoRTP.Start());
            _videoThread.Start();
        }
                                           
        private void HndlVideoPacketReceived(IPEndPoint arg1, SDPMediaTypesEnum arg2, RTPPacket arg3)
        {
            if (OnVideoRtpPacketReceived == null) return;
            OnVideoRtpPacketReceived.Invoke(arg1, arg2, arg3);
        }
        private void HndlAudioPacketReceived(IPEndPoint arg1, SDPMediaTypesEnum arg2, RTPPacket arg3)
        {
            if (OnAudioRtpPacketReceived == null) return;
            OnAudioRtpPacketReceived.Invoke(arg1, arg2, arg3);
        }
    }
}
 