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
        Rate8KHz = 8000,
        Rate16KHz = 16000,
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
        // Well known codecs, format ID "should" not change.
        PCMU = 0,
        G722 = 9,
        PCMA = 8,

        // Dynamic codecs, format ID can change.
        OPUS = 111,
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
        public const int DEFAULT_CLOCK_RATE = 8000;
        public const int DEFAULT_CHANNEL_COUNT = 1;

        public static readonly AudioFormat Empty = new AudioFormat() { FormatID = -1 };

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
        /// The rate used to set RTP timestamps and to be set in the SDP format
        /// attribute for this format. It should almost always be the same as the
        /// <seealso cref="ClockRate"/>. An example of where it's not is G722 which
        /// uses a sample rate of 16KHz but an RTP rate of 8KHz for historical reasons.
        /// </summary>
        /// <example>
        /// In the SDP format attribute below the RTP clock rate is 48000.
        /// a=rtpmap:109 opus/48000/2
        /// </example>
        public int RtpClockRate { get; set; }

        /// <summary>
        /// The rate used by decoded samples for this audio format.
        /// </summary>
        public int ClockRate { get; set; }

        /// <summary>
        /// The number of channels for the audio format.
        /// </summary>
        /// <example>
        /// In the SDP format attribute below the channel count is 2.
        /// Note for single channel codecs the parameter is typically omitted from the
        /// SDP format attribute.
        /// a=rtpmap:109 opus/48000/2
        /// </example>
        public int ChannelCount { get; set; }

        /// <summary>
        /// This is the string that goes in the SDP "a=fmtp" parameter.
        /// This field should be set WITHOUT the "a=fmtp:" prefix.
        /// </summary>
        /// <example>
        /// In the case below this filed should be set as "emphasis=50-15".
        /// a=fmtp:97 emphasis=50-15
        /// </example>
        public string Parameters { get; set; }

        /// <summary>
        /// Creates a new audio format based on a well known codec.
        /// </summary>
        public AudioFormat(AudioCodecsEnum codec) :
            this(codec, (int)codec)
        { 
            // G722 is the only known instance where a codecs default parameters assume 
            // a sample rate of 16KHz but 8KHz timestamps.
            if(codec == AudioCodecsEnum.G722)
            {
                ClockRate = 16000;
            }
        }

        /// <summary>
        /// Creates a new audio format based on a well known codec.
        /// </summary>
        public AudioFormat(AudioCodecsEnum codec, int clockRate) :
            this(codec, (int)codec, clockRate)
        { }

        /// <summary>
        /// Creates a new audio format based on a well known codec.
        /// </summary>
        public AudioFormat(AudioCodecsEnum codec, int formatID, int clockRate = DEFAULT_CLOCK_RATE, string parameters = null)
        {
            Codec = codec;
            FormatID = formatID;
            FormatName = codec.ToString();
            ClockRate = clockRate;
            RtpClockRate = clockRate;
            ChannelCount = DEFAULT_CHANNEL_COUNT;
            Parameters = parameters;
        }

        /// <summary>
        /// Creates a new audio format based on a dynamic codec (or an unsupported well known codec).
        /// </summary>
        public AudioFormat(
            int formatID, 
            string formatName, 
            int clockRate = DEFAULT_CLOCK_RATE, 
            int channelCount = DEFAULT_CHANNEL_COUNT, 
            string parameters = null) :
            this(formatID, formatName, clockRate, clockRate, channelCount, parameters)
        { }

        /// <summary>
        /// Creates a new audio format based on a dynamic codec (or an unsupported well known codec).
        /// </summary>
        public AudioFormat(int formatID, string formatName, int clockRate, int rtpClockRate, int channelCount, string parameters)
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
            ClockRate = clockRate;
            RtpClockRate = rtpClockRate;
            ChannelCount = channelCount;
            Parameters = parameters;

            if(Enum.TryParse<AudioCodecsEnum>(FormatName, out var audioCodec))
            {
                Codec = audioCodec;
            }
            else
            {
                Codec = AudioCodecsEnum.Dynamic;
            }
        }
    }

    public struct VideoFormat
    {
        public const int DYNAMIC_ID_MIN = 96;
        public const int DYNAMIC_ID_MAX = 127;
        public const int DEFAULT_CLOCK_RATE = 90000;

        public static readonly VideoFormat Empty = new VideoFormat() { FormatID = -1 };

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
        /// The rate used by decoded samples for this video format.
        /// </summary>
        /// <remarks>
        /// Example, 90000 is the clock rate:
        /// a=rtpmap:102 H264/90000
        /// </remarks>
        public int ClockRate { get; set; }

        /// <summary>
        /// This is the "a=fmtp" format parameter that will be set in the SDP offer/answer.
        /// This field should be set WITHOUT the "a=fmtp:0" prefix.
        /// </summary>
        /// <remarks>
        /// Example:
        /// a=fmtp:102 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f"
        /// </remarks>
        public string Parameters{ get; set; }

        /// <summary>
        /// Creates a new video format based on a well known codec.
        /// </summary>
        public VideoFormat(VideoCodecsEnum codec) :
            this(codec, (int)codec)
        { }

        /// <summary>
        /// Creates a new video format based on a well known codec.
        /// </summary>
        public VideoFormat(VideoCodecsEnum codec, int clockRate) :
            this(codec, (int)codec, clockRate)
        { }

        /// <summary>
        /// Creates a new video format based on a well known codec.
        /// </summary>
        public VideoFormat(VideoCodecsEnum codec, int formatID, int clockRate = DEFAULT_CLOCK_RATE, string parameters = null)
        {
            Codec = codec;
            FormatID = formatID;
            FormatName = codec.ToString();
            ClockRate = clockRate;
            Parameters = parameters;
        }

        /// <summary>
        /// Creates a new video format based on a dynamic codec (or an unsupported well known codec).
        /// </summary>
        public VideoFormat(int formatID, string formatName, int clockRate = DEFAULT_CLOCK_RATE, string parameters = null)
        {
            if (formatID < 0)
            {
                // Note format ID's less than the dynamic start range are allowed as the codec list
                // does not currently support all well known codecs.
                throw new ApplicationException("The format ID for an VideoFormat must be greater than 0.");
            }
            else if (formatID > DYNAMIC_ID_MAX)
            {
                throw new ApplicationException($"The format ID for an VideoFormat exceeded the maximum allowed vale of {DYNAMIC_ID_MAX}.");
            }

            FormatID = formatID;
            FormatName = formatName;
            ClockRate = clockRate;
            Parameters = parameters;

            if (Enum.TryParse<VideoCodecsEnum>(FormatName, out var videoCodec))
            {
                Codec = videoCodec;
            }
            else
            {
                Codec = VideoCodecsEnum.Dynamic;
            }
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
        /// <summary>
        /// Checks whether the encoder supports a particular audio format.
        /// </summary>
        /// <param name="format">The audio format to check support for.</param>
        /// <returns>True if the encode and decode operations are supported for the audio format.</returns>
        bool IsSupported(AudioFormat format);

        /// <summary>
        /// Encodes 16bit signed PCM samples.
        /// </summary>
        /// <param name="pcm">An array of 16 bit signed audio samples.</param>
        /// <param name="format">The audio format to encode the PCM sample to.</param>
        /// <returns>A byte array containing the encoded sample.</returns>
        byte[] EncodeAudio(short[] pcm, AudioFormat format);

        /// <summary>
        /// Decodes to 16bit signed PCM samples.
        /// </summary>
        /// <param name="encodedSample">The byte array containing the encoded sample.</param>
        /// <param name="format">The audio format of the encoded sample.</param>
        /// <returns>An array containing the 16 bit signed PCM samples.</returns>
        short[] DecodeAudio(byte[] encodedSample, AudioFormat format);
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

        Task PauseAudioSink();

        Task ResumeAudioSink();

        Task StartAudioSink();

        Task CloseAudioSink();
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

        Task PauseVideoSink();

        Task ResumeVideoSink();

        Task StartVideoSink();

        Task CloseVideoSink();
    }
}
