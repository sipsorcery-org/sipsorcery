// The video codecs the chain can decode and encode with, by name:
//   vp8.net - SIPSorcery.VP8, the managed libvpx port (VP8 only, no native code);
//   av1.net - SIPSorcery.AV1, the managed port of libaom's realtime encoder (AV1, encode only);
//   h264.net - SIPSorcery.H264, the managed OpenH264 port (H264 Constrained Baseline, decode only
//             until its encoder is ported);
//   ffmpeg - SIPSorceryMedia.FFmpeg, FFmpeg's native codecs, if FFmpeg's shared libraries are
//             on the PATH (or in an FFmpeg\bin\x64 folder above the working directory).

using FFmpeg.AutoGen;
using SIPSorcery.AV1;
using SIPSorcery.H264;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SIPSorcery.TranscodeLoop;

internal static class Codecs
{
    public const string Vp8Net = "vp8.net";
    public const string Av1Net = "av1.net";
    public const string H264Net = "h264.net";
    public const string Ffmpeg = "ffmpeg";

    /// <summary>Every codec SIPSorcery can carry over RTP, in the order the pickers show them.</summary>
    public static readonly string[] All = ["VP8", "VP9", "H264", "H265", "AV1"];

    private static readonly Dictionary<string, (string[] Decode, string[] Encode)> _implementations = new();
    // FFmpeg's own codec for each, as "decode:VP9" -> "vp9", "encode:AV1" -> "libsvtav1".
    private static readonly Dictionary<string, string> _ffmpegNames = new();
    private static string? _ffmpegNote;

