using System.Diagnostics;
using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace VideoSpike;

public static class FfSetup
{
    private static bool _done;

    /// <summary>Loads the FFmpeg 8 shared libraries from the folder that holds ffmpeg.exe on PATH.</summary>
    public static void Init()
    {
        if (_done) return;
        _done = true;
        FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_ERROR);
    }

    public static string Err(int code)
    {
        unsafe
        {
            var buf = stackalloc byte[256];
            ffmpeg.av_strerror(code, buf, 256);
            return System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)buf) ?? code.ToString();
        }
    }

    public static int Check(this int code, string what) =>
        code < 0 ? throw new ApplicationException($"{what} failed: {Err(code)}") : code;
}

/// <summary>
/// Converts a decoded frame (any software pixel format) into a caller-owned buffer, reusing the
/// swscale context while the size and formats stay the same. Same-size conversion only.
/// </summary>
public sealed unsafe class FrameWriter : IDisposable
{
    private SwsContext* _sws;
    private int _w, _h;
    private AVPixelFormat _src, _dst;

    /// <summary>Writes NV12 with a stride equal to the width: Y plane then interleaved UV.</summary>
    public void WriteNv12(AVFrame* frame, byte* dst)
    {
        int w = frame->width, h = frame->height;
        if ((AVPixelFormat)frame->format == AVPixelFormat.AV_PIX_FMT_NV12)
        {
            // Already NV12 (hardware decode output): a plane copy, no swscale.
            CopyPlane(frame->data[0], frame->linesize[0], dst, w, w, h);
            CopyPlane(frame->data[1], frame->linesize[1], dst + w * h, w, w, h / 2);
            return;
        }
        Ensure(w, h, (AVPixelFormat)frame->format, AVPixelFormat.AV_PIX_FMT_NV12);
        var dstData = new byte*[] { dst, dst + w * h, null, null };
        var dstStride = new[] { w, w, 0, 0 };
        ffmpeg.sws_scale(_sws, frame->data.ToArray(), frame->linesize.ToArray(), 0, h, dstData, dstStride);
    }

    public void WriteBgra(AVFrame* frame, byte* dst, int dstStride)
    {
        int w = frame->width, h = frame->height;
        Ensure(w, h, (AVPixelFormat)frame->format, AVPixelFormat.AV_PIX_FMT_BGRA);
        ffmpeg.sws_scale(_sws, frame->data.ToArray(), frame->linesize.ToArray(), 0, h,
            new byte*[] { dst, null, null, null }, new[] { dstStride, 0, 0, 0 });
    }

    /// <summary>Converts into a frame the caller allocated (e.g. NV12 camera to YUV420P for libvpx).</summary>
    public void Convert(AVFrame* src, AVFrame* dst)
    {
        Ensure(src->width, src->height, (AVPixelFormat)src->format, (AVPixelFormat)dst->format);
        ffmpeg.sws_scale(_sws, src->data.ToArray(), src->linesize.ToArray(), 0, src->height, dst->data.ToArray(), dst->linesize.ToArray());
    }

    private void Ensure(int w, int h, AVPixelFormat src, AVPixelFormat dst)
    {
        if (_sws != null && w == _w && h == _h && src == _src && dst == _dst) return;
        if (_sws != null) ffmpeg.sws_freeContext(_sws);
        _sws = ffmpeg.sws_getContext(w, h, src, w, h, dst, (int)SwsFlags.SWS_POINT, null, null, null);
        if (_sws == null) throw new ApplicationException($"No swscale path {src} -> {dst}.");
        (_w, _h, _src, _dst) = (w, h, src, dst);
    }

    public static void CopyPlane(byte* src, int srcStride, byte* dst, int dstStride, int rowBytes, int rows)
    {
        for (int y = 0; y < rows; y++)
        {
            Buffer.MemoryCopy(src + y * srcStride, dst + y * dstStride, rowBytes, rowBytes);
        }
    }

    public void Dispose()
    {
        if (_sws != null) ffmpeg.sws_freeContext(_sws);
        _sws = null;
    }
}

/// <summary>
/// An FFmpeg decoder for one stream. With hardware on, it asks for a D3D11VA device; FFmpeg's
/// default get_format then picks the D3D11 surface format when the GPU supports the codec and
/// quietly decodes in software when it doesn't, so <see cref="Hardware"/> reports what actually
/// happened, frame by frame. Hardware frames are copied back to system memory as NV12.
/// </summary>
public sealed unsafe class FfDecoder : IDisposable
{
    private AVCodecContext* _ctx;
    private AVFrame* _frame;
    private AVFrame* _sw;
    private AVPacket* _pkt;

