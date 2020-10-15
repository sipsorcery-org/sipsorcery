using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace SIPSorceryMedia.Abstractions.V1
{
    public delegate void EncodedSampleDelegate(uint durationRtpUnits, byte[] sample);
    public delegate void RawAudioSampleDelegate(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample);
    public delegate void RawVideoSampleDelegate(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat);
    public delegate void VideoSinkSampleDecodedDelegate(byte[] sample, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat);
    public delegate void SourceErrorDelegate(string errorMessage);

    public enum AudioSamplingRatesEnum
    {
        Rate8KHz = 0,
        Rate16KHz = 1,
    }

    public enum VideoPixelFormatsEnum
    {
        Rgb = 0,        // 24 bits per pixel.
        Bgr = 1,        // 24 bits per pixel.
        Bgra = 2,       // 32 bits per pixel.
        I420 = 3
    }

    public enum AudioCodecsEnum
    {
        PCMU = 0,
        G722 = 9,
        PCMA = 8,
        OPUS = 111,
        L8 = 118,       // 8 bit signed linear.
        L16 = 119,      // 16 bit Signed linear.

        /// <summary>
        /// Use for audio codecs that are not supported in the above list. A dynamic
        /// codec requires at least a format attribute to be specified with it and 
        /// it will be up to the application to encode/decode.
        /// </summary>
        Dynamic = 128,
    }

    public enum VideoCodecsEnum
    {
        VP8 = 100,
        H264 = 102,

        /// <summary>
        /// Use for video codecs that are not supported in the above list. A dynamic
        /// codec requires at least a format attribute to be specified with it and 
        /// it will be up to the application to encode/decode.
        /// </summary>
        Dynamic = 128,
    }

    public struct AudioFormat
    {
        public const int DYNAMIC_ID_MIN = 96;
        public const int DYNAMIC_ID_MAX = 127;

        public AudioCodecsEnum Codec { get; set; }

        /// <summary>
        /// The format ID for the codec. If this is a well known codec it should be set to the
        /// value from the codec enum. If the codec is a dynamic it must be set between 96–127
        /// inclusive.
        /// </summary>
        public int FormatID { get; set; }

        /// <summary>
        /// The official name for the codec. This field is critical for dynamic codecs
        /// where it is used to match the codecs in the SDP offer/answer.
        /// </summary>
        public string FormatName { get; set; }

        /// <summary>
        /// This is the "a=rtpmap" format attribute that will be set in the SDP offer/answer.
        /// </summary>
        /// <remarks>
        /// Example:
        /// a=rtpmap:109 opus/48000/2
        /// </remarks>
        public string FormatAttribute { get; set; }

        /// <summary>
        /// This is the "a=fmtp" format parameter that will be set in the SDP offer/answer.
        /// </summary>
        public string FormatParameterAttribute { get; set; }

        /// <summary>
        /// Creates a new audio format based on a well known codec.
        /// </summary>
        public AudioFormat(AudioCodecsEnum codec, string formatAttribute = null, string formatParameterAttribute = null)
        {
            Codec = codec;
            FormatID = (int)codec;
            FormatName = codec.ToString();
            FormatAttribute = formatAttribute;
            FormatParameterAttribute = formatParameterAttribute;
        }

        /// <summary>
        /// Creates a new audio format based on a dynamic codec (or an unsupported well known codec).
        /// </summary>
        public AudioFormat(int formatID, string formatName, string formatAttribute, string formatParameterAttribute)
        {
            if (formatID < 0)
            {
                // Note format ID's less than the dynamic start range are allowed as the codec list
                // does not currently support all well known codecs.
                throw new ApplicationException("The format ID for an AudioFormat must be greater than 0.");
            }
            else if (formatID > DYNAMIC_ID_MAX)
            {
                throw new ApplicationException($"The format ID for an AudioFormat exceeded the maximum allowed vale of {DYNAMIC_ID_MAX}.");
            }

            FormatID = formatID;
            FormatName = formatName;
            Codec = AudioCodecsEnum.Dynamic;
            FormatAttribute = formatAttribute;
            FormatParameterAttribute = formatParameterAttribute;
        }
    }

    public struct VideoFormat
    {
        public const int DYNAMIC_ID_MIN = 96;
        public const int DYNAMIC_ID_MAX = 127;

        public VideoCodecsEnum Codec { get; set; }

        /// <summary>
        /// The format ID for the codec. If this is a well known codec it should be set to the
        /// value from the codec enum. If the codec is a dynamic it must be set between 96–127
        /// inclusive.
        /// </summary>
        public int FormatID { get; set; }

        /// <summary>
        /// The official name for the codec. This field is critical for dynamic codecs
        /// where it is used to match the codecs in the SDP offer/answer.
        /// </summary>
        public string FormatName { get; set; }

        /// <summary>
        /// This is the "a=rtpmap" format attribute that will be set in the SDP offer/answer.
        /// </summary>
        /// <remarks>
        /// Example:
        /// a=rtpmap:102 H264/90000
        /// </remarks>
        public string FormatAttribute { get; set; }

        /// <summary>
        /// This is the "a=fmtp" format parameter that will be set in the SDP offer/answer.
        /// </summary>
        /// <remarks>
        /// Example:
        /// a=fmtp:102 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f"
        /// </remarks>
        public string FormatParameterAttribute { get; set; }

        /// <summary>
        /// Creates a new video format based on a well known codec.
        /// </summary>
        public VideoFormat(VideoCodecsEnum codec, string formatAttribute = null, string formatParameterAttribute = null)
        {
            Codec = codec;
            FormatID = (int)codec;
            FormatName = codec.ToString();
            FormatAttribute = formatAttribute;
            FormatParameterAttribute = formatParameterAttribute;
        }

        /// <summary>
        /// Creates a new video format based on a dynamic codec (or an unsupported well known codec).
        /// </summary>
        public VideoFormat(int formatID, string formatName, string formatAttribute, string formatParameterAttribute)
        {
            if (formatID < 0)
            {
                // Note format ID's less than the dynamic start range are allowed as the codec list
                // does not currently support all well known codecs.
                throw new ApplicationException("The format ID for a VideoFormat must be greater than 0.");
            }
            else if (formatID > DYNAMIC_ID_MAX)
            {
                throw new ApplicationException($"The format ID for a VideoFormat exceeded the maximum allowed vale of {DYNAMIC_ID_MAX}.");
            }

            FormatID = formatID;
            FormatName = formatName;
            Codec = VideoCodecsEnum.Dynamic;
            FormatAttribute = formatAttribute;
            FormatParameterAttribute = formatParameterAttribute;
        }
    }

    public class MediaEndPoints
    {
        public IAudioSource AudioSource { get; set; }
        public IAudioSink AudioSink { get; set; }
        public IVideoSource VideoSource { get; set; }
        public IVideoSink VideoSink { get; set; }
    }

    public interface IAudioEncoder
    {
        bool IsSupported(AudioCodecsEnum codec);

        byte[] EncodeAudio(byte[] pcm, AudioCodecsEnum codec, AudioSamplingRatesEnum sampleRate);

        byte[] EncodeAudio(short[] pcm, AudioCodecsEnum codec, AudioSamplingRatesEnum sampleRate);

        byte[] DecodeAudio(byte[] encodedSample, AudioCodecsEnum codec, AudioSamplingRatesEnum sampleRate);
    }

    public struct VideoSample
    {
        public uint Width;
        public uint Height;
        public byte[] Sample;
    }

    public interface IVideoEncoder
    {
        bool IsSupported(VideoCodecsEnum codec);

        byte[] EncodeVideo(int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec);

        void ForceKeyFrame();

        IEnumerable<VideoSample> DecodeVideo(byte[] encodedSample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec);
    }

    public interface IAudioSource
    {
        event EncodedSampleDelegate OnAudioSourceEncodedSample;

        event RawAudioSampleDelegate OnAudioSourceRawSample;

        event SourceErrorDelegate OnAudioSourceError;

        Task PauseAudio();

        Task ResumeAudio();

        Task StartAudio();

        Task CloseAudio();

        List<AudioFormat> GetAudioSourceFormats();

        void SetAudioSourceFormat(AudioFormat audioFormat);

        void RestrictFormats(Func<AudioFormat, bool> filter);

        void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample);

        bool HasEncodedAudioSubscribers();

        bool IsAudioSourcePaused();
    }

    public interface IAudioSink
    {
        event SourceErrorDelegate OnAudioSinkError;

        List<AudioFormat> GetAudioSinkFormats();

        void SetAudioSinkFormat(AudioFormat audioFormat);

        void GotAudioRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload);

        void RestrictFormats(Func<AudioFormat, bool> filter);
    }

    public interface IVideoSource
    {
        event EncodedSampleDelegate OnVideoSourceEncodedSample;

        event RawVideoSampleDelegate OnVideoSourceRawSample;

        event SourceErrorDelegate OnVideoSourceError;

        Task PauseVideo();

        Task ResumeVideo();

        Task StartVideo();

        Task CloseVideo();

        List<VideoFormat> GetVideoSourceFormats();

        void SetVideoSourceFormat(VideoFormat videoFormat);

        void RestrictFormats(Func<VideoFormat, bool> filter);

        void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat);

        void ForceKeyFrame();

        bool HasEncodedVideoSubscribers();

        bool IsVideoSourcePaused();
    }

    public interface IVideoSink
    {
        /// <summary>
        /// This event will be fired by the sink after is decodes a video frame from the RTP stream.
        /// </summary>
        event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;

        void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload);

        void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] payload);

        List<VideoFormat> GetVideoSinkFormats();

        void SetVideoSinkFormat(VideoFormat videoFormat);

        void RestrictFormats(Func<VideoFormat, bool> filter);
    }
}
