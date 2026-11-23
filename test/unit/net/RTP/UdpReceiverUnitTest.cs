//-----------------------------------------------------------------------------
// Filename: UdpReceiverUnitTest.cs
//
// Description: Unit tests for the UdpReceiver class, specifically verifying
// that closing a receiver while it has a pending BeginReceiveFrom does not
// leave an unobserved IAsyncResult (issue #1494).
//
// Author(s):
// CraziestPower
//
// History:
// 16 Feb 2026	CraziestPower	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class UdpReceiverUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public UdpReceiverUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Verifies that closing a UdpReceiver while a BeginReceiveFrom is pending
        /// does not produce an UnobservedTaskException. This is the scenario described
        /// in issue #1494: the APM End* call must always be made for every Begin* call.
        /// </summary>
        [Fact]
        public async Task CloseWhileReceivingDoesNotThrowUnobservedException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            AggregateException capturedException = null;

            // Only capture (and observe) unobserved-task exceptions whose stack trace
            // mentions UdpReceiver. The TaskScheduler.UnobservedTaskException event is
            // process-global, so without a filter this handler also picks up Task leaks
            // from any other test running in parallel — which made this test fail
            // sporadically. Exceptions from other sources are intentionally left
            // unobserved so their owning tests still see them.
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (s, e) =>
            {
                bool fromUdpReceiver = e.Exception.InnerExceptions
                    .Any(ex => ex.StackTrace?.Contains(nameof(UdpReceiver)) == true);
                if (fromUdpReceiver)
                {
                    capturedException = e.Exception;
                    e.SetObserved();
                }
            };

            TaskScheduler.UnobservedTaskException += handler;
            try
            {
                // Create a UDP socket bound to a random port.
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

                var receiver = new UdpReceiver(socket);

                // Start the async receive loop.
                receiver.BeginReceiveFrom();
                Assert.True(receiver.IsRunningReceive);

                // Close while the receive is pending — this is the #1494 trigger.
                receiver.Close("test shutdown");
                Assert.True(receiver.IsClosed);

                // Give the async callback time to fire and the finalizer to run.
                await Task.Delay(250);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(250);

                Assert.Null(capturedException);
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= handler;
            }

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Verifies that the OnClosed event fires with the reason string when
        /// Close is called on a UdpReceiver.
        /// </summary>
        [Fact]
        public void CloseFiresOnClosedEvent()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

            var receiver = new UdpReceiver(socket);

            string closedReason = null;
            receiver.OnClosed += reason => closedReason = reason;

            receiver.Close("unit test close");

            Assert.True(receiver.IsClosed);
            Assert.Equal("unit test close", closedReason);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// RTPChannel.Send calls BeginReceiveFrom from the sending thread whenever the receive looks stopped. A receive
        /// that completed used to clear its running flag before re-arming, and a sender that called in between posted a
        /// second receive. The two then shared the receive buffer for the rest of the session, overwriting each other's
        /// datagrams: corrupted and duplicated packets, which on an SRTP session fail authentication or replay checks.
        /// Here a thread calls BeginReceiveFrom without pause while datagrams arrive; packets are handled one at a
        /// time and each one arrives once, intact.
        /// </summary>
        [Fact]
        public void BeginReceiveFromOtherThreadsKeepsOneReceive()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int count = 4000;
            const int size = 200;

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveBufferSize = 1024 * 1024;
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var receiver = new UdpReceiver(socket);

            int inCallback = 0, overlapped = 0, corrupt = 0, received = 0;
            var seen = new bool[count];
            int duplicates = 0;
            var done = new ManualResetEventSlim();
            receiver.OnPacketReceived += (_, _, _, packet) =>
            {
                if (Interlocked.Increment(ref inCallback) > 1) { Interlocked.Increment(ref overlapped); }
                try
                {
                    int n = BitConverter.ToInt32(packet, 0);
                    bool intact = packet.Length == size && n >= 0 && n < count;
                    for (int i = 4; intact && i < size; i++) { intact = packet[i] == (byte)(n + i); }
                    if (!intact) { Interlocked.Increment(ref corrupt); }
                    else
                    {
                        lock (seen)
                        {
                            if (seen[n]) { duplicates++; }
                            seen[n] = true;
                        }
                    }
                    if (Interlocked.Increment(ref received) >= count) { done.Set(); }
                }
                finally
                {
                    Interlocked.Decrement(ref inCallback);
                }
            };
            receiver.BeginReceiveFrom();

            using var stop = new CancellationTokenSource();
            var hammer = new Thread(() => { while (!stop.IsCancellationRequested) { receiver.BeginReceiveFrom(); } }) { IsBackground = true };
            hammer.Start();

            using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            var dst = (IPEndPoint)socket.LocalEndPoint;
            var buffer = new byte[size];
            for (int n = 0; n < count; n++)
            {
                BitConverter.GetBytes(n).CopyTo(buffer, 0);
                for (int i = 4; i < size; i++) { buffer[i] = (byte)(n + i); }
                sender.SendTo(buffer, dst);
                // Spaced out, so most arrive on a fresh asynchronous receive rather than the drain loop.
                if (n % 4 == 0) { Thread.Sleep(0); }
            }

            done.Wait(TimeSpan.FromSeconds(10));
            stop.Cancel();
            hammer.Join();
            receiver.Close(null);

            Assert.Equal(0, overlapped);
            Assert.Equal(0, corrupt);
            Assert.Equal(0, duplicates);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Verifies that calling Close multiple times only fires OnClosed once
        /// and does not throw.
        /// </summary>
        [Fact]
        public void DoubleCloseDoesNotThrow()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

            var receiver = new UdpReceiver(socket);

            int closedCount = 0;
            receiver.OnClosed += _ => Interlocked.Increment(ref closedCount);

            receiver.Close("first");
            receiver.Close("second");

            Assert.True(receiver.IsClosed);
            Assert.Equal(1, closedCount);

            logger.LogDebug("-----------------------------------------");
        }
    }
}
