//-----------------------------------------------------------------------------
// Filename: DtlsSrtpTransportReceiveUnitTest.cs
//
// Description: Tests for the receive side of the DtlsSrtpTransport, the
// DatagramTransport BouncyCastle reads handshake flights and, once the
// handshake is complete, SCTP packets from. Receive used to poll its queue
// with Thread.Sleep(25), delaying every packet by up to a poll interval and
// keeping its thread busy waking for the whole of each wait. It now blocks on
// the queue and returns as soon as a packet is written to it.
//
// History:
// 14 Oct 2026  Andy Ventura    Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using SIPSorcery.Net.SharpSRTP.DTLSSRTP;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class DtlsSrtpTransportReceiveUnitTest
    {
        private const int LONG_WAIT_MILLISECONDS = 10000;

        private readonly ILogger logger;

        public DtlsSrtpTransportReceiveUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// A packet already queued is returned straight away, copied to the requested offset.
        /// </summary>
        [Fact]
        public void Receive_QueuedPacket_ReturnedAtOffset()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var transport = new DtlsSrtpTransport(new DtlsSrtpClient(new BcTlsCrypto()));
            transport.WriteToRecvStream(new byte[] { 1, 2, 3 });

            var buffer = new byte[8];
            int received = transport.Receive(buffer, 2, buffer.Length - 2, LONG_WAIT_MILLISECONDS);

            Assert.Equal(3, received);
            Assert.Equal(new byte[] { 0, 0, 1, 2, 3, 0, 0, 0 }, buffer);
        }

        /// <summary>
        /// With nothing written the receive waits for its timeout and then reports it with
        /// DTLS_RETRANSMISSION_CODE, which is what BouncyCastle and the SCTP transport expect.
        /// </summary>
        [Fact]
        public void Receive_NoPacket_TimesOut()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var transport = new DtlsSrtpTransport(new DtlsSrtpClient(new BcTlsCrypto()));

            var sw = Stopwatch.StartNew();
            int received = transport.Receive(new byte[DtlsSrtpTransport.MAXIMUM_MTU], 0, DtlsSrtpTransport.MAXIMUM_MTU, 200);
            sw.Stop();

            Assert.Equal(DtlsSrtpTransport.DTLS_RETRANSMISSION_CODE, received);
            Assert.InRange(sw.ElapsedMilliseconds, 150, LONG_WAIT_MILLISECONDS);
        }

        /// <summary>
        /// A packet written while a receive is waiting wakes it. The old poll returned up to 25 ms
        /// (one sleep) late, a median of about 12 ms; waiting on the queue returns well inside
        /// a millisecond. The median keeps a scheduling hiccup in a single round from failing it.
        /// </summary>
        [Fact]
        public async Task Receive_PacketWrittenDuringWait_ReturnsPromptly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            const int ROUNDS = 20;

            var transport = new DtlsSrtpTransport(new DtlsSrtpClient(new BcTlsCrypto()));
            var latencies = new double[ROUNDS];

            for (int i = 0; i < ROUNDS; i++)
            {
                var receiveTask = Task.Factory.StartNew(() =>
                {
                    int received = transport.Receive(new byte[DtlsSrtpTransport.MAXIMUM_MTU], 0, DtlsSrtpTransport.MAXIMUM_MTU, LONG_WAIT_MILLISECONDS);
                    long returnedAt = Stopwatch.GetTimestamp();
                    Assert.Equal(1, received);
                    return returnedAt;
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                // Give the receive time to start waiting before the packet arrives.
                await Task.Delay(30);

                long writtenAt = Stopwatch.GetTimestamp();
                transport.WriteToRecvStream(new byte[] { (byte)i });

                var winner = await Task.WhenAny(receiveTask, Task.Delay(LONG_WAIT_MILLISECONDS));
                Assert.True(winner == receiveTask, "Receive did not return after a packet was written.");

                latencies[i] = (await receiveTask - writtenAt) * 1000.0 / Stopwatch.Frequency;
            }

            double median = latencies.OrderBy(x => x).ElementAt(ROUNDS / 2);

            logger.LogDebug("Receive wake latency median {Median:0.000} ms, max {Max:0.000} ms.", median, latencies.Max());

            Assert.True(median < 5, $"Median receive wake latency was {median:0.000} ms.");
        }

        /// <summary>
        /// Closing the transport before the handshake completes wakes a waiting receive, which
        /// fails with a user_canceled alert so BouncyCastle abandons the handshake.
        /// </summary>
        [Fact]
        public async Task Close_BeforeHandshakeComplete_WakesWaitingReceive()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var transport = new DtlsSrtpTransport(new DtlsSrtpClient(new BcTlsCrypto()));

            var receiveTask = Task.Factory.StartNew(
                () => transport.Receive(new byte[DtlsSrtpTransport.MAXIMUM_MTU], 0, DtlsSrtpTransport.MAXIMUM_MTU, LONG_WAIT_MILLISECONDS),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            await Task.Delay(50);

            var sw = Stopwatch.StartNew();
            transport.Close();

            var winner = await Task.WhenAny(receiveTask, Task.Delay(LONG_WAIT_MILLISECONDS));
            sw.Stop();

            Assert.True(winner == receiveTask, "Receive did not return after the transport was closed.");
            var alert = await Assert.ThrowsAsync<TlsFatalAlert>(() => receiveTask);
            Assert.Equal(AlertDescription.user_canceled, alert.AlertDescription);
            Assert.True(sw.ElapsedMilliseconds < LONG_WAIT_MILLISECONDS / 2, $"Receive took {sw.ElapsedMilliseconds} ms to return after close.");
        }

        /// <summary>
        /// A handshake with no remote peer, closed part way through, ends at the close rather
        /// than running on to the handshake timeout.
        /// </summary>
        [Fact]
        public async Task Close_DuringHandshake_HandshakeEndsPromptly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var transport = new DtlsSrtpTransport(new DtlsSrtpClient(new BcTlsCrypto()));
            transport.TimeoutMilliseconds = LONG_WAIT_MILLISECONDS;

            var handshakeTask = Task.Factory.StartNew(() => transport.DoHandshake(out _),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            await Task.Delay(500);

            var sw = Stopwatch.StartNew();
            transport.Close();

            var winner = await Task.WhenAny(handshakeTask, Task.Delay(LONG_WAIT_MILLISECONDS / 2));
            sw.Stop();

            Assert.True(winner == handshakeTask, $"Handshake still running {sw.ElapsedMilliseconds} ms after close.");
            Assert.False(await handshakeTask);
            Assert.False(transport.IsHandshakeComplete());
        }

        /// <summary>
        /// Once the handshake has completed a close leaves receive semantics as they were: the
        /// SCTP receive thread keeps getting timeouts, which it ends on, rather than errors.
        /// </summary>
        [Fact]
        public async Task Close_AfterHandshakeComplete_ReceiveStillTimesOut()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var client = new DtlsSrtpTransport(new DtlsSrtpClient(new BcTlsCrypto()));
            var server = new DtlsSrtpTransport(new DtlsSrtpServer(new BcTlsCrypto()));
            client.TimeoutMilliseconds = LONG_WAIT_MILLISECONDS;
            server.TimeoutMilliseconds = LONG_WAIT_MILLISECONDS;

            client.OnDataReady += server.WriteToRecvStream;
            server.OnDataReady += client.WriteToRecvStream;

            var serverTask = Task.Factory.StartNew(() => server.DoHandshake(out _),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var clientTask = Task.Factory.StartNew(() => client.DoHandshake(out _),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            var bothTask = Task.WhenAll(serverTask, clientTask);
            Assert.True(await Task.WhenAny(bothTask, Task.Delay(LONG_WAIT_MILLISECONDS)) == bothTask, "Handshake timed out.");
            Assert.True(await serverTask);
            Assert.True(await clientTask);

            client.Close();

            // Anything the server sent after the handshake is still delivered, then the timeout.
            int received = 0;
            for (int i = 0; i < 10 && received != DtlsSrtpTransport.DTLS_RETRANSMISSION_CODE; i++)
            {
                received = client.Receive(new byte[DtlsSrtpTransport.MAXIMUM_MTU], 0, DtlsSrtpTransport.MAXIMUM_MTU, 100);
            }

            Assert.Equal(DtlsSrtpTransport.DTLS_RETRANSMISSION_CODE, received);
        }
    }
}
