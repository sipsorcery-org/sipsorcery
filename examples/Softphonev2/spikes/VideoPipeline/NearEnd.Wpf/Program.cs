using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFmpeg.AutoGen;
using SIPSorcery.SIP;
using VideoSpike;

namespace NearEnd.Wpf;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var cfg = RunConfig.Parse(args);
        Clock.UseHighResolutionTimer();
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new MainWindow(cfg));
    }
}

/// <summary>
/// Path 3, the native baseline. C# decodes with FFmpeg exactly as path 2 does, converts to BGRA on
/// the decode thread, and the UI thread copies the newest frame of each tile into a WriteableBitmap
/// on every WPF render tick. There's no browser engine at all, so this is the cost of a plain
/// native video grid. (A zero-copy D3D11 → D3DImage renderer would be cheaper still; it isn't built.)
/// </summary>
internal sealed class MainWindow : Window
{
    private const double RefreshMs = 1000.0 / 60;

    private sealed unsafe class Tile
    {
        public required int Index;
        public required string Kind;
        public required Image Image;
        public required Border Frame;
        public readonly object Lock = new();
        public readonly FrameWriter Writer = new();
        public byte* Front, Back;
        public int Capacity, Width, Height, Seq, ShownSeq;
        public double FrontRecvMs;
        public WriteableBitmap? Bitmap;
        public readonly PresentTracker Tracker = new();
        public bool First;

        /// <summary>Decode thread: convert into the back buffer, then swap it to the front.</summary>
        public void Write(IntPtr framePtr, double recvMs)
        {
            var f = (AVFrame*)framePtr;
            int w = f->width, h = f->height, bytes = w * h * 4;
            if (Back == null || Capacity < bytes)
            {
                lock (Lock)
                {
                    if (Back != null) NativeMemory.Free(Back);
                    if (Front != null) NativeMemory.Free(Front);
                    Back = (byte*)NativeMemory.Alloc((nuint)bytes);
                    Front = (byte*)NativeMemory.Alloc((nuint)bytes);
                    Capacity = bytes;
                }
            }
            Writer.WriteBgra(f, Back, w * 4);
            lock (Lock)
            {
                var swap = Front; Front = Back; Back = swap;
                Width = w; Height = h; FrontRecvMs = recvMs; Seq++;
            }
        }
    }

