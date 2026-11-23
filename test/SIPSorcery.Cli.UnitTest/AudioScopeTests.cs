//-----------------------------------------------------------------------------
// Filename: AudioScopeTests.cs
//
// Description: Unit tests for the route --scope-mode hilbert pieces: the
// --scope-size parser and the AudioScopeRenderer that draws the Hilbert audio
// scope into a bgr24 frame.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 24 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Numerics;
using SIPSorcery.Cli.Commands.Route;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class AudioScopeTests
{
    [Theory]
    [InlineData("640x360", 640, 360)]
    [InlineData("1280X720", 1280, 720)]
    public void Parses_valid_scope_sizes(string input, int expectedWidth, int expectedHeight)
    {
        Assert.True(AudioScopeTransform.TryParseSize(input, out int width, out int height, out string? error));
        Assert.Null(error);
        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Theory]
    [InlineData("")]
    [InlineData("640")]
    [InlineData("640x")]
    [InlineData("0x360")]
    [InlineData("-640x360")]
    [InlineData("641x360")]
    [InlineData("640x361")]
    public void Rejects_invalid_scope_sizes(string input)
    {
        Assert.False(AudioScopeTransform.TryParseSize(input, out _, out _, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Renderer_frame_matches_requested_size()
    {
        var renderer = new AudioScopeRenderer(320, 180);

        byte[] frame = renderer.ProcessAudioSample(Tone(440, 8000, 160));

        Assert.Equal(320 * 180 * AudioScopeRenderer.PIXEL_STRIDE, frame.Length);
    }

    [Fact]
    public void Renderer_draws_a_trace_for_a_tone()
    {
        var renderer = new AudioScopeRenderer(320, 180);

        // The analysis is a sliding FFT window, so feed a few blocks to fill it.
        byte[] frame = Array.Empty<byte>();
        for (int i = 0; i < 10; i++)
        {
            frame = renderer.ProcessAudioSample(Tone(440, 8000, 160));
        }

        Assert.Contains(frame, b => b > 0);
    }

    [Fact]
    public void Renderer_stays_black_for_silence()
    {
        var renderer = new AudioScopeRenderer(320, 180);

        byte[] frame = Array.Empty<byte>();
        for (int i = 0; i < 10; i++)
        {
            frame = renderer.ProcessAudioSample(new Complex[160]);
        }

        // Silence is a point at the centre of the scope, so almost the whole frame stays black.
        int lit = frame.Count(b => b > 0);
        Assert.True(lit < frame.Length / 100, $"{lit} bytes lit for silence.");
    }

    private static Complex[] Tone(double frequency, int sampleRate, int count)
    {
        var samples = new Complex[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = new Complex(0.5 * Math.Sin(2 * Math.PI * frequency * i / sampleRate), 0);
        }

        return samples;
    }
}