    public string Name { get; }
    public bool Hardware { get; private set; }
    public long Decoded { get; private set; }
    public double TotalDecodeMs { get; private set; }
    public int Errors { get; private set; }

    public FfDecoder(VideoCodecsEnum codec, bool hardware)
    {
        var id = codec == VideoCodecsEnum.H264 ? AVCodecID.AV_CODEC_ID_H264 : AVCodecID.AV_CODEC_ID_VP8;
        var dec = ffmpeg.avcodec_find_decoder(id);
        _ctx = ffmpeg.avcodec_alloc_context3(dec);
        Name = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)dec->name) ?? "?";

        AVBufferRef* dev = null;
        if (hardware && SupportsD3D11(dec) &&
            ffmpeg.av_hwdevice_ctx_create(&dev, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0) >= 0)
        {
            _ctx->hw_device_ctx = dev;
            _ctx->thread_count = 1;
            Name += "+d3d11va";
        }
        else
        {
            // Slice threads use more cores without adding the frame of delay that frame threads do.
            _ctx->thread_count = 4;
            _ctx->thread_type = ffmpeg.FF_THREAD_SLICE;
        }
        _ctx->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;

        ffmpeg.avcodec_open2(_ctx, dec, null).Check("avcodec_open2");
        _frame = ffmpeg.av_frame_alloc();
        _sw = ffmpeg.av_frame_alloc();
        _pkt = ffmpeg.av_packet_alloc();
    }

    /// <summary>FFmpeg has D3D11VA hwaccels for H.264 (and HEVC, VP9, AV1) but none for VP8.</summary>
    private static bool SupportsD3D11(AVCodec* dec)
    {
        for (int i = 0; ; i++)
        {
            var cfg = ffmpeg.avcodec_get_hw_config(dec, i);
            if (cfg == null) return false;
            if (cfg->device_type == AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA &&
                (cfg->methods & 0x01) != 0)   // AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX
            {
                return true;
            }
        }
    }

    /// <summary>Decodes one encoded frame; calls <paramref name="onFrame"/> with a software frame for each picture out.</summary>
    public void Decode(byte[] data, Action<IntPtr> onFrame)
    {
        var sw = Stopwatch.StartNew();
        fixed (byte* p = data)
        {
            _pkt->data = p;
            _pkt->size = data.Length;
            int r = ffmpeg.avcodec_send_packet(_ctx, _pkt);
            _pkt->data = null;
            _pkt->size = 0;
            if (r < 0) { Errors++; return; }
        }

        while (ffmpeg.avcodec_receive_frame(_ctx, _frame) == 0)
        {
            AVFrame* outFrame = _frame;
            if (_frame->hw_frames_ctx != null)
            {
                Hardware = true;
                ffmpeg.av_frame_unref(_sw);
                if (ffmpeg.av_hwframe_transfer_data(_sw, _frame, 0) < 0) { Errors++; ffmpeg.av_frame_unref(_frame); continue; }
                outFrame = _sw;
            }
            else
            {
                Hardware = false;
            }
            Decoded++;
            TotalDecodeMs += sw.Elapsed.TotalMilliseconds;
            onFrame((IntPtr)outFrame);
            sw.Restart();
            ffmpeg.av_frame_unref(_frame);
        }
    }

    public void Dispose()
    {
        fixed (AVFrame** f = &_frame) ffmpeg.av_frame_free(f);
        fixed (AVFrame** f = &_sw) ffmpeg.av_frame_free(f);
        fixed (AVPacket** p = &_pkt) ffmpeg.av_packet_free(p);
        fixed (AVCodecContext** c = &_ctx) ffmpeg.avcodec_free_context(c);
    }
}

/// <summary>
/// A real-time encoder for the near end's camera. H.264 tries the GPU encoders first (NVENC,
/// Quick Sync, Media Foundation) and falls back to x264; VP8 only has libvpx, so it's software.
/// </summary>
public sealed unsafe class FfEncoder : IDisposable
{
    private AVCodecContext* _ctx;
    private AVPacket* _pkt;
    private AVFrame* _conv;
    private readonly FrameWriter _writer = new();
    private long _pts;

    public string Name { get; }
    public bool Hardware { get; }
    public long Encoded { get; private set; }
    public long Bytes { get; private set; }
    public double TotalEncodeMs { get; private set; }

