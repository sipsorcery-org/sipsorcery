using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RtspToWebRtcRestreamer
{
    internal class DemuxerConfig
    {
        private Dictionary<StreamsEnum, string> commandTemplateDic = new Dictionary<StreamsEnum, string>()
        {
            {StreamsEnum.video, "-re -i {0} -an -vcodec {1} -ssrc {2} -f rtp rtp://{3}:{4} -sdp_file {5}" },
            {StreamsEnum.audio, "-re -i {0} -vn -acodec {1} -ssrc {2} -f rtp rtp://{3}:{4} -sdp_file {5}"  },
            {StreamsEnum.videoAndAudio, "-use_wallclock_as_timestamps 1 -i {0} -map 0:v -c:v {1} -ssrc {2} -f rtp rtp://{3}:{4} -map 0:a -c:a {5} -ssrc {6} -f rtp rtp://{3}:{7}  -sdp_file {8} -y"},
            {StreamsEnum.videoAndAudioUdp, "-use_wallclock_as_timestamps 1 -rtsp_transport udp -i {0} -map 0:v -c:v {1} -ssrc {2} -f rtp rtp://{3}:{4} -map 0:a -c:a {5} -ssrc {6} -f rtp rtp://{3}:{7} -sdp_file {8} -y" }
        };
        // exe path
        public string SdpFolder { get; private set; } = "A:\\temp\\sdp";
        public string SdpFileName { get; private set; } = "stream.sdp";
        public string FfmpegBinaryFolder { get; private set; } = "C:\\Program Files\\ffmpeg\\bin";



        // ffmpeg command settings
       
        public string rtspUrl = "rtsp://admin:HelloWorld4@192.168.1.64:554/ISAPI/Streaming/Channels/101";
        public string vcodec = "h264";
        public string acodec = "pcm_alaw";
        public int audioPort = 5204;
        public int videoPort = 5202;
        public uint audioSsrc = 50;
        public uint videoSsrc = 60;
        public string serverIP = "127.0.0.1";

        public StreamsEnum outputStream = StreamsEnum.videoAndAudio;

        public string sdpPath  
        {
            get { return Path.Combine(SdpFolder, SdpFileName); } 
        }

        public string Args
        {
            get
            {
                switch (outputStream)
                {
                    case StreamsEnum.videoAndAudio:
                        return String.Format(commandTemplateDic[StreamsEnum.videoAndAudio],
                            rtspUrl,
                            vcodec,
                            videoSsrc,
                            serverIP,
                            videoPort,
                            acodec,
                            audioSsrc,
                            audioPort,
                            sdpPath
                            );
                    case StreamsEnum.videoAndAudioUdp:
                        return String.Format(commandTemplateDic[StreamsEnum.videoAndAudioUdp],
                            rtspUrl,
                            vcodec,
                            videoSsrc,
                            serverIP,
                            videoPort,
                            acodec,
                            audioSsrc,
                            audioPort,
                            sdpPath
                            );
                    case StreamsEnum.audio:
                        return String.Format(commandTemplateDic[StreamsEnum.audio],
                            rtspUrl,                            
                            acodec,
                            audioSsrc,
                            serverIP,
                            audioPort,
                            sdpPath
                            );
                    case StreamsEnum.video:
                        return String.Format(commandTemplateDic[StreamsEnum.video],
                            rtspUrl,
                            vcodec,
                            videoSsrc,
                            serverIP,
                            videoPort,
                            sdpPath
                            );
                    default:
                        return "";
                }
                    
                
            }
        }
    }
}