    private readonly RunConfig _cfg;
    private readonly BenchLink _link = new();
    private readonly SIPTransport _transport = SipLeg.CreateTransport();
    private readonly List<SipLeg> _legs = new();
    private readonly List<Tile> _tiles = new();
    private readonly List<DecodePipeline> _decoders = new();
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromRgb(17, 17, 17)) };
    private CameraSource? _camera;
    private CameraHub? _hub;
    private bool _ready, _measuring;
    private double _windowStart;
    private long[] _recvAtStart = [], _decAtStart = [];
    private double[] _decMsAtStart = [];
    private long _capAtStart, _sentAtStart;
    private long[] _encAtStart = [], _bytesAtStart = [];
    private double[] _encMsAtStart = [];

    public MainWindow(RunConfig cfg)
    {
        _cfg = cfg;
        Title = $"Video spike - {cfg.Label}";
        Topmost = true;   // same as the WebView2 host, so all three paths run under the same conditions
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 40; Top = 40;
        Content = _canvas;
        SizeToContent = SizeToContent.WidthAndHeight;
        SourceInitialized += (_, _) =>
        {
            // Size the client area in physical pixels, like the WebView2 host.
            var dpi = VisualTreeHelper.GetDpi(this);
            _canvas.Width = cfg.Width / dpi.DpiScaleX;
            _canvas.Height = cfg.Height / dpi.DpiScaleY;
        };
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => Shutdown();
    }

    private async Task StartAsync()
    {
        try
        {
            FfSetup.Init();
            int count = _cfg.Streams + (_cfg.Camera == CameraKind.Off ? 0 : 1);
            var rects = GridLayout(count, _canvas.Width, _canvas.Height);
            for (int i = 0; i < count; i++)
            {
                var image = new Image { Stretch = Stretch.UniformToFill };
                var border = new Border
                {
                    Width = rects[i].W, Height = rects[i].H, Background = Brushes.Black, ClipToBounds = true,
                    CornerRadius = new CornerRadius(6), Child = image,
                };
                bool self = i >= _cfg.Streams;
                if (self) image.RenderTransform = new ScaleTransform(-1, 1, rects[i].W / 2, rects[i].H / 2);
                Canvas.SetLeft(border, rects[i].X);
                Canvas.SetTop(border, rects[i].Y);
                _canvas.Children.Add(border);
                _tiles.Add(new Tile { Index = i, Kind = self ? "self" : "remote", Image = image, Frame = border });
            }

            _legs.AddRange(await SipLeg.CallAllAsync(_transport, _cfg));
            BenchLink.Log($"{_legs.Count} calls up");

            foreach (var leg in _legs)
            {
                var tile = _tiles[leg.Index];
                var dp = new DecodePipeline(leg.Index, _cfg.Codec, _cfg.HwDecode);
                dp.Frame += (f, recv) => tile.Write(f, recv);
                _decoders.Add(dp);
                leg.Rtp.OnVideoFrameReceived += (_, _, frame, _) => dp.Push(frame, Clock.NowMs);
            }

            if (_cfg.Camera != CameraKind.Off)
            {
                var self = _tiles[^1];
                _hub = new CameraHub(_cfg, _legs);
                _camera = new CameraSource(_cfg.Camera);
                _camera.Frame += (f, ms) =>
                {
                    self.Write(f, ms);
                    _hub.OnFrame(f, ms);
                };
                _camera.Start();
            }

            CompositionTarget.Rendering += OnRender;

            _link.Start += () => Dispatcher.BeginInvoke(() =>
            {
                foreach (var t in _tiles) t.Tracker.Reset();
                _recvAtStart = _decoders.Select(d => Interlocked.Read(ref d.Received)).ToArray();
                _decAtStart = _decoders.Select(d => d.Decoder.Decoded).ToArray();
                _decMsAtStart = _decoders.Select(d => d.Decoder.TotalDecodeMs).ToArray();
                _capAtStart = _camera == null ? 0 : Interlocked.Read(ref _camera.Captured);
                _sentAtStart = _hub == null ? 0 : Interlocked.Read(ref _hub.SentToSip);
                _encAtStart = _hub?.Encoders.Select(e => e.Encoded).ToArray() ?? [];
                _encMsAtStart = _hub?.Encoders.Select(e => e.TotalEncodeMs).ToArray() ?? [];
                _bytesAtStart = _hub?.Encoders.Select(e => e.Bytes).ToArray() ?? [];
                _windowStart = Clock.NowMs;
                _measuring = true;
            });
            _link.Stop += () => Dispatcher.InvokeAsync(Collect).Task;
            _link.Exit += () => Dispatcher.BeginInvoke(Close);
            _link.Run();

            _ = Task.Delay(20000).ContinueWith(_ => Dispatcher.BeginInvoke(() => SetReady("timeout")));
        }
        catch (Exception ex)
        {
            BenchLink.Log($"startup failed: {ex}");
            BenchLink.Send("FAILED " + ex.Message.Replace('\n', ' '));
            Close();
        }
    }

    private void SetReady(string why)
    {
        if (_ready) return;
        _ready = true;
        BenchLink.Log($"ready ({why})");
        _link.Ready();
    }

    /// <summary>UI thread, once per WPF frame: copy each tile's newest frame into its bitmap.</summary>
    private unsafe void OnRender(object? sender, EventArgs e)
    {
        var now = Clock.NowMs;
        foreach (var t in _tiles)
        {
            int seq;
            double recv;
            lock (t.Lock)
            {
                seq = t.Seq;
                if (seq == t.ShownSeq || t.Front == null) continue;
                if (t.Bitmap == null || t.Bitmap.PixelWidth != t.Width || t.Bitmap.PixelHeight != t.Height)
                {
                    t.Bitmap = new WriteableBitmap(t.Width, t.Height, 96, 96, PixelFormats.Bgra32, null);
                    t.Image.Source = t.Bitmap;
                }
                t.Bitmap.WritePixels(new Int32Rect(0, 0, t.Width, t.Height), (IntPtr)t.Front, t.Width * t.Height * 4, t.Width * 4);
                recv = t.FrontRecvMs;
            }

            if (_measuring)
            {
                if (t.ShownSeq > 0) t.Tracker.Dropped += Math.Max(0, seq - t.ShownSeq - 1);
                t.Tracker.OnPresented(now + RefreshMs, now + RefreshMs - recv);
            }
            t.ShownSeq = seq;
            if (!t.First)
            {
                t.First = true;
                if (_tiles.All(x => x.First)) SetReady("all tiles showing");
            }
        }
    }

    private NearEndStats Collect()
    {
        _measuring = false;
        var window = (Clock.NowMs - _windowStart) / 1000.0;
        var stats = new NearEndStats
        {
            WindowSeconds = Math.Round(window, 2),
            LatencyMethod = "SIP receive in C# to the WPF render tick that copied the frame into its bitmap, " +
                            "plus one display refresh (16.7 ms) for the compositor",
        };
        foreach (var t in _tiles)
        {
            var ts = new TileStats
            {
                Tile = t.Index, Kind = t.Kind, FramesPresented = t.Tracker.Presented, FramesDropped = t.Tracker.Dropped,
                PresentedFps = Math.Round(t.Tracker.Presented / window, 1), Stalls = t.Tracker.Stalls,
                LongestGapMs = Math.Round(t.Tracker.LongestGapMs, 1), Latency = t.Tracker.Latency,
                Width = t.Width, Height = t.Height,
            };
            if (t.Kind == "remote")
            {
                var d = _decoders[t.Index];
                ts.FramesReceived = Interlocked.Read(ref d.Received) - _recvAtStart[t.Index];
                ts.FramesDecoded = d.Decoder.Decoded - _decAtStart[t.Index];
                ts.FramesDropped += Math.Max(0, ts.FramesReceived - ts.FramesDecoded);
                ts.AvgDecodeMs = ts.FramesDecoded > 0 ? Math.Round((d.Decoder.TotalDecodeMs - _decMsAtStart[t.Index]) / ts.FramesDecoded, 2) : null;
                ts.Decoder = d.Decoder.Name;
                ts.HardwareDecode = d.Decoder.Hardware;
            }
            stats.Tiles.Add(ts);
        }

        if (_camera != null && _hub != null)
        {
            long encoded = _hub.Encoders.Select((e, i) => e.Encoded - _encAtStart[i]).Sum();
            double encMs = _hub.Encoders.Select((e, i) => e.TotalEncodeMs - _encMsAtStart[i]).Sum();
            long bytes = _hub.Encoders.Select((e, i) => e.Bytes - _bytesAtStart[i]).Sum();
            stats.Camera = new CameraStats
            {
                Source = _camera.Description,
                FramesCaptured = Interlocked.Read(ref _camera.Captured) - _capAtStart,
                Encoders = _hub.Encoders.Count,
                FramesEncoded = encoded,
                EncodeFpsPerEncoder = Math.Round(encoded / (double)_hub.Encoders.Count / window, 1),
                AvgEncodeMs = encoded > 0 ? Math.Round(encMs / encoded, 2) : null,
                SentKbpsPerEncoder = Math.Round(bytes * 8 / 1000.0 / _hub.Encoders.Count / window, 0),
                Encoder = _hub.Encoders[0].Name,
                HardwareEncode = _hub.Encoders[0].Hardware,
                FramesSentToSip = Interlocked.Read(ref _hub.SentToSip) - _sentAtStart,
                Width = CameraSource.Width,
                Height = CameraSource.Height,
            };
            stats.Camera.CaptureFps = Math.Round(stats.Camera.FramesCaptured / window, 1);
            stats.Camera.SentFpsPerLeg = Math.Round(stats.Camera.FramesSentToSip / (double)_legs.Count / window, 1);
        }
        return stats;
    }

    private static List<(double X, double Y, double W, double H)> GridLayout(int count, double width, double height, double gap = 4)
    {
        int cols = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (int)Math.Ceiling(count / (double)cols);
        double w = (width - gap * (cols + 1)) / cols, h = (height - gap * (rows + 1)) / rows;
        return Enumerable.Range(0, count).Select(i => (gap + i % cols * (w + gap), gap + i / cols * (h + gap), w, h)).ToList();
    }

    private void Shutdown()
    {
        CompositionTarget.Rendering -= OnRender;
        foreach (var leg in _legs) leg.Hangup();
        _camera?.Dispose();
        foreach (var d in _decoders) d.Dispose();
        _hub?.Dispose();
        Thread.Sleep(300);
        _transport.Shutdown();
    }
}
