//-----------------------------------------------------------------------------
// Filename: MicrophoneCapture.cs
//
// Description: Captures the local microphone with an ffmpeg process, the same way
// AudioSink uses ffplay as the cross platform speaker: ffmpeg is the audio device
// layer, so the CLI needs no native audio dependencies. ffmpeg reads the platform's
// capture API (dshow on Windows, avfoundation on macOS, PulseAudio on Linux) and
// writes 48kHz mono s16le PCM to stdout, which is handed out in 20ms blocks.
//
// Windows DirectShow has no "default" device, so without an explicit device the
// first audio capture device ffmpeg lists is used.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Cli.Common;

public sealed class MicrophoneCapture : IAsyncDisposable
{
    public const int SAMPLE_RATE = 48000;
    public const int FRAME_MS = 20;
    public const int FRAME_SAMPLES = SAMPLE_RATE / 1000 * FRAME_MS;   // 960

    private readonly string? _device;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Process? _ffmpeg;
    private Thread? _reader;
    private volatile bool _stopping;

    /// <summary>Raised on the reader thread with each 20ms block of 48kHz mono PCM.</summary>
    public event Action<short[]>? OnSamples;

    /// <summary>Completes when capture stops: ffmpeg exited (e.g. the device went away) or it was disposed.</summary>
    public Task Completion => _completion.Task;

    /// <summary>A short description of the capture input, available after <see cref="Start"/>.</summary>
    public string Description { get; private set; } = "microphone";

    /// <param name="device">The capture device name; null for the platform default (on Windows the first
    /// DirectShow audio device).</param>
    public MicrophoneCapture(string? device, ILogger logger)
    {
        _device = string.IsNullOrWhiteSpace(device) ? null : device;
        _logger = logger;
    }

    /// <summary>Starts ffmpeg capturing. Throws <see cref="InvalidOperationException"/> if no device is found or ffmpeg cannot start.</summary>
    public void Start()
    {
        string input = BuildInputArguments(out string description);
        Description = description;

        // -fflags nobuffer and a small dshow buffer keep the capture latency down for a conversation.
        var startInfo = new ProcessStartInfo("ffmpeg")
        {
            Arguments = $"-hide_banner -loglevel error -fflags nobuffer {input} -ac 1 -ar {SAMPLE_RATE} -f s16le -",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            _ffmpeg = Process.Start(startInfo) ?? throw new InvalidOperationException("ffmpeg did not start.");
        }
        catch (Exception excp) when (excp is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Could not start ffmpeg for the microphone: {excp.Message}. Install ffmpeg and ensure it is on the PATH.");
        }

        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await _ffmpeg.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                _logger.LogWarning("ffmpeg(mic): {Line}", line);
            }
        });

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "mic-capture" };
        _reader.Start();

        _logger.LogDebug("Microphone capture started: {Description}.", Description);
    }

    private void ReadLoop()
    {
        var stdout = _ffmpeg!.StandardOutput.BaseStream;
        var bytes = new byte[FRAME_SAMPLES * sizeof(short)];
        try
        {
            while (!_stopping)
            {
                int filled = 0;
                while (filled < bytes.Length)
                {
                    int n = stdout.Read(bytes, filled, bytes.Length - filled);
                    if (n <= 0)
                    {
                        return;   // ffmpeg exited.
                    }
                    filled += n;
                }

                var samples = new short[FRAME_SAMPLES];
                Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
                OnSamples?.Invoke(samples);
            }
        }
        catch (Exception excp)
        {
            if (!_stopping)
            {
                _logger.LogWarning("Microphone capture stopped: {Error}", excp.Message);
            }
        }
        finally
        {
            _completion.TrySetResult();
        }
    }

    private string BuildInputArguments(out string description)
    {
        if (OperatingSystem.IsWindows())
        {
            string device = _device ?? FindFirstDirectShowAudioDevice()
                ?? throw new InvalidOperationException("No microphone found (ffmpeg lists no DirectShow audio capture devices). Pass one with --mic.");
            description = $"dshow \"{device}\"";
            return $"-f dshow -audio_buffer_size 40 -i audio=\"{device}\"";
        }

        if (OperatingSystem.IsMacOS())
        {
            string device = _device ?? "default";
            description = $"avfoundation {device}";
            return $"-f avfoundation -i \":{device}\"";
        }

        string pulseDevice = _device ?? "default";
        description = $"pulse {pulseDevice}";
        return $"-f pulse -i \"{pulseDevice}\"";
    }

    /// <summary>Returns the first audio device ffmpeg's DirectShow device list reports, or null.</summary>
    private static string? FindFirstDirectShowAudioDevice()
    {
        try
        {
            using var list = Process.Start(new ProcessStartInfo("ffmpeg", "-hide_banner -list_devices true -f dshow -i dummy")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });
            if (list == null)
            {
                return null;
            }

            string output = list.StandardError.ReadToEnd();
            list.WaitForExit(5000);
            return ParseFirstDirectShowAudioDevice(output);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Picks the first <c>"Name" (audio)</c> entry out of ffmpeg's dshow device list output.</summary>
    public static string? ParseFirstDirectShowAudioDevice(string listDevicesOutput)
    {
        var match = Regex.Match(listDevicesOutput, "\"([^\"]+)\"\\s+\\(audio\\)");
        return match.Success ? match.Groups[1].Value : null;
    }

    public ValueTask DisposeAsync()
    {
        _stopping = true;
        if (_ffmpeg != null)
        {
            try
            {
                if (!_ffmpeg.HasExited)
                {
                    _ffmpeg.Kill();
                }
                _ffmpeg.Dispose();
            }
            catch (Exception excp)
            {
                _logger.LogDebug("Microphone ffmpeg close error: {Error}", excp.Message);
            }
        }

        _reader?.Join(2000);
        _completion.TrySetResult();
        return ValueTask.CompletedTask;
    }
}
