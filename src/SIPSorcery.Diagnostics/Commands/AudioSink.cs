//-----------------------------------------------------------------------------
// Filename: AudioSink.cs
//
// Description: Routes received, decoded PCM audio to one of three sinks:
//  - "play":     a spawned ffplay child process rendering to the speakers,
//                leaving the verb's stdout untouched.
//  - <file.wav>: a WAV file (header patched with the final sizes on close).
//  - "-":        raw s16le PCM on stdout. The caller is responsible for
//                routing its result object to stderr in this mode, per the
//                rule that stdout carries exactly one payload.
//
// The sink initialises lazily on the first write because the sample rate is
// only known once the audio format has been negotiated.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 12 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class AudioSink : IDisposable
{
    private enum SinkMode
    {
        None,
        Wav,
        Stdout,
        Play
    }

    private readonly SinkMode _mode;
    private readonly string? _filePath;
    private readonly ILogger _logger;
    private readonly object _lock = new();

    private Stream? _out;
    private FileStream? _wavFile;
    private Process? _ffplay;
    private bool _failed;
    private long _bytesWritten;

    public bool IsActive => _mode != SinkMode.None;
    public bool IsStdout => _mode == SinkMode.Stdout;
    public long BytesWritten { get { lock (_lock) { return _bytesWritten; } } }

    private AudioSink(SinkMode mode, string? filePath, ILogger logger)
    {
        _mode = mode;
        _filePath = filePath;
        _logger = logger;
    }

    public static AudioSink Create(string? spec, ILogger logger, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(spec))
        {
            return new AudioSink(SinkMode.None, null, logger);
        }

        if (spec == "-")
        {
            return new AudioSink(SinkMode.Stdout, null, logger);
        }

        if (spec.Equals("play", StringComparison.OrdinalIgnoreCase))
        {
            return new AudioSink(SinkMode.Play, null, logger);
        }

        if (spec.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return new AudioSink(SinkMode.Wav, spec, logger);
        }

        error = $"--audio must be \"play\", \"-\" or a .wav file path. Got \"{spec}\".";
        return new AudioSink(SinkMode.None, null, logger);
    }

    /// <summary>
    /// Writes a block of decoded mono PCM. The first call fixes the sample rate for the sink.
    /// </summary>
    public void Write(short[] pcm, int sampleRate)
    {
        if (_mode == SinkMode.None || _failed || pcm.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            if (_out == null && !Init(sampleRate))
            {
                return;
            }

            var bytes = new byte[pcm.Length * sizeof(short)];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);

            try
            {
                _out!.Write(bytes, 0, bytes.Length);
                _out.Flush();
                _bytesWritten += bytes.Length;
            }
            catch (Exception excp)
            {
                // e.g. ffplay was closed by the user, or the downstream pipe broke.
                _logger.LogWarning("Audio sink write failed, no further audio will be written: {Error}", excp.Message);
                _failed = true;
            }
        }
    }

    private bool Init(int sampleRate)
    {
        try
        {
            switch (_mode)
            {
                case SinkMode.Wav:
                    _wavFile = new FileStream(_filePath!, FileMode.Create, FileAccess.ReadWrite);
                    WavFile.WriteHeader(_wavFile, sampleRate);
                    _out = _wavFile;
                    _logger.LogDebug("Writing received audio to {FilePath} at {SampleRate}Hz.", _filePath, sampleRate);
                    return true;

                case SinkMode.Stdout:
                    _out = Console.OpenStandardOutput();
                    Console.Error.WriteLine($"Writing raw PCM to stdout: s16le, {sampleRate} Hz, mono.");
                    return true;

                case SinkMode.Play:
                    var startInfo = new ProcessStartInfo("ffplay")
                    {
                        // Note -ch_layout rather than the -ac option which was removed in ffplay 8.
                        Arguments = $"-hide_banner -loglevel error -nodisp -autoexit -f s16le -ar {sampleRate} -ch_layout mono -i -",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardError = true
                    };

                    _ffplay = Process.Start(startInfo);
                    if (_ffplay == null)
                    {
                        throw new ApplicationException("ffplay did not start.");
                    }

                    // Drain ffplay's stderr so it cannot block, surfacing anything it says as debug.
                    _ = Task.Run(async () =>
                    {
                        string? line;
                        while ((line = await _ffplay.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            _logger.LogDebug("ffplay: {Line}", line);
                        }
                    });

                    _out = _ffplay.StandardInput.BaseStream;
                    Console.Error.WriteLine($"Rendering received audio with ffplay ({sampleRate} Hz mono).");
                    return true;

                default:
                    return false;
            }
        }
        catch (Exception excp) when (_mode == SinkMode.Play)
        {
            _logger.LogError("Could not start ffplay: {Error}. Install ffmpeg (which includes ffplay) and ensure it is on the PATH.", excp.Message);
            _failed = true;
            return false;
        }
        catch (Exception excp)
        {
            _logger.LogError("Could not initialise the audio sink: {Error}", excp.Message);
            _failed = true;
            return false;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try
            {
                if (_wavFile != null)
                {
                    WavFile.PatchHeader(_wavFile, _bytesWritten);
                    _wavFile.Dispose();
                }
                else if (_ffplay != null)
                {
                    // Closing stdin lets ffplay drain its buffer and exit (-autoexit).
                    _ffplay.StandardInput.Close();
                    if (!_ffplay.WaitForExit(2000))
                    {
                        _ffplay.Kill();
                    }
                    _ffplay.Dispose();
                }
                else
                {
                    _out?.Flush();
                }
            }
            catch (Exception excp)
            {
                _logger.LogDebug("Audio sink close error: {Error}", excp.Message);
            }
        }
    }
}

