//-----------------------------------------------------------------------------
// Filename: StreamGraphTests.cs
//
// Description: Unit tests for when a route's StreamGraph stops: the source ending,
// every sink ending on its own (a sip: call hung up), or the duration running out
// while a sink that can't end (a file, a player) is still in the tee.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 16 Nov 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Cli.Commands.Route;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class StreamGraphTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Run_stops_when_the_source_ends()
    {
        var source = new FakeSource();
        var graph = new StreamGraph(source, new ISinkNode[] { new FakeSink() });

        var run = graph.RunAsync(0, CancellationToken.None);
        source.End();

        Assert.Equal("source ended", await run.WaitAsync(Limit));
    }

    [Fact]
    public async Task Run_stops_once_every_endable_sink_has_ended()
    {
        var first = new FakeEndableSink();
        var second = new FakeEndableSink();
        var graph = new StreamGraph(new FakeSource(), new ISinkNode[] { first, second });

        var run = graph.RunAsync(0, CancellationToken.None);
        first.End();
        await Task.Delay(100);
        Assert.False(run.IsCompleted, "one call hanging up shouldn't stop the other");

        second.End();
        Assert.Equal("sink ended", await run.WaitAsync(Limit));
    }

    [Fact]
    public async Task Run_goes_on_while_a_sink_that_cannot_end_is_in_the_tee()
    {
        // A recording shouldn't be cut short because the phone watching alongside it hung up.
        var call = new FakeEndableSink();
        var graph = new StreamGraph(new FakeSource(), new ISinkNode[] { call, new FakeSink() });

        var run = graph.RunAsync(1, CancellationToken.None);
        call.End();

        Assert.Equal("duration elapsed", await run.WaitAsync(Limit));
    }

    private sealed class FakeSource : ISourceNode
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action<MediaFrame>? OnFrame { add { } remove { } }
        public long? ConnectTimeMs => null;
        public Task Completion => _completion.Task;
        public void End() => _completion.TrySetResult();
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public string Describe() => "fake source";
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private class FakeSink : ISinkNode
    {
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public void Write(MediaFrame frame) { }
        public SinkStats GetStats() => default;
        public string Describe() => "fake sink";
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeEndableSink : FakeSink, IEndableSink
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;
        public void End() => _completion.TrySetResult();
    }
}
