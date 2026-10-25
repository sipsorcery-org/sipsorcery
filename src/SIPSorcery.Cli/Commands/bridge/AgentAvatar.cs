//-----------------------------------------------------------------------------
// Filename: AgentAvatar.cs
//
// Description: An agent's optional video face (--avatar), wrapping one of the two
// renderers behind the small surface the agents need:
//
//   wav2lip - the default: the photoreal Max Headroom head from the maxheadroom repo
//             (Wav2LipAvatarRenderer). The Wav2Lip model animates the mouth from the
//             speech audio itself, so the agent hands over its voice with
//             BeginSpeech / PushSpeech / EndSpeech.
//   max     - the SkiaSharp cartoon (MaxHeadroomVideoSource), lip-synced from
//             visemes or the speech envelope, which the agents drive through MaxFace.
//
// Wav2Lip needs its model and persona files (see Wav2LipAvatarRenderer) and the
// DirectML onnxruntime build, which is Windows only. A plain --avatar falls back to
// max with a warning when either is missing; --avatar wav2lip insists.
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

using System.IO;
using Concentus;
using Microsoft.Extensions.Logging;
using SIPSorcery.Cli.Commands.Route;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SIPSorcery.Cli.Commands.Bridge;

public sealed class AgentAvatar : IDisposable
{
    public const string WAV2LIP = "wav2lip";
    public const string MAX = "max";

    /// <summary>The value meaning "the default avatar": --avatar given without a name.</summary>
    public const string DEFAULT = "";

    /// <summary>The accepted --avatar names.</summary>
    public static readonly string[] Names = { WAV2LIP, MAX };

    private const int WAV2LIP_SAMPLE_RATE = MelSpectrogram.SampleRate;   // 16 kHz

    private readonly IVideoSource _video;
    private readonly Wav2LipAvatarRenderer? _wav2lip;
    private readonly VideoFormat _h264 = RouteVideoFormats.H264;
    private readonly object _speechLock = new();
    private IResampler? _resampler;
    private int _resamplerInputRate;
    private bool _inSpeech;
    private uint _videoTimestamp;

    /// <summary>Raised with each encoded H264 frame of the face.</summary>
    public event Action<MediaFrame>? OnFrame;

    /// <summary>The selected avatar: <see cref="WAV2LIP"/> or <see cref="MAX"/>.</summary>
    public string Name { get; }

    /// <summary>The cartoon face, for the agents to drive from visemes or the envelope; null for wav2lip.</summary>
    public MaxHeadroomVideoSource? MaxFace { get; }

    private AgentAvatar(string name, IVideoSource video, MaxHeadroomVideoSource? maxFace, Wav2LipAvatarRenderer? wav2lip)
    {
        Name = name;
        _video = video;
        MaxFace = maxFace;
        _wav2lip = wav2lip;

        _video.RestrictFormats(vf => vf.Codec == VideoCodecsEnum.H264);
        _video.OnVideoSourceEncodedSample += (durationRtpUnits, sample) =>
        {
            _videoTimestamp += durationRtpUnits;
            OnFrame?.Invoke(MediaFrame.ForVideo(sample, _videoTimestamp, _h264, durationRtpUnits));
        };
    }

    /// <summary>
    /// Checks an --avatar name and resolves the default, without building anything. Throws
    /// <see cref="EdgeException"/> for an unknown name, or for wav2lip asked for explicitly when it
    /// cannot run here.
    /// </summary>
    public static string Resolve(string requested, ILogger logger)
    {
        string name = requested.Trim().ToLowerInvariant();

        if (name == MAX)
        {
            return MAX;
        }

        if (name != WAV2LIP && name != DEFAULT)
        {
            throw new EdgeException($"Unknown avatar '{requested}'. Choose one of: {string.Join(", ", Names)} (default {WAV2LIP}).");
        }

        string? unavailable = Wav2LipUnavailableReason();
        if (unavailable == null)
        {
            return WAV2LIP;
        }

        if (name == WAV2LIP)
        {
            throw new EdgeException($"The wav2lip avatar cannot run: {unavailable} Use --avatar max for the cartoon face.");
        }

        logger.LogWarning("The default wav2lip avatar cannot run ({Reason}) Using the max avatar instead; pass --avatar max to choose it explicitly.",
            unavailable.TrimEnd('.'));
        return MAX;
    }

