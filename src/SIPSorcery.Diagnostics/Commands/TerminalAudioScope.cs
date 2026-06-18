//-----------------------------------------------------------------------------
// Filename: TerminalAudioScope.cs
//
// Description: A single line terminal audio visualiser: a bank of Goertzel
// filters renders a log-spaced frequency spectrum as Unicode block glyphs,
// alongside an RMS level readout. The line is redrawn in place with a
// carriage return, which works on every terminal without ANSI cursor
// support, and is written to STDERR so it composes with any stdout payload
// (JSON result or raw PCM).
//
// Example output, redrawn at ~10fps:
//   ♪ ▂▃▅█▇▅▃▂▁▁▂▁▁▁▁▁  -18 dBFS  PCMU/8000  pkts 142
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Text;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class TerminalAudioScope : IDisposable
{
    private const int BAND_COUNT = 16;
    private const int WINDOW_SIZE = 256;             // Analysis window in samples.
    private const int RENDER_INTERVAL_MILLISECONDS = 100;
    private const double MIN_BAND_FREQUENCY = 100.0;
    private const double FLOOR_DB = -50.0;           // Spectrum bar floor.

    private static readonly char[] _glyphs = { '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

    private readonly object _lock = new();
    private readonly short[] _window = new short[WINDOW_SIZE];
    private readonly Func<string>? _statusProvider;
    private readonly bool _enabled;

    private int _windowPosn;
    private bool _windowFilled;
    private int _sampleRate;
    private double[]? _bandCoefficients;
    private Timer? _renderTimer;
    private int _lastLineLength;

    public TerminalAudioScope(Func<string>? statusProvider = null)
    {
        _statusProvider = statusProvider;

        // The scope draws over itself with carriage returns, which only makes sense on an
        // interactive terminal.
        _enabled = !Console.IsErrorRedirected || Environment.GetEnvironmentVariable("SIPSORCERY_SCOPE_FORCE") == "1";

        if (!_enabled)
        {
            Console.Error.WriteLine("The audio scope is disabled because stderr is redirected.");
            return;
        }

        try
        {
            // The block glyphs need a Unicode capable output encoding (legacy code pages render
            // them as question marks).
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // Best effort; modern terminals are UTF-8 by default.
        }

        _renderTimer = new Timer(_ => Render(), null, RENDER_INTERVAL_MILLISECONDS, RENDER_INTERVAL_MILLISECONDS);
    }

    /// <summary>
    /// Feeds decoded mono PCM into the analysis window. The first call fixes the sample rate.
    /// </summary>
    public void Write(short[] pcm, int sampleRate)
    {
        if (!_enabled || pcm.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            if (_sampleRate == 0)
            {
                _sampleRate = sampleRate;
                _bandCoefficients = CreateBandCoefficients(sampleRate);
            }

            foreach (short sample in pcm)
            {
                _window[_windowPosn] = sample;
                _windowPosn = (_windowPosn + 1) % WINDOW_SIZE;
                if (_windowPosn == 0)
                {
                    _windowFilled = true;
                }
            }
        }
    }

    /// <summary>
    /// Pre-computes the Goertzel coefficient for each log-spaced band centre frequency.
    /// </summary>
    private static double[] CreateBandCoefficients(int sampleRate)
    {
        double maxFrequency = sampleRate / 2.0 * 0.9;
        var coefficients = new double[BAND_COUNT];

        for (int band = 0; band < BAND_COUNT; band++)
        {
            // Log spacing from MIN_BAND_FREQUENCY to maxFrequency.
            double fraction = band / (double)(BAND_COUNT - 1);
            double frequency = MIN_BAND_FREQUENCY * Math.Pow(maxFrequency / MIN_BAND_FREQUENCY, fraction);
            coefficients[band] = 2.0 * Math.Cos(2.0 * Math.PI * frequency / sampleRate);
        }

        return coefficients;
    }

    private void Render()
    {
        short[] snapshot;
        double[]? coefficients;

        lock (_lock)
        {
            if (!_windowFilled || _bandCoefficients == null)
            {
                return;
            }

            // Unroll the ring buffer into time order.
            snapshot = new short[WINDOW_SIZE];
            for (int i = 0; i < WINDOW_SIZE; i++)
            {
                snapshot[i] = _window[(_windowPosn + i) % WINDOW_SIZE];
            }
            coefficients = _bandCoefficients;
        }

        // Normalise to +/-1 with a Hann window, accumulating the RMS as we go.
        var samples = new double[WINDOW_SIZE];
        double sumSquares = 0;
        for (int i = 0; i < WINDOW_SIZE; i++)
        {
            double normalised = snapshot[i] / 32768.0;
            sumSquares += normalised * normalised;
            double hann = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (WINDOW_SIZE - 1)));
            samples[i] = normalised * hann;
        }

        // Clamp the readout floor so digital silence shows as -60 rather than the log epsilon.
        double rmsDb = Math.Max(-60.0, 20.0 * Math.Log10(Math.Sqrt(sumSquares / WINDOW_SIZE) + 1e-9));

        var bars = new StringBuilder(BAND_COUNT);
        foreach (double coefficient in coefficients)
        {
            bars.Append(_glyphs[GlyphLevel(GoertzelPower(samples, coefficient))]);
        }

        string status = _statusProvider?.Invoke() ?? string.Empty;
        string line = $"♪ {bars}  {rmsDb,4:0} dBFS  {status}";

        // Redraw in place, padding to wipe any longer previous line.
        int pad = Math.Max(0, _lastLineLength - line.Length);
        _lastLineLength = line.Length;
        Console.Error.Write($"\r{line}{new string(' ', pad)}");
    }

    /// <summary>
    /// Standard Goertzel power for one band over the windowed samples.
    /// </summary>
    private static double GoertzelPower(double[] samples, double coefficient)
    {
        double s1 = 0, s2 = 0;
        foreach (double sample in samples)
        {
            double s0 = sample + coefficient * s1 - s2;
            s2 = s1;
            s1 = s0;
        }
        return s1 * s1 + s2 * s2 - coefficient * s1 * s2;
    }

    private static int GlyphLevel(double power)
    {
        // Normalise so a full scale sine in the band centre is ~0dB, then scale FLOOR_DB..0
        // onto the eight glyphs.
        double db = 10.0 * Math.Log10(power / (WINDOW_SIZE * WINDOW_SIZE / 16.0) + 1e-12);
        double fraction = Math.Clamp((db - FLOOR_DB) / -FLOOR_DB, 0.0, 1.0);
        return (int)Math.Round(fraction * (_glyphs.Length - 1));
    }

    public void Dispose()
    {
        if (_renderTimer != null)
        {
            _renderTimer.Dispose();
            _renderTimer = null;

            // Leave the last frame visible and move off the scope line so subsequent output
            // starts cleanly.
            Console.Error.WriteLine();
        }
    }
}