    /// <summary>Finds which implementations can be used on this machine. Once, at start-up.</summary>
    public static void Probe(Action<string> log)
    {
        _implementations[Vp8Net] = (["VP8"], ["VP8"]);
        _implementations[Av1Net] = ([], ["AV1"]);
        // Decode only: SIPSorcery.H264's encoder isn't ported yet.
        _implementations[H264Net] = (["H264"], []);
        try
        {
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_FATAL);
            var decode = new List<string>();
            var encode = new List<string>();
            foreach (var name in All)
            {
                var id = FfmpegId(name);
                unsafe
                {
                    var dec = ffmpeg.avcodec_find_decoder(id);
                    // As FFmpegVideoEncoder picks: SVT-AV1 for AV1 if the build has it (libaom, FFmpeg's
                    // default, is far too slow for real time), otherwise FFmpeg's default for the codec.
                    var enc = id == AVCodecID.AV_CODEC_ID_AV1 ? ffmpeg.avcodec_find_encoder_by_name("libsvtav1") : null;
                    if (enc == null) enc = ffmpeg.avcodec_find_encoder(id);
                    if (dec != null)
                    {
                        decode.Add(name);
                        _ffmpegNames[$"decode:{name}"] = Name(dec);
                    }
                    if (enc != null)
                    {
                        encode.Add(name);
                        _ffmpegNames[$"encode:{name}"] = Name(enc);
                    }
                }
            }
            _implementations[Ffmpeg] = (decode.ToArray(), encode.ToArray());
            log($"ffmpeg {ffmpeg.av_version_info()}: decodes {string.Join(", ", decode.Select(c => $"{c} ({_ffmpegNames[$"decode:{c}"]})"))}; " +
                $"encodes {string.Join(", ", encode.Select(c => $"{c} ({_ffmpegNames[$"encode:{c}"]})"))}");
        }
        // No FFmpeg, or not one FFmpeg.AutoGen can use: FFmpegInit throws DllNotFoundException or
        // ApplicationException, and a missing or mismatched library the others.
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                                      or NotSupportedException or ApplicationException)
        {
            _ffmpegNote = ex.Message;
            log($"ffmpeg not available ({ex.Message}); only vp8.net, av1.net and h264.net can be used");
        }
    }

    private static unsafe string Name(AVCodec* codec) => System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)codec->name) ?? "?";

    private static AVCodecID FfmpegId(string name) => name switch
    {
        "VP8" => AVCodecID.AV_CODEC_ID_VP8,
        "VP9" => AVCodecID.AV_CODEC_ID_VP9,
        "H264" => AVCodecID.AV_CODEC_ID_H264,
        "H265" => AVCodecID.AV_CODEC_ID_HEVC,
        "AV1" => AVCodecID.AV_CODEC_ID_AV1,
        _ => throw new ArgumentException($"Unknown codec {name}"),
    };

    /// <summary>For the page's pickers: each implementation and the codecs it can do.</summary>
    public static object Capabilities() => new
    {
        codecs = All,
        implementations = _implementations.Select(kv => new { name = kv.Key, decode = kv.Value.Decode, encode = kv.Value.Encode }),
        ffmpegNames = _ffmpegNames,
        ffmpegNote = _ffmpegNote,
    };

    public static bool CanDecode(string impl, string codec) => _implementations.TryGetValue(impl, out var c) && c.Decode.Contains(codec);
    public static bool CanEncode(string impl, string codec) => _implementations.TryGetValue(impl, out var c) && c.Encode.Contains(codec);

    /// <summary>What actually does the work, for the log: "ffmpeg (libsvtav1)", "av1.net".</summary>
    public static string Describe(string impl, string codec, bool encode) =>
        impl == Ffmpeg && _ffmpegNames.TryGetValue($"{(encode ? "encode" : "decode")}:{codec}", out var name) ? $"{impl} ({name})" : impl;

    public static VideoCodecsEnum Codec(string name) => name switch
    {
        "VP8" => VideoCodecsEnum.VP8,
        "VP9" => VideoCodecsEnum.VP9,
        "H264" => VideoCodecsEnum.H264,
        "H265" => VideoCodecsEnum.H265,
        "AV1" => VideoCodecsEnum.AV1,
        _ => throw new ArgumentException($"Unknown codec {name}"),
    };

    /// <summary>
    /// The format offered for a codec. Only the one codec is offered on each leg, so the browser
    /// has to use it. Payload types and parameters are the ones Chromium offers, with the level
    /// raised to what the frame size needs:
    /// H264 is Constrained Baseline, packetization mode 1, as WebRTC uses; VP9 profile 0 (8-bit
    /// 4:2:0); H265 Main, single RTP stream; AV1 Main profile.
    /// </summary>
    public static VideoFormat Format(string name, int width, int height) => name switch
    {
        "VP8" => new VideoFormat(VideoCodecsEnum.VP8, 96),
        "VP9" => new VideoFormat(VideoCodecsEnum.VP9, 98, 90000, "profile-id=0"),
        "H264" => new VideoFormat(VideoCodecsEnum.H264, 102, 90000,
            $"level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e0{H264Level(width, height):x2}"),
        "H265" => new VideoFormat(VideoCodecsEnum.H265, 49, 90000,
            $"level-id={H265Level(width, height)};profile-id=1;tier-flag=0;tx-mode=SRST"),
        "AV1" => new VideoFormat(VideoCodecsEnum.AV1, 45, 90000, $"level-idx={Av1Level(width, height)};profile=0;tier=0"),
        _ => throw new ArgumentException($"Unknown codec {name}"),
    };

    /// <summary>
    /// The lowest H264 level (as profile-level-id's last byte, 10 x the level) whose largest frame
    /// (in 16x16 macroblocks, H.264 table A-1) holds this size: 3.1 for up to 720p, as browsers
    /// offer, 4.0 for 1080p, 5.1 for 4K, 6.0 beyond.
    /// </summary>
    private static int H264Level(int width, int height)
    {
        int macroblocks = (width + 15) / 16 * ((height + 15) / 16);
        return macroblocks switch
        {
            <= 3600 => 31,
            <= 8192 => 40,
            <= 36864 => 51,
            _ => 60,
        };
    }

    /// <summary>
    /// The lowest H265 level (as level-id, 30 x the level) whose largest picture (H.265 table A.8,
    /// MaxLumaPs) holds this size: 3.1 up to 720p, 4 for 1080p, 5 for 4K, 6 beyond.
    /// </summary>
    private static int H265Level(int width, int height) => (width * height) switch
    {
        <= 983_040 => 93,
        <= 2_228_224 => 120,
        <= 8_912_896 => 150,
        _ => 180,
    };

    /// <summary>
    /// The lowest AV1 level (as level-idx, seq_level_idx) whose largest picture (AV1 annex A.3,
    /// MaxPicSize) holds this size: 3.1 (5) up to 720p, as browsers offer, 4.0 (8) for 1080p, 5.0
    /// (12) for 4K, 6.0 (16) beyond.
    /// </summary>
    private static int Av1Level(int width, int height) => (width * height) switch
    {
        <= 1_065_024 => 5,
        <= 2_359_296 => 8,
        <= 8_912_896 => 12,
        _ => 16,
    };

    public static AudioFormat Opus => AudioCommonlyUsedFormats.OpusWebRTC;

    /// <summary>
    /// A new codec instance. SIPSorcery's codecs do both directions in one class; each chain makes
    /// one to decode and one to encode. A bitrate of 0 leaves the implementation's default.
    /// </summary>
    public static IVideoEncoder Create(string impl, int bitrateKbps) => impl switch
    {
        Vp8Net => new Vpx.Net.VP8Codec(),
        Av1Net => bitrateKbps > 0 ? new AV1VideoEncoder(new Av1rt.Av1EncoderOptions { TargetBitrateKbps = bitrateKbps }) : new AV1VideoEncoder(),
        H264Net => new H264Codec(),
        Ffmpeg => CreateFfmpeg(bitrateKbps),
        _ => throw new ArgumentException($"Unknown codec implementation {impl}"),
    };

    private static FFmpegVideoEncoder CreateFfmpeg(int bitrateKbps)
    {
        var codec = new FFmpegVideoEncoder();
        if (bitrateKbps > 0) codec.SetBitrate(bitrateKbps * 1000L, null, null, null);
        return codec;
    }

    /// <summary>Whether an encoded frame is a keyframe: one a decoder can start from.</summary>
    public static bool IsKeyframe(string codec, byte[] frame)
    {
        if (frame.Length == 0) return false;
        return codec switch
        {
            // Bit 0 of the first byte clear (RFC 6386 9.1).
            "VP8" => (frame[0] & 0x01) == 0,
            // frame_type in the uncompressed header (VP9 bitstream spec 6.2).
            "VP9" => Vp9Packetiser.IsKeyFrame(frame),
            // An IDR slice, NAL type 5.
            "H264" => AnnexBNalTypes(frame, 1).Any(t => (t & 0x1f) == 5),
            // An IRAP picture: BLA, IDR or CRA, NAL types 16 to 21 (H.265 table 7-1).
            "H265" => AnnexBNalTypes(frame, 2).Any(t => ((t >> 1) & 0x3f) is >= 16 and <= 21),
            "AV1" => IsAv1KeyFrame(frame),
            _ => false,
        };
    }

    /// <summary>The first byte of each NAL unit in an Annex B access unit (after 3 or 4 byte start codes).</summary>
    private static IEnumerable<byte> AnnexBNalTypes(byte[] frame, int headerBytes)
    {
        for (int i = 0; i + 2 + headerBytes < frame.Length; i++)
        {
            if (frame[i] == 0 && frame[i + 1] == 0 && frame[i + 2] == 1) yield return frame[i + 3];
        }
    }

    /// <summary>
    /// A temporal unit (OBUs with size fields, as SIPSorcery's depacketiser and the encoders give
    /// them) holding a frame whose header says KEY_FRAME: show_existing_frame 0 and frame_type 0,
    /// the header's first bits when reduced_still_picture_header is off, as it is for video.
    /// </summary>
    private static bool IsAv1KeyFrame(byte[] tu)
    {
        int pos = 0;
        while (pos < tu.Length)
        {
            byte header = tu[pos];
            int type = (header >> 3) & 0x0f;
            bool extension = (header & 0x04) != 0, hasSize = (header & 0x02) != 0;
            pos += extension ? 2 : 1;
            int size = tu.Length - pos;
            if (hasSize)
            {
                size = 0;
                for (int shift = 0; pos < tu.Length; shift += 7)
                {
                    byte b = tu[pos++];
                    size |= (b & 0x7f) << shift;
                    if ((b & 0x80) == 0) break;
                }
            }
            // OBU_FRAME_HEADER (3) or OBU_FRAME (6).
            if ((type == 3 || type == 6) && pos < tu.Length) return (tu[pos] & 0xe0) == 0;
            pos += size;
        }
        return false;
    }
}