    /// <summary>Builds the face for an --avatar name (see <see cref="Resolve"/>).</summary>
    /// <param name="audioDrivenMouth">For the max face: drive the mouth from the speech envelope (no visemes).</param>
    public static AgentAvatar Create(string requested, bool audioDrivenMouth, ILogger logger)
    {
        string name = Resolve(requested, logger);

        if (name == WAV2LIP)
        {
            try
            {
                var wav2lip = new Wav2LipAvatarRenderer(new FFmpegVideoEncoder());
                return new AgentAvatar(WAV2LIP, wav2lip, null, wav2lip);
            }
            catch (Exception excp) when (requested.Trim().Length == 0)
            {
                logger.LogWarning("The default wav2lip avatar failed to load ({Error}). Using the max avatar instead.", excp.Message);
            }
            catch (Exception excp)
            {
                throw new EdgeException($"The wav2lip avatar failed to load: {excp.Message}");
            }
        }

        var max = new MaxHeadroomVideoSource(new FFmpegVideoEncoder()) { AudioDrivenMouth = audioDrivenMouth };
        return new AgentAvatar(MAX, max, max, null);
    }

    /// <summary>Why the Wav2Lip head cannot run on this machine, or null if it can.</summary>
    private static string? Wav2LipUnavailableReason()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "it needs the DirectML onnxruntime build, which is Windows only.";
        }

        if (!File.Exists(Wav2LipAvatarRenderer.DefaultModelPath()))
        {
            return $"its model was not found at {Wav2LipAvatarRenderer.DefaultModelPath()} (set WAV2LIP_ONNX).";
        }

        if (!File.Exists(Wav2LipAvatarRenderer.DefaultPersonaPath()))
        {
            return $"its persona image was not found at {Wav2LipAvatarRenderer.DefaultPersonaPath()} (set NEURAL_PERSONA).";
        }

        return null;
    }

    /// <summary>Starts rendering: the face is drawn and encoded continuously, idle between utterances.</summary>
    public Task StartAsync()
    {
        _video.SetVideoSourceFormat(_h264);
        return _video.StartVideo();
    }

    public Task CloseAsync() => _video.CloseVideo();

    /// <summary>The agent has started an utterance. Only the wav2lip face uses it.</summary>
    public void BeginSpeech()
    {
        if (_wav2lip == null)
        {
            return;
        }

        lock (_speechLock)
        {
            if (!_inSpeech)
            {
                _inSpeech = true;
                _wav2lip.BeginSpeech();
            }
        }
    }

    /// <summary>
    /// Hands over the agent's voice for the current utterance (16-bit mono PCM at any rate; resampled
    /// to Wav2Lip's 16 kHz). Starts an utterance if none is open. Only the wav2lip face uses it.
    /// </summary>
    public void PushSpeech(short[] pcm, int sampleRate)
    {
        if (_wav2lip == null || pcm.Length == 0)
        {
            return;
        }

        lock (_speechLock)
        {
            if (!_inSpeech)
            {
                _inSpeech = true;
                _wav2lip.BeginSpeech();
            }

            _wav2lip.PushAudio(sampleRate == WAV2LIP_SAMPLE_RATE ? pcm : Resample(pcm, sampleRate), WAV2LIP_SAMPLE_RATE);
        }
    }

    /// <summary>The agent has finished (or been interrupted in) an utterance. Only the wav2lip face uses it.</summary>
    public void EndSpeech()
    {
        if (_wav2lip == null)
        {
            return;
        }

        lock (_speechLock)
        {
            if (_inSpeech)
            {
                _inSpeech = false;
                _wav2lip.EndSpeech();
            }
        }
    }

    /// <summary>Resamples to 16 kHz, keeping the resampler's filter state across calls. Assumes _speechLock is held.</summary>
    private short[] Resample(short[] pcm, int sampleRate)
    {
        if (_resampler == null || _resamplerInputRate != sampleRate)
        {
            _resampler = ResamplerFactory.CreateResampler(1, sampleRate, WAV2LIP_SAMPLE_RATE, 5, TextWriter.Null);
            _resamplerInputRate = sampleRate;
        }

        var output = new short[(int)((long)pcm.Length * WAV2LIP_SAMPLE_RATE / sampleRate) + 16];
        int inputLength = pcm.Length;
        int outputLength = output.Length;
        _resampler.Process(0, pcm, ref inputLength, output, ref outputLength);
        return outputLength == output.Length ? output : output[..outputLength];
    }

    public void Dispose()
    {
        (_video as IDisposable)?.Dispose();
    }
}