/// <summary>
/// Minimal 16 bit mono PCM WAV reading/writing, just enough for the audio verbs.
/// </summary>
public static class WavFile
{
    private const int HEADER_LENGTH = 44;

    public static void WriteHeader(Stream stream, int sampleRate)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(0);                            // RIFF chunk size, patched on close.
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);                           // fmt chunk size.
        writer.Write((short)1);                     // PCM.
        writer.Write((short)1);                     // Mono.
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);               // Byte rate.
        writer.Write((short)2);                     // Block align.
        writer.Write((short)16);                    // Bits per sample.
        writer.Write("data"u8);
        writer.Write(0);                            // Data chunk size, patched on close.
    }

    public static void PatchHeader(FileStream stream, long dataLength)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        stream.Seek(4, SeekOrigin.Begin);
        writer.Write((int)(dataLength + HEADER_LENGTH - 8));
        stream.Seek(40, SeekOrigin.Begin);
        writer.Write((int)dataLength);
    }

    /// <summary>
    /// Reads a 16 bit mono PCM WAV file sampled at 8 or 16KHz, the formats the audio source
    /// can stream.
    /// </summary>
    public static bool TryReadPcm(string path, out byte[]? pcm, out int sampleRate, out string? error)
    {
        pcm = null;
        sampleRate = 0;
        error = null;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (reader.ReadBytes(4) is not [0x52, 0x49, 0x46, 0x46])    // "RIFF"
            {
                error = $"\"{path}\" is not a WAV file (missing RIFF header).";
                return false;
            }

            reader.ReadInt32();                                          // RIFF chunk size.

            if (reader.ReadBytes(4) is not [0x57, 0x41, 0x56, 0x45])    // "WAVE"
            {
                error = $"\"{path}\" is not a WAV file (missing WAVE marker).";
                return false;
            }

            short channels = 0;
            short bitsPerSample = 0;

            // Walk the chunks looking for fmt and data.
            while (stream.Position + 8 <= stream.Length)
            {
                string chunkId = new(reader.ReadChars(4));
                int chunkSize = reader.ReadInt32();

                if (chunkId == "fmt ")
                {
                    short audioFormat = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32();                                  // Byte rate.
                    reader.ReadInt16();                                  // Block align.
                    bitsPerSample = reader.ReadInt16();
                    stream.Seek(chunkSize - 16, SeekOrigin.Current);     // Skip any fmt extension.

                    if (audioFormat != 1 || channels != 1 || bitsPerSample != 16 || (sampleRate != 8000 && sampleRate != 16000))
                    {
                        error = $"\"{path}\" must be 16 bit mono PCM at 8000 or 16000 Hz " +
                                $"(found format {audioFormat}, {channels} channel(s), {bitsPerSample} bit, {sampleRate} Hz). " +
                                "Convert with: ffmpeg -i in.wav -ar 8000 -ac 1 -c:a pcm_s16le out.wav";
                        return false;
                    }
                }
                else if (chunkId == "data")
                {
                    pcm = reader.ReadBytes(chunkSize);
                    return true;
                }
                else
                {
                    stream.Seek(chunkSize, SeekOrigin.Current);
                }
            }

            error = $"\"{path}\" has no data chunk.";
            return false;
        }
        catch (Exception excp)
        {
            error = $"Could not read \"{path}\": {excp.Message}";
            return false;
        }
    }
}
