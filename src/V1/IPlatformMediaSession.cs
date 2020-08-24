using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace SIPSorceryMedia.Abstractions.V1
{
    public delegate void AudioEncodedSampleReadyDelegate(AudioFormat audioFormat, int durationMilliseconds, byte[] sample);
    public delegate void VideoEncodedSampleReadyDelegate(VideoFormat videoFormat, int durationMilliseconds, byte[] sample);
    public delegate void RawAudioSampleReadyDelegate(int durationMilliseconds, byte[] sample, AudioSamplingRatesEnum samplingRate);
    public delegate void RawVideoSampleReadyDelegate(int durationMilliseconds, byte[] sample);

    public delegate void SourceErrorDelegate(string error);

    public enum AudioSamplingRatesEnum
    {
        Rate8KHz = 0,
        Rate16KHz = 1,
    }

    public enum AudioCodecsEnum
    {
        PCMU = 0,
        G722 = 9,
        PCMA = 8,
        OPUS = 111,
    }

    public enum VideoCodecsEnum
    {
        VP8 = 100,
        H264 = 102,
    }

    public struct AudioFormat
    {
        public string Name { get; set; }
        public int PayloadID { get; set; }
        public AudioCodecsEnum Codec { get; set;}
        public AudioSamplingRatesEnum Rate { get; set; }
        public int BitsPerSample { get; set; }
        public Dictionary<string, string> CustomProperties { get; set; }
    }

    public struct VideoFormat
    {
        public string Name { get; set; }
        public int PayloadID { get; set; }
        public VideoCodecsEnum Codec { get; set;}
        public int Rate { get; set; }
        public int BitsPerSample { get; set; }
        public Dictionary<string, string> CustomProperties { get; set; }
    }

    public interface IPlatformMediaSession
    {
        event AudioEncodedSampleReadyDelegate OnEncodedAudioSampleReady;

        event RawAudioSampleReadyDelegate OnRawAudioSampleReady;

        event VideoEncodedSampleReadyDelegate OnEncodedVideoSampleReady;

        event RawVideoSampleReadyDelegate OnRawVideoSampleReady;

        event SourceErrorDelegate OnAudioSourceFailure;

        event SourceErrorDelegate OnVideoSourceFailure;

        List<AudioFormat> GetAudioFormats();

        void SetAudioSendingFormat(AudioFormat audioFormat);

        List<VideoFormat> GetVideoFormats();

        void SetVideoSendingFormat(VideoFormat videoFormat);

        Task Start();

        Task Close();

        void PauseAudioSource();

        void ResumeAudioSource();

        void PauseVideoSource();

        void ResumeVideoSource();

        void GotAudioRtp(IPEndPoint remoteEndPoint, int ssrc, int seqnum, int timestamp, int payloadID, bool marker, byte[] payload);

        void GotVideoRtp(IPEndPoint remoteEndPoint, int ssrc, int seqnum, int timestamp, int payloadID, bool marker, byte[] payload);

        void GotRemoteAudioSample(byte[] pcmSample);

        void GotRemoteVideoSample(int pixelFormat, byte[] bmpSample);
    }
}
