using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace SIPSorceryMedia.Abstractions.V1
{
    public delegate void AudioEncodedSampleDelegate(AudioFormat audioFormat, uint durationRtpUnits, byte[] sample);
    public delegate void VideoEncodedSampleDelegate(VideoFormat videoFormat, uint durationRtpUnits, byte[] sample);
    public delegate void RawAudioSampleDelegate(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample);
    public delegate void RawVideoSampleDelegate(uint durationMilliseconds, byte[] sample);
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

    public enum PixelFormatsEnum
    {
        RGB = 0,
        BGR = 1,
        I420 = 2
    }

    public struct AudioFormat
    {
        public string Name { get; set; }
        public int PayloadID { get; set; }
        public AudioCodecsEnum Codec { get; set; }
        public AudioSamplingRatesEnum Rate { get; set; }
        public int BitsPerSample { get; set; }
        public Dictionary<string, string> CustomProperties { get; set; }
    }

    public struct VideoFormat
    {
        public string Name { get; set; }
        public int PayloadID { get; set; }
        public VideoCodecsEnum Codec { get; set; }
        public int FrameRate { get; set; }
        public int BitsPerSample { get; set; }
        public Dictionary<string, string> CustomProperties { get; set; }
    }

    public class MediaEndPoints
    {
        public IAudioSource AudioSource { get; set; }
        public IAudioSink AudioSink { get; set; }
        public IVideoSource VideoSource { get; set; }
        public IVideoSink VideoSink { get; set; }
    }

    public interface IAudioSource
    {
        event SourceErrorDelegate OnAudioSourceFailure;

        event AudioEncodedSampleDelegate OnAudioSourceEncodedSample;

        event RawAudioSampleDelegate OnAudioSourceRawSample;

        Task PauseAudio();

        Task ResumeAudio();

        Task StartAudio();

        Task CloseAudio();

        List<AudioFormat> GetAudioSourceFormats();

        void SetAudioSourceFormat(AudioFormat audioFormat);
    }

    public interface IAudioSink
    {
        bool EncodedSamplesOnly { get; set; }

        AudioSamplingRatesEnum AudioPlaybackRate { get; set; }

        List<AudioFormat> GetAudioSinkFormats();

        void SetAudioSinkFormat(AudioFormat audioFormat);

        void GotAudioRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload);

        void GotAudioSample(byte[] pcmSample);
    }

    public interface IVideoSource
    {
        event SourceErrorDelegate OnVideoSourceFailure;

        event VideoEncodedSampleDelegate OnVideoSourceEncodedSample;

        /// <summary>
        /// No point enabling using this until there is at least one fully C# (no native library) codec
        /// that can encode raw video samples.
        /// </summary>
        event RawVideoSampleDelegate OnVideoSourceRawSample;

        Task PauseVideo();

        Task ResumeVideo();

        Task StartVideo();

        Task CloseVideo();

        List<VideoFormat> GetVideoSourceFormats();

        void SetVideoSourceFormat(VideoFormat videoFormat);
    }

    public interface IVideoSink
    {
        /// <summary>
        /// This event will be fired by the sink after is decodes a video frame from the RTP stream.
        /// </summary>
        event RawVideoSampleDelegate OnVideoSinkRawSample;

        //bool EncodedSamplesOnly { get; set; }

        void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload);

        //void GotVideoSample(PixelFormatsEnum pixelFormat, byte[] bmpSample);

        List<VideoFormat> GetVideoSinkFormats();

        void SetVideoSinkFormat(VideoFormat videoFormat);
    }
}
