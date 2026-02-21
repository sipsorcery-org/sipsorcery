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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            AggregateException capturedException = null;

            EventHandler<UnobservedTaskExceptionEventArgs> handler = (s, e) =>
            {
                // Only capture exceptions that originate from UdpReceiver code.
                // Other tests running in parallel may leave unobserved task exceptions
                // (e.g. from SIPUserAgent) that the GC collects during our test.
                if (!e.Observed && e.Exception.ToString().Contains("UdpReceiver"))
                {
                    capturedException = e.Exception;
                }
                e.SetObserved();
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
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
        /// Verifies that calling Close multiple times only fires OnClosed once
        /// and does not throw.
        /// </summary>
        [Fact]
        public void DoubleCloseDoesNotThrow()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
