// The video codecs the chain can decode and encode with, by name:
//   vp8.net - SIPSorcery.VP8, the managed libvpx port (VP8 only, no native code);
//   ffmpeg  - SIPSorceryMedia.FFmpeg, FFmpeg's native codecs, if FFmpeg's shared libraries are
//             on the PATH (or in an FFmpeg\bin\x64 folder above the working directory).

using FFmpeg.AutoGen;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SIPSorcery.TranscodeLoop;

internal static class Codecs
{
    public const string Vp8Net = "vp8.net";
    public const string Ffmpeg = "ffmpeg";

    private static readonly Dictionary<string, (string[] Decode, string[] Encode)> _implementations = new();
    private static string? _ffmpegNote;

    /// <summary>Finds which implementations can be used on this machine. Once, at start-up.</summary>
    public static void Probe(Action<string> log)
    {
        _implementations[Vp8Net] = (["VP8"], ["VP8"]);
        try
        {
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_FATAL);
            var decode = new List<string>();
            var encode = new List<string>();
            foreach (var (name, id) in new[] { ("VP8", AVCodecID.AV_CODEC_ID_VP8), ("H264", AVCodecID.AV_CODEC_ID_H264) })
            {
                unsafe
                {
                    if (ffmpeg.avcodec_find_decoder(id) != null) decode.Add(name);
                    if (ffmpeg.avcodec_find_encoder(id) != null) encode.Add(name);
                }
            }
            _implementations[Ffmpeg] = (decode.ToArray(), encode.ToArray());
            log($"ffmpeg {ffmpeg.av_version_info()}: decodes {string.Join(", ", decode)}; encodes {string.Join(", ", encode)}");
        }
        catch (Exception ex)
        {
            _ffmpegNote = ex.Message;
            log($"ffmpeg not available ({ex.Message}); only vp8.net can be used");
        }
    }

    /// <summary>For the page's pickers: each implementation and the codecs it can do.</summary>
    public static object Capabilities() => new
    {
        implementations = _implementations.Select(kv => new { name = kv.Key, decode = kv.Value.Decode, encode = kv.Value.Encode }),
        ffmpegNote = _ffmpegNote,
    };

    public static bool CanDecode(string impl, string codec) => _implementations.TryGetValue(impl, out var c) && c.Decode.Contains(codec);
    public static bool CanEncode(string impl, string codec) => _implementations.TryGetValue(impl, out var c) && c.Encode.Contains(codec);

    public static VideoCodecsEnum Codec(string name) => name switch
    {
        "VP8" => VideoCodecsEnum.VP8,
        "H264" => VideoCodecsEnum.H264,
        _ => throw new ArgumentException($"Unknown codec {name}"),
    };

    /// <summary>
    /// The format offered for a codec. Only the one codec is offered on each leg, so the browser
    /// has to use it. H264 is Constrained Baseline, packetization mode 1, as WebRTC uses, at the
    /// lowest level that allows the frame size.
    /// </summary>
    public static VideoFormat Format(string name, int width, int height) => name switch
    {
        "VP8" => new VideoFormat(VideoCodecsEnum.VP8, 96),
        "H264" => new VideoFormat(VideoCodecsEnum.H264, 102, 90000,
            $"level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e0{H264Level(width, height):x2}"),
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

    public static AudioFormat Opus => AudioCommonlyUsedFormats.OpusWebRTC;

    /// <summary>A new codec instance. SIPSorcery's codecs do both directions in one class; each chain makes one to decode and one to encode.</summary>
    public static IVideoEncoder Create(string impl, int bitrateKbps) => impl switch
    {
        Vp8Net => new Vpx.Net.VP8Codec(),
        Ffmpeg => CreateFfmpeg(bitrateKbps),
        _ => throw new ArgumentException($"Unknown codec implementation {impl}"),
    };

    private static FFmpegVideoEncoder CreateFfmpeg(int bitrateKbps)
    {
        var codec = new FFmpegVideoEncoder();
        if (bitrateKbps > 0) codec.SetBitrate(bitrateKbps * 1000L, null, null, null);
        return codec;
    }

    /// <summary>VP8 keyframes have bit 0 of the first byte clear (RFC 6386 9.1); H264 ones carry an IDR slice (NAL type 5).</summary>
    public static bool IsKeyframe(string codec, byte[] frame)
    {
        if (frame.Length == 0) return false;
        if (codec == "VP8") return (frame[0] & 0x01) == 0;
        for (int i = 0; i + 3 < frame.Length; i++)
        {
            // Annex B start codes, 3 or 4 bytes.
            if (frame[i] == 0 && frame[i + 1] == 0 && frame[i + 2] == 1 && (frame[i + 3] & 0x1f) == 5) return true;
        }
        return false;
    }
}
