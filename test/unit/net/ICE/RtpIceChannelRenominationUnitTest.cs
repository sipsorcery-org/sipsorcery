//-----------------------------------------------------------------------------
// Filename: RtpIceChannelRenominationUnitTest.cs
//
// Description: Tests for how a controlled RtpIceChannel handles a controlling
// peer that nominates further candidate pairs once connected. Chrome nominates
// every pair it rates at least as good as its selected one, so the channel sees
// a stream of USE-CANDIDATE requests across several pairs. Only pairs the channel
// has verified with its own check may become the destination.
//
// The remote peer is played by loopback UDP sockets: one that answers the
// channel's checks (a working path) and one that only sends (a path that works
// in one direction, as the Android emulator's NAT has for IPv6).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RtpIceChannelRenominationUnitTest
    {
        private const string PEER_UFRAG = "peer";
        private const string PEER_PASSWORD = "peerpasswordpeerpassword";

        private readonly Microsoft.Extensions.Logging.ILogger logger;

        public RtpIceChannelRenominationUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// A path the remote peer can send on but the channel cannot reach is nominated while the
        /// channel is connected over a working one. The destination stays on the working path.
        /// </summary>
        [Fact]
        public async Task UnverifiedNominationDoesNotMoveTheDestination()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channel = NewControlledChannel();
            using var working = new PeerSocket(channel, answersChecks: true);
            using var oneWay = new PeerSocket(channel, answersChecks: false);
            try
            {
                working.Nominate();
                await WaitFor(() => IsDestination(channel, working), "connected over the working path");

                for (int i = 0; i < 20; i++)
                {
                    working.Nominate();
                    oneWay.Nominate();
                    await Task.Delay(60);
                    Assert.True(IsDestination(channel, working), $"destination moved to {channel.NominatedEntry.RemoteCandidate.ToShortString()}");
                }
            }
            finally
            {
                channel.Close();
            }
        }

        /// <summary>
        /// The channel connects on a path it cannot reach, and the peer then nominates a working
        /// one as well. The destination moves to the working path once the channel's own check
        /// on it succeeds, and stays there while the peer goes on nominating both.
        /// </summary>
        [Fact]
        public async Task VerifiedNominationMovesTheDestinationOnce()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channel = NewControlledChannel();
            using var working = new PeerSocket(channel, answersChecks: true);
            using var oneWay = new PeerSocket(channel, answersChecks: false);
            try
            {
                int changes = 0;
                oneWay.Nominate();
                await WaitFor(() => IsDestination(channel, oneWay), "connected over the one-way path");
                channel.OnIceConnectionStateChange += _ => changes++;

                var nominated = DateTime.Now;
                await WaitFor(() =>
                {
                    working.Nominate();
                    oneWay.Nominate();
                    return IsDestination(channel, working);
                }, "moved to the working path");

                // Within DTLS's first retransmit (1 s): the one-way path never answered, so it
                // gets the short grace, not the full one.
                var took = DateTime.Now.Subtract(nominated);
                Assert.True(took.TotalMilliseconds < 1000, $"moved after {took.TotalMilliseconds:F0} ms");

                for (int i = 0; i < 20; i++)
                {
                    working.Nominate();
                    oneWay.Nominate();
                    await Task.Delay(60);
                    Assert.True(IsDestination(channel, working), $"destination moved to {channel.NominatedEntry.RemoteCandidate.ToShortString()}");
                }

                Assert.Equal(1, changes);
            }
            finally
            {
                channel.Close();
            }
        }

        /// <summary>
        /// The path in use dies (a network change) after the channel verified it, and the peer
        /// nominates another working one. The destination moves within seconds, not when the
        /// connection times out as disconnected (DISCONNECTED_TIMEOUT_PERIOD, 8 s).
        /// </summary>
        [Fact]
        public async Task LeavesAVerifiedPathThatStopsAnswering()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channel = NewControlledChannel();
            using var first = new PeerSocket(channel, answersChecks: true);
            using var second = new PeerSocket(channel, answersChecks: true);
            using var oneWay = new PeerSocket(channel, answersChecks: false);
            try
            {
                // Onto the first path by way of a verified renomination, so it counts as verified.
                oneWay.Nominate();
                await WaitFor(() => IsDestination(channel, oneWay), "connected over the one-way path");
                await WaitFor(() =>
                {
                    first.Nominate();
                    return IsDestination(channel, first);
                }, "moved to the first path");

                first.AnswersChecks = false;
                var died = DateTime.Now;

                // The peer renominates as Chrome does once its pings on the first path fail.
                while (!IsDestination(channel, second) && DateTime.Now.Subtract(died).TotalSeconds < 6)
                {
                    second.Nominate();
                    await Task.Delay(300);
                }

                var took = DateTime.Now.Subtract(died);
                Assert.True(IsDestination(channel, second), $"still on {channel.NominatedEntry.RemoteCandidate.ToShortString()} after {took.TotalMilliseconds:F0} ms");
                Assert.True(took.TotalSeconds < 3, $"moved after {took.TotalMilliseconds:F0} ms");
            }
            finally
            {
                channel.Close();
            }
        }

        /// <summary>
        /// The path in use dies with no renomination at first (Wi-Fi back, mobile data off,
        /// before the peer has revalidated the Wi-Fi path), so only the channel's own
        /// periodic checks on it go unanswered. When the peer then nominates a path it
        /// nominated before, the channel moves at once: the unanswered periodic checks
        /// have already cost the dead path its claim.
        /// </summary>
        [Fact]
        public async Task LeavesAPathWhosePeriodicChecksWentUnanswered()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channel = NewControlledChannel();
            using var inUse = new PeerSocket(channel, answersChecks: true);
            using var other = new PeerSocket(channel, answersChecks: true);
            try
            {
                inUse.Nominate();
                await WaitFor(() => IsDestination(channel, inUse), "connected");

                // The other path nominated and verified while the one in use still works.
                for (int i = 0; i < 5; i++)
                {
                    other.Nominate();
                    inUse.Nominate();
                    await Task.Delay(200);
                }
                Assert.True(IsDestination(channel, inUse), "moved while the path in use worked");

                // Dies quietly: a periodic check (every CONNECTED_CHECK_PERIOD, 3 s) and its
                // grace pass before the peer nominates anything.
                inUse.AnswersChecks = false;
                await Task.Delay(4_500);

                var nominated = DateTime.Now;
                while (!IsDestination(channel, other) && DateTime.Now.Subtract(nominated).TotalSeconds < 5)
                {
                    other.Nominate();
                    await Task.Delay(300);
                }

                var took = DateTime.Now.Subtract(nominated);
                Assert.True(IsDestination(channel, other), $"still on {channel.NominatedEntry.RemoteCandidate.ToShortString()} after {took.TotalMilliseconds:F0} ms");
                Assert.True(took.TotalMilliseconds < 900, $"moved {took.TotalMilliseconds:F0} ms after the first nomination");
            }
            finally
            {
                channel.Close();
            }
        }

        /// <summary>
        /// A path that answered once and has since died, unchecked meanwhile, must not win
        /// over the one the peer is nominating now: a phone's mobile-data path, verified
        /// before it went back to Wi-Fi and turned mobile data off.
        /// </summary>
        [Fact]
        public async Task MovesToThePathNominatedNowNotOneVerifiedLongAgo()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channel = NewControlledChannel();
            using var inUse = new PeerSocket(channel, answersChecks: true);
            using var stale = new PeerSocket(channel, answersChecks: true);
            using var current = new PeerSocket(channel, answersChecks: true);
            try
            {
                inUse.Nominate();
                await WaitFor(() => IsDestination(channel, inUse), "connected");

                // Verified while it worked, created before the path nominated later, so first on a tie.
                for (int i = 0; i < 3; i++)
                {
                    stale.Nominate();
                    await Task.Delay(200);
                }
                stale.AnswersChecks = false;

                // Both the stale path and the one in use have died by the time the peer
                // nominates the path that works now.
                inUse.AnswersChecks = false;
                await Task.Delay(4_500);

                var nominated = DateTime.Now;
                var visitedStale = false;
                while (!IsDestination(channel, current) && DateTime.Now.Subtract(nominated).TotalSeconds < 5)
                {
                    current.Nominate();
                    await Task.Delay(100);
                    visitedStale |= IsDestination(channel, stale);
                }

                var took = DateTime.Now.Subtract(nominated);
                Assert.False(visitedStale, "moved to the stale path");
                Assert.True(IsDestination(channel, current), $"still on {channel.NominatedEntry.RemoteCandidate.ToShortString()} after {took.TotalMilliseconds:F0} ms");
                Assert.True(took.TotalMilliseconds < 900, $"moved {took.TotalMilliseconds:F0} ms after the first nomination");
            }
            finally
            {
                channel.Close();
            }
        }

        /// <summary>
        /// Two working paths, both nominated over and over: the one in use keeps answering, so it
        /// is kept, rather than the destination following each nomination.
        /// </summary>
        [Fact]
        public async Task KeepsAWorkingPathWhenAnotherIsNominated()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channel = NewControlledChannel();
            using var first = new PeerSocket(channel, answersChecks: true);
            using var second = new PeerSocket(channel, answersChecks: true);
            try
            {
                first.Nominate();
                await WaitFor(() => IsDestination(channel, first), "connected over the first path");
                int changes = 0;
                channel.OnIceConnectionStateChange += _ => changes++;

                for (int i = 0; i < 20; i++)
                {
                    second.Nominate();
                    first.Nominate();
                    await Task.Delay(100);
                }

                Assert.True(IsDestination(channel, first), $"destination moved to {channel.NominatedEntry.RemoteCandidate.ToShortString()}");
                Assert.Equal(0, changes);
            }
            finally
            {
                channel.Close();
            }
        }

        private static RtpIceChannel NewControlledChannel()
        {
            var channel = new RtpIceChannel(IPAddress.Loopback, RTCIceComponent.rtp);
            channel.SetRemoteCredentials(PEER_UFRAG, PEER_PASSWORD);
            channel.StartGathering();
            return channel;
        }

        private static bool IsDestination(RtpIceChannel channel, PeerSocket peer) =>
            channel.IceConnectionState == RTCIceConnectionState.connected &&
            channel.NominatedEntry?.RemoteCandidate.DestinationEndPoint?.Port == peer.Port;

        private static async Task WaitFor(Func<bool> condition, string what)
        {
            var until = DateTime.Now.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.Now > until)
                {
                    throw new TimeoutException($"Not {what} within 5 s.");
                }
                await Task.Delay(50);
            }
        }

        /// <summary>
        /// One of the controlling peer's sockets. It sends USE-CANDIDATE binding requests to the
        /// channel and, if it answers checks, replies to the channel's binding requests.
        /// </summary>
        private sealed class PeerSocket : IDisposable
        {
            private readonly RtpIceChannel _channel;
            private readonly UdpClient _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public int Port => ((IPEndPoint)_udp.Client.LocalEndPoint).Port;

            /// <summary>Whether the channel's checks are answered: a path that works both ways.</summary>
            public bool AnswersChecks { get; set; }

            public PeerSocket(RtpIceChannel channel, bool answersChecks)
            {
                _channel = channel;
                AnswersChecks = answersChecks;
                _ = Task.Run(Receive);
            }

            public void Nominate()
            {
                var request = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
                request.Header.TransactionId = Encoding.ASCII.GetBytes(Crypto.GetRandomString(STUNHeader.TRANSACTION_ID_LENGTH));
                request.AddUsernameAttribute($"{_channel.LocalIceUser}:{PEER_UFRAG}");
                request.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Priority, BitConverter.GetBytes(1_000_000u)));
                request.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.IceControlling, NetConvert.GetBytes(1UL)));
                request.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.UseCandidate, null));
                var bytes = request.ToByteBufferStringKey(_channel.LocalIcePassword, true);
                _udp.Send(bytes, bytes.Length, _channel.RTPLocalEndPoint);
            }

            private async Task Receive()
            {
                while (!_cts.IsCancellationRequested)
                {
                    UdpReceiveResult received;
                    try
                    {
                        received = await _udp.ReceiveAsync();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        continue;
                    }

                    var message = STUNMessage.ParseSTUNMessage(received.Buffer, received.Buffer.Length);
                    if (!AnswersChecks || message?.Header.MessageType != STUNMessageTypesEnum.BindingRequest)
                    {
                        continue;
                    }

                    var response = new STUNMessage(STUNMessageTypesEnum.BindingSuccessResponse);
                    response.Header.TransactionId = message.Header.TransactionId;
                    response.AddXORMappedAddressAttribute(received.RemoteEndPoint.Address, received.RemoteEndPoint.Port);
                    var bytes = response.ToByteBufferStringKey(PEER_PASSWORD, true);
                    _udp.Send(bytes, bytes.Length, received.RemoteEndPoint);
                }
            }

            public void Dispose()
            {
                _cts.Cancel();
                _udp.Dispose();
            }
        }
    }
}
