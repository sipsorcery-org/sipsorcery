//-----------------------------------------------------------------------------
// Filename: TerminalEndpointTests.cs
//
// Description: Unit tests for the pieces behind the "term" bridge endpoint: the
// AudioPacer that turns bursty agent audio into a steady stream, the DirectShow
// microphone discovery and the Matroska sink's audio only header.
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

using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Cli.Commands.Bridge;
using SIPSorcery.Cli.Commands.Route;
using SIPSorcery.Cli.Common;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class TerminalEndpointTests
{
    private static readonly byte[] Silence = { 0xF8, 0xFF, 0xFE };

    private static (AudioPacer Pacer, List<(byte[] Payload, uint Timestamp, uint Duration)> Out) CreatePacer()
    {
        var output = new List<(byte[], uint, uint)>();
        var pacer = new AudioPacer(Silence, 960, 48000, (p, ts, d) => output.Add((p, ts, d)));
        return (pacer, output);
    }

    [Fact]
    public void Pacer_fills_with_silence_when_nothing_is_queued()
    {
        var (pacer, output) = CreatePacer();

        pacer.EmitDue(100);   // frames due at 0, 20, 40, 60, 80, 100ms

        Assert.Equal(6, output.Count);
        Assert.All(output, f => Assert.Same(Silence, f.Payload));
    }

    [Fact]
    public void Pacer_timestamps_advance_continuously()
    {
        var (pacer, output) = CreatePacer();

        pacer.EmitDue(60);

        Assert.Equal(new uint[] { 0, 960, 1920, 2880 }, output.Select(f => f.Timestamp).ToArray());
    }

    [Fact]
    public void Pacer_spreads_a_burst_over_real_time()
    {
        var (pacer, output) = CreatePacer();
        var speech = Enumerable.Range(1, 5).Select(i => new byte[] { (byte)i }).ToArray();
        foreach (var frame in speech)
        {
            pacer.Enqueue(frame, 960);
        }

        long wait = pacer.EmitDue(0);   // only the first frame is due yet

        Assert.Single(output);
        Assert.Same(speech[0], output[0].Payload);
        Assert.Equal(20, wait);
        Assert.Equal(4, pacer.Queued);

        pacer.EmitDue(80);
        Assert.Equal(speech, output.Select(f => f.Payload).ToArray());
    }

    [Fact]
    public void Pacer_uses_a_frame_duration_for_the_next_due_time()
    {
        var (pacer, output) = CreatePacer();
        pacer.Enqueue(new byte[] { 1 }, 1920);   // a 40ms frame

        long wait = pacer.EmitDue(0);

        Assert.Equal(40, wait);
        pacer.EmitDue(40);
        Assert.Equal(1920u, output[1].Timestamp);
    }

    [Fact]
    public void Pacer_bounds_the_queue()
    {
        var output = new List<byte[]>();
        var pacer = new AudioPacer(Silence, 960, 48000, (p, _, _) => output.Add(p), maxQueued: 3);

        for (int i = 0; i < 10; i++)
        {
            pacer.Enqueue(new byte[] { (byte)i }, 960);
        }

        Assert.Equal(3, pacer.Queued);
        pacer.EmitDue(0);
        Assert.Equal(7, output[0][0]);   // the oldest were dropped
    }

    [Fact]
    public void Finds_the_first_directshow_audio_device()
    {
        string listing =
            "[in#0 @ 0001] \"MX Brio\" (video)\n" +
            "[in#0 @ 0001]   Alternative name \"@device_pnp_...\"\n" +
            "[in#0 @ 0001] \"Speakerphone (MX Brio)\" (audio)\n" +
            "[in#0 @ 0001] \"Microphone (USB)\" (audio)\n";

        Assert.Equal("Speakerphone (MX Brio)", MicrophoneCapture.ParseFirstDirectShowAudioDevice(listing));
    }

    [Fact]
    public void No_directshow_audio_device_gives_null()
    {
        Assert.Null(MicrophoneCapture.ParseFirstDirectShowAudioDevice("[in#0 @ 0001] \"MX Brio\" (video)\n"));
    }

    [Fact]
    public async Task Audio_only_matroska_header_does_not_wait_for_video()
    {
        string path = Path.Combine(Path.GetTempPath(), $"term-test-{Guid.NewGuid():N}.mkv");
        try
        {
            var sink = new MatroskaSinkNode(path, path, NullLogger.Instance, expectVideo: false);
            sink.Write(MediaFrame.ForAudio(Silence, 960, 960, AudioCommonlyUsedFormats.OpusWebRTC));

            // The header is written by the worker as soon as the audio track is known; poll briefly for it.
            for (int i = 0; i < 50 && sink.GetStats().Frames == 0; i++)
            {
                await Task.Delay(20);
            }
            Assert.Equal(1, sink.GetStats().Frames);

            await sink.DisposeAsync();

            string text = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path));
            Assert.Contains("A_OPUS", text);
            Assert.DoesNotContain("V_", text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
