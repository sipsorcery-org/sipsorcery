using System.Diagnostics;
using FFmpeg.AutoGen;

namespace VideoSpike;

/// <summary>
/// The near end's camera for the C# paths, delivering NV12 frames on a capture thread. "Real" opens
/// the first DirectShow camera at 1280x720@30 (NV12 if offered, otherwise whatever the camera gives,
/// converted); "Fake" generates a moving pattern so runs can be repeated without a camera.
/// </summary>
public sealed unsafe class CameraSource : IDisposable
{
    public const int Width = 1280, Height = 720, Fps = 30;

    private readonly CameraKind _kind;
    private readonly string _device;
    private Thread? _thread;
    private volatile bool _stop;

    public long Captured;
    public string Description { get; private set; } = "";

    /// <summary>Called on the capture thread with (AVFrame* NV12 frame, capture time).</summary>
    public event Action<IntPtr, double>? Frame;

    public CameraSource(CameraKind kind, string device = "MX Brio")
    {
        _kind = kind;
        _device = device;
    }

    public void Start()
    {
        _thread = new Thread(_kind == CameraKind.Real ? RunReal : RunFake) { IsBackground = true, Name = "camera" };
        _thread.Start();
    }

    private void RunFake()
    {
        Description = $"fake {Width}x{Height}@{Fps}";
        var frame = ffmpeg.av_frame_alloc();
        frame->width = Width; frame->height = Height; frame->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
        ffmpeg.av_frame_get_buffer(frame, 32).Check("av_frame_get_buffer");
        var rnd = new Random(1);
        var start = Stopwatch.GetTimestamp();
        for (long n = 0; !_stop; n++)
        {
            ffmpeg.av_frame_make_writable(frame);
            byte* y = frame->data[0];
            int stride = frame->linesize[0];
            int bar = (int)(n * 8 % Width);
            for (int row = 0; row < Height; row++)
            {
                byte* line = y + row * stride;
                byte v = (byte)(16 + (row + n) % 200);
                new Span<byte>(line, Width).Fill(v);
                new Span<byte>(line + bar, Math.Min(64, Width - bar)).Fill(235);
                // A little noise so the encoder has real work to do, like a real sensor.
                for (int k = 0; k < 16; k++) line[rnd.Next(Width)] = (byte)rnd.Next(16, 235);
            }
            new Span<byte>(frame->data[1], frame->linesize[1] * Height / 2).Fill(128);

            Interlocked.Increment(ref Captured);
            Frame?.Invoke((IntPtr)frame, Clock.NowMs);

            var due = start + (long)((n + 1) * Stopwatch.Frequency / (double)Fps);
            while (Stopwatch.GetTimestamp() < due && !_stop) Thread.Sleep(1);
        }
        ffmpeg.av_frame_free(&frame);
    }

    private void RunReal()
    {
        AVFormatContext* fmt = null;
        AVDictionary* opts = null;
        ffmpeg.av_dict_set(&opts, "video_size", $"{Width}x{Height}", 0);
        ffmpeg.av_dict_set(&opts, "framerate", Fps.ToString(), 0);
        ffmpeg.av_dict_set(&opts, "pixel_format", "nv12", 0);
        ffmpeg.av_dict_set(&opts, "rtbufsize", "64M", 0);
        var input = ffmpeg.av_find_input_format("dshow");
        int r = ffmpeg.avformat_open_input(&fmt, $"video={_device}", input, &opts);
        if (r < 0)
        {
            // The camera may not offer NV12 at this size; let it pick (often MJPEG).
            ffmpeg.av_dict_free(&opts);
            opts = null;
            ffmpeg.av_dict_set(&opts, "video_size", $"{Width}x{Height}", 0);
            ffmpeg.av_dict_set(&opts, "framerate", Fps.ToString(), 0);
            ffmpeg.av_dict_set(&opts, "rtbufsize", "64M", 0);
            ffmpeg.avformat_open_input(&fmt, $"video={_device}", input, &opts).Check("open camera");
        }
        ffmpeg.av_dict_free(&opts);
        ffmpeg.avformat_find_stream_info(fmt, null).Check("camera stream info");

        AVCodec* dec = null;
        int si = ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &dec, 0).Check("camera stream");
        var ctx = ffmpeg.avcodec_alloc_context3(dec);
        ffmpeg.avcodec_parameters_to_context(ctx, fmt->streams[si]->codecpar);
        ffmpeg.avcodec_open2(ctx, dec, null).Check("camera decoder");
        Description = $"{_device} {ctx->width}x{ctx->height} {ffmpeg.avcodec_get_name(ctx->codec_id)}/{(AVPixelFormat)ctx->pix_fmt}";

