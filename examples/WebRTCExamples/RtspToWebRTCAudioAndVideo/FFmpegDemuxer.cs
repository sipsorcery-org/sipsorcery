using SIPSorcery.Net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RtspToWebRtcRestreamer
{
    /// <summary>
    /// Create FFmpeg process that split rtsp stream into two RTP stream (audio+video)
    /// </summary>
    internal class FFmpegDemuxer
    {     
        private Process _ffmpegProcess;
        private DemuxerConfig _dc;        

        public DemuxerConfig GetConfig()
        {
            return _dc;
        }
                    
        public FFmpegDemuxer(DemuxerConfig config)
        {
            _dc = config;
        }

        public void Run()
        {
            // delete old sdp file if exist
            if (File.Exists(_dc.sdpPath))
            {
                File.Delete(_dc.sdpPath);
            }
            // configure and run ffmpeg process
            
            SetupAndRunProcess(ref _ffmpegProcess, _dc.Args);

            // Verification
            // wait until sdp file created and satisfy condition
            // in my case sdp file need have two track audio and video
            // if we do not check desire condition it may lead to errors due to not fully writed sdp file
            var ready = false;
            while (!ready)
            {
                if(IsOk(_dc.sdpPath) == true)
                {
                    ready = true;
                    break;
                }
                Task.Delay(77);           
            }
        }
      
        private bool IsOk(string sdpFilePath)
        {
            try
            {
                if (File.Exists(sdpFilePath))
                {
                    var sdp = SDP.ParseSDPDescription(File.ReadAllText(sdpFilePath));
                    switch (_dc.outputStream)
                    {
                        case StreamsEnum.videoAndAudio:
                        {
                            var videoAnn = sdp.Media.First(x => x.Media == SDPMediaTypesEnum.video);
                            var audioAnn = sdp.Media.First(x => x.Media == SDPMediaTypesEnum.audio);
                            if (videoAnn != null && audioAnn != null)
                                return true;
                            return false;
                        }
                        case StreamsEnum.video:
                        {
                            var videoAnn = sdp.Media.First(x => x.Media == SDPMediaTypesEnum.video);
                            if (videoAnn != null)
                                return true;
                            return false;
                        }
                        case StreamsEnum.audio:
                        {
                            var audioAnn = sdp.Media.First(x => x.Media == SDPMediaTypesEnum.audio);
                            if (audioAnn != null)
                                return true;
                            return false;
                        }
                        default:
                            return false;
                    }
                }
                else
                    return false;
            }
            
            catch(Exception ex) { return false; }
        }

        void SetupAndRunProcess(ref Process proc, string arguments)
        {
            proc = new Process();
            proc.StartInfo.FileName = Path.Combine(_dc.FfmpegBinaryFolder, "ffmpeg");
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.OutputDataReceived += FFMpegOutputLog;
            proc.ErrorDataReceived += FFMpegOutputError;
            proc.StartInfo.Arguments = arguments;
            proc.StartInfo.WorkingDirectory = _dc.FfmpegBinaryFolder;
                   
            if (!Directory.Exists(_dc.SdpFolder)) Directory.CreateDirectory(_dc.SdpFolder);
            proc.Start();
            proc.BeginErrorReadLine();
            proc.BeginOutputReadLine();
        }

        private void FFMpegOutputError(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine(e.Data);
        }

        private void FFMpegOutputLog(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine(e.Data);
        }
    }
}
