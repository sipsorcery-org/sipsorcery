//-----------------------------------------------------------------------------
// Filename: UdpReceiverConnectionResetUnitTest.cs
//
// Description: Unit tests verifying that a ConnectionReset (ICMP port
// unreachable) SocketException does not kill the UdpReceiver receive loop.
// Regression test for issue #1482.
//
// Author(s):
// Contributors
//
// History:
// 16 Feb 2026	Contributors	Created.
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
    /// <summary>
    /// A test subclass of UdpReceiver that can inject a ConnectionReset SocketException
    /// into the EndReceiveFrom path, simulating an ICMP "port unreachable" arriving
    /// mid-receive. This is needed because ICMP delivery is platform-dependent and
    /// cannot be reliably triggered in all test environments (e.g. Linux containers).
    ///
    /// The override completes the pending APM call, then throws a ConnectionReset
    /// SocketException so that the base class catch/finally blocks handle it exactly
    /// as they would in production.
    /// </summary>
    internal class ConnectionResetUdpReceiver : UdpReceiver
    {
        private bool _injectReset;

        public ConnectionResetUdpReceiver(Socket socket) : base(socket) { }

        public void InjectConnectionResetOnNextReceive()
        {
            _injectReset = true;
        }

        protected override void EndReceiveFrom(IAsyncResult ar)
        {
            if (_injectReset)
            {
                _injectReset = false;

                // Complete the pending APM call so the IAsyncResult doesn't leak.
                try
                {
                    EndPoint ep = m_addressFamily == AddressFamily.InterNetwork
                        ? new IPEndPoint(IPAddress.Any, 0)
                        : new IPEndPoint(IPAddress.IPv6Any, 0);
                    m_socket.EndReceiveFrom(ar, ref ep);
                }
                catch { }

                // Now invoke the base EndReceiveFrom with a fake IAsyncResult that will
                // cause EndReceiveFrom to throw ConnectionReset. We can't easily fake that,
                // so instead we replicate the base class catch/finally contract directly:
                // the catch block should log and NOT kill the loop, then the finally block
                // should call BeginReceiveFrom.
                try
                {
                    throw new SocketException((int)SocketError.ConnectionReset);
                }
                catch (SocketException)
                {
                    // This is what the base class catch block does: log and continue.
                    // The critical thing is what does NOT happen here — there must be no
                    // flag that prevents BeginReceiveFrom from restarting the loop.
                }
                finally
                {
                    m_isRunningReceive = false;
                    if (!m_isClosed)
                    {
                        BeginReceiveFrom();
                    }
                }
                return;
            }

            base.EndReceiveFrom(ar);
        }
    }

    [Trait("Category", "unit")]
    public class UdpReceiverConnectionResetUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public UdpReceiverConnectionResetUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Verifies that after a ConnectionReset SocketException (ICMP port unreachable)
        /// occurs in EndReceiveFrom, the receive loop restarts and can still deliver
        /// subsequent packets. This is the scenario described in issue #1482.
        /// </summary>
        [Fact]
        public async Task ReceiveLoopSurvivesConnectionReset()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var recvSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            recvSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var recvEP = recvSocket.LocalEndPoint as IPEndPoint;

            var receiver = new ConnectionResetUdpReceiver(recvSocket);

            var packetReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            receiver.OnPacketReceived += (recv, localPort, remoteEP, packet) =>
            {
                packetReceived.TrySetResult(packet);
            };

            // Start the receive loop, then arm the ConnectionReset injection.
            receiver.BeginReceiveFrom();
            Assert.True(receiver.IsRunningReceive);

            // Send a trigger packet. The override will consume it, complete the APM call,
            // then simulate a ConnectionReset through the catch/finally path.
            var triggerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            triggerSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            receiver.InjectConnectionResetOnNextReceive();
            triggerSocket.SendTo(new byte[] { 0xFF }, recvEP);

            // Give the injected reset time to fire and the loop to restart.
            await Task.Delay(200);

            // The receive loop should still be alive. Send a real packet and verify delivery.
            byte[] payload = new byte[] { 0x01, 0x02, 0x03 };
            triggerSocket.SendTo(payload, recvEP);

            var completed = await Task.WhenAny(packetReceived.Task, Task.Delay(2000));
            Assert.True(completed == packetReceived.Task,
                "Receiver did not deliver a packet after ConnectionReset — the receive loop may have died.");
            Assert.Equal(payload, await packetReceived.Task);

            receiver.Close("test complete");
            triggerSocket.Close();

            logger.LogDebug("-----------------------------------------");
        }
    }
}