        var pkt = ffmpeg.av_packet_alloc();
        var raw = ffmpeg.av_frame_alloc();
        var nv12 = ffmpeg.av_frame_alloc();
        nv12->width = ctx->width; nv12->height = ctx->height; nv12->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
        ffmpeg.av_frame_get_buffer(nv12, 32).Check("av_frame_get_buffer");
        using var writer = new FrameWriter();

        while (!_stop && ffmpeg.av_read_frame(fmt, pkt) >= 0)
        {
            if (pkt->stream_index == si && ffmpeg.avcodec_send_packet(ctx, pkt) >= 0)
            {
                while (ffmpeg.avcodec_receive_frame(ctx, raw) == 0)
                {
                    AVFrame* outFrame = raw;
                    if ((AVPixelFormat)raw->format != AVPixelFormat.AV_PIX_FMT_NV12)
                    {
                        ffmpeg.av_frame_make_writable(nv12);
                        writer.Convert(raw, nv12);
                        outFrame = nv12;
                    }
                    Interlocked.Increment(ref Captured);
                    Frame?.Invoke((IntPtr)outFrame, Clock.NowMs);
                    ffmpeg.av_frame_unref(raw);
                }
            }
            ffmpeg.av_packet_unref(pkt);
        }

        ffmpeg.av_packet_free(&pkt);
        ffmpeg.av_frame_free(&raw);
        ffmpeg.av_frame_free(&nv12);
        ffmpeg.avcodec_free_context(&ctx);
        ffmpeg.avformat_close_input(&fmt);
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(2000);
    }
}

/// <summary>
/// Sends the camera to every SIP leg. Shared: one encoder, its output fanned out to every call.
/// Per call: one encoder per call, the way each call would encode independently if it adapted its
/// own bitrate. Encoders run in parallel so a per-call run isn't penalised by serial encoding.
/// </summary>
public sealed unsafe class CameraHub : IDisposable
{
    private readonly List<SipLeg> _legs;
    private readonly List<FfEncoder> _encoders = new();
    private readonly CameraMode _mode;
    private readonly double _t0 = Clock.NowMs;
    private int _keyEvery;
    private long _n;

    public long SentToSip;

    public CameraHub(RunConfig cfg, List<SipLeg> legs)
    {
        _legs = legs;
        _mode = cfg.CameraMode;
        int count = cfg.CameraMode == CameraMode.Shared ? 1 : legs.Count;
        for (int i = 0; i < count; i++)
        {
            _encoders.Add(new FfEncoder(cfg.Codec, CameraSource.Width, CameraSource.Height, CameraSource.Fps, cfg.CameraBitrateKbps));
        }
        _keyEvery = CameraSource.Fps * 2;
    }

    public IReadOnlyList<FfEncoder> Encoders => _encoders;

    public void OnFrame(IntPtr framePtr, double captureMs)
    {
        var frame = (AVFrame*)framePtr;
        uint ts = (uint)((captureMs - _t0) * 90);
        bool key = _n++ % _keyEvery == 0;

        if (_encoders.Count == 1)
        {
            var encoded = _encoders[0].Encode(frame, key);
            if (encoded == null) return;
            foreach (var leg in _legs) Send(leg, ts, encoded);
        }
        else
        {
            Parallel.For(0, _encoders.Count, i =>
            {
                var encoded = _encoders[i].Encode((AVFrame*)framePtr, key);
                if (encoded != null) Send(_legs[i], ts, encoded);
            });
        }
    }

    private void Send(SipLeg leg, uint ts, byte[] encoded)
    {
        if (leg.Rtp.IsClosed) return;
        leg.Rtp.SendVideoAt(ts, encoded);
        Interlocked.Increment(ref SentToSip);
    }

    public void Dispose()
    {
        foreach (var e in _encoders) e.Dispose();
    }
}