    public FfEncoder(VideoCodecsEnum codec, int width, int height, int fps, int kbps)
    {
        string[] candidates = codec == VideoCodecsEnum.H264
            ? ["h264_nvenc", "h264_qsv", "h264_mf", "libx264"]
            : ["libvpx"];

        foreach (var name in candidates)
        {
            var enc = ffmpeg.avcodec_find_encoder_by_name(name);
            if (enc == null) continue;
            var ctx = ffmpeg.avcodec_alloc_context3(enc);
            ctx->width = width;
            ctx->height = height;
            ctx->time_base = new AVRational { num = 1, den = fps };
            ctx->framerate = new AVRational { num = fps, den = 1 };
            ctx->bit_rate = kbps * 1000L;
            ctx->rc_max_rate = kbps * 1000L;
            ctx->rc_buffer_size = kbps * 1000;
            ctx->gop_size = fps * 2;
            ctx->max_b_frames = 0;
            ctx->pix_fmt = name == "libvpx" ? AVPixelFormat.AV_PIX_FMT_YUV420P : AVPixelFormat.AV_PIX_FMT_NV12;
            ctx->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;

            AVDictionary* opts = null;
            AVDictionary** po = &opts;
            switch (name)
            {
                case "h264_nvenc": Opt(po, "preset", "p4"); Opt(po, "tune", "ll"); Opt(po, "zerolatency", "1"); Opt(po, "rc", "cbr"); Opt(po, "profile", "baseline"); break;
                case "h264_qsv": Opt(po, "preset", "veryfast"); Opt(po, "profile", "baseline"); break;
                case "h264_mf": Opt(po, "rate_control", "cbr"); Opt(po, "scenario", "display_remoting"); Opt(po, "hw_encoding", "1"); break;
                case "libx264": Opt(po, "preset", "veryfast"); Opt(po, "tune", "zerolatency"); Opt(po, "profile", "baseline"); break;
                case "libvpx": Opt(po, "deadline", "realtime"); Opt(po, "cpu-used", "8"); Opt(po, "lag-in-frames", "0"); Opt(po, "error-resilient", "1"); ctx->thread_count = 4; break;
            }

            int r = ffmpeg.avcodec_open2(ctx, enc, &opts);
            ffmpeg.av_dict_free(&opts);
            if (r < 0)
            {
                ffmpeg.avcodec_free_context(&ctx);
                continue;
            }

            _ctx = ctx;
            Name = name;
            Hardware = name != "libx264" && name != "libvpx";
            break;
        }

        if (_ctx == null) throw new ApplicationException($"No encoder could be opened for {codec}.");
        Name ??= "?";
        _pkt = ffmpeg.av_packet_alloc();

        if (_ctx->pix_fmt != AVPixelFormat.AV_PIX_FMT_NV12)
        {
            _conv = ffmpeg.av_frame_alloc();
            _conv->width = width;
            _conv->height = height;
            _conv->format = (int)_ctx->pix_fmt;
            ffmpeg.av_frame_get_buffer(_conv, 32).Check("av_frame_get_buffer");
        }
    }

    /// <summary>Encodes one NV12 frame; returns the encoded frame or null while the encoder buffers.</summary>
    public byte[]? Encode(AVFrame* nv12, bool keyframe)
    {
        var sw = Stopwatch.StartNew();
        AVFrame* input = nv12;
        if (_conv != null)
        {
            ffmpeg.av_frame_make_writable(_conv);
            _writer.Convert(nv12, _conv);
            input = _conv;
        }
        input->pts = _pts++;
        input->pict_type = keyframe ? AVPictureType.AV_PICTURE_TYPE_I : AVPictureType.AV_PICTURE_TYPE_NONE;

        if (ffmpeg.avcodec_send_frame(_ctx, input) < 0) return null;

        byte[]? result = null;
        while (ffmpeg.avcodec_receive_packet(_ctx, _pkt) == 0)
        {
            var bytes = new byte[_pkt->size];
            System.Runtime.InteropServices.Marshal.Copy((IntPtr)_pkt->data, bytes, 0, _pkt->size);
            result = result == null ? bytes : [.. result, .. bytes];
            ffmpeg.av_packet_unref(_pkt);
        }

        if (result != null)
        {
            Encoded++;
            Bytes += result.Length;
            TotalEncodeMs += sw.Elapsed.TotalMilliseconds;
        }
        return result;
    }

    private static void Opt(AVDictionary** d, string k, string v) => ffmpeg.av_dict_set(d, k, v, 0);

    public void Dispose()
    {
        fixed (AVCodecContext** c = &_ctx) ffmpeg.avcodec_free_context(c);
        fixed (AVPacket** p = &_pkt) ffmpeg.av_packet_free(p);
        if (_conv != null) fixed (AVFrame** f = &_conv) ffmpeg.av_frame_free(f);
        _writer.Dispose();
    }
}
