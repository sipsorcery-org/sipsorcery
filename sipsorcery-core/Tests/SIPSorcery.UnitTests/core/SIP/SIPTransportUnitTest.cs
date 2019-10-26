//-----------------------------------------------------------------------------
// Filename: SIPTransportUnitTest.cs
//
// Description: Unit tests for the SIPTransport class.
// 
// History:
// 15 Oct 2019	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2019 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Dublin, Ireland (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.UnitTests
{
    [TestClass]
    public class SIPTransportUnitTest
    {
        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate IPv6 sockets using the loopback address.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void IPv6LoopbackSendReceiveTest()
        {
            CancellationTokenSource cancelServer = new CancellationTokenSource();
            TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>();

            var serverChannel = new SIPUDPChannel(IPAddress.IPv6Loopback, 6060);
            var clientChannel = new SIPUDPChannel(IPAddress.IPv6Loopback, 6061);

            var serverTask = Task.Run(() => { RunServer(serverChannel, cancelServer); });
            var clientTask = Task.Run(async () => { await RunClient(clientChannel, new SIPURI(SIPSchemesEnum.sip, serverChannel.SIPChannelEndPoint), testComplete); });

            Task.WhenAny(new Task[] { serverTask, clientTask, Task.Delay(2000) }).Wait();

            if (testComplete.Task.IsCompleted == false)
            {
                // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                testComplete.SetResult(false);
            }

            Assert.IsTrue(testComplete.Task.Result);

            cancelServer.Cancel();
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate IPv4 sockets using the loopback address.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void IPv4LoopbackSendReceiveTest()
        {
            CancellationTokenSource cancelServer = new CancellationTokenSource();
            TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>();

            var serverChannel = new SIPUDPChannel(IPAddress.Loopback, 6060);
            var clientChannel = new SIPUDPChannel(IPAddress.Loopback, 6061);

            var serverTask = Task.Run(() => { RunServer(serverChannel, cancelServer); });
            var clientTask = Task.Run(async () => { await RunClient(clientChannel, new SIPURI(SIPSchemesEnum.sip, serverChannel.SIPChannelEndPoint), testComplete); });

            Task.WhenAny(new Task[] { serverTask, clientTask, Task.Delay(2000) }).Wait();

            if (testComplete.Task.IsCompleted == false)
            {
                // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                testComplete.SetResult(false);
            }

            Assert.IsTrue(testComplete.Task.Result);
            cancelServer.Cancel();
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate TCP IPv6 sockets using the loopback address.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void IPv6TcpLoopbackSendReceiveTest()
        {
            CancellationTokenSource cancelServer = new CancellationTokenSource();
            TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>();

            var serverChannel = new SIPTCPChannel(IPAddress.IPv6Loopback, 7060);
            serverChannel.DisableLocalTCPSocketsCheck = true;
            var clientChannel = new SIPTCPChannel(IPAddress.IPv6Loopback, 7061);
            clientChannel.DisableLocalTCPSocketsCheck = true;

            var serverTask = Task.Run(() => { RunServer(serverChannel, cancelServer); });
            var clientTask = Task.Run(async () => { await RunClient(clientChannel, new SIPURI(SIPSchemesEnum.sip, serverChannel.SIPChannelEndPoint), testComplete); });

            Task.WhenAny(new Task[] { serverTask, clientTask, Task.Delay(2000) }).Wait();

            if (testComplete.Task.IsCompleted == false)
            {
                // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                testComplete.SetResult(false);
            }

            Assert.IsTrue(testComplete.Task.Result);

            cancelServer.Cancel();
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate IPv4 TCP sockets using the loopback address.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void IPv4TcpLoopbackSendReceiveTest()
        {
            CancellationTokenSource cancelServer = new CancellationTokenSource();
            TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>();

            var serverChannel = new SIPTCPChannel(IPAddress.Loopback, 7060);
            serverChannel.DisableLocalTCPSocketsCheck = true;
            var clientChannel = new SIPTCPChannel(IPAddress.Loopback, 7061);
            clientChannel.DisableLocalTCPSocketsCheck = true;

            Task.Run(() => { RunServer(serverChannel, cancelServer); });
            var clientTask = Task.Run(async () => { await RunClient(clientChannel, new SIPURI(SIPSchemesEnum.sip, serverChannel.SIPChannelEndPoint), testComplete); });

            Task.WhenAny(new Task[] { clientTask, Task.Delay(2000) }).Wait();

            if (testComplete.Task.IsCompleted == false)
            {
                // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                testComplete.SetResult(false);
            }

            Assert.IsTrue(testComplete.Task.Result);

            cancelServer.Cancel();
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate IPv4 TCP sockets using the loopback address AND then 
        /// can be repeated. This tests that the no linger option on the TCP sockets is working correctly. If it's not the OS will keep 
        /// one or both end of the closed socket in a TIME_WAIT state for typically 30s which prevents the TCP socket from being able to
        /// reconnect with the same IP address and port number combination.
        /// This is not a real test because the OS will allow the connection to be re-established if the process ID is the same as the one
        /// that put the socket into the TIME_WAIT state.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void IPv4TcpLoopbackConsecutiveSendReceiveTest()
        {
            CancellationTokenSource cancelServer = new CancellationTokenSource();
            var serverChannel = new SIPTCPChannel(IPAddress.Loopback, 7064);
            serverChannel.DisableLocalTCPSocketsCheck = true;

            var serverTask = Task.Run(() => { RunServer(serverChannel, cancelServer); });

            for (int i = 1; i < 3; i++)
            {
                TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>();

                var clientChannel = new SIPTCPChannel(IPAddress.Any, 7065);
                clientChannel.DisableLocalTCPSocketsCheck = true;
                SIPURI serverUri = new SIPURI(SIPSchemesEnum.sip, serverChannel.SIPChannelEndPoint);

                logger.LogDebug($"Server URI {serverUri.ToString()}.");

                var clientTask = Task.Run(async () => { await RunClient(clientChannel, serverUri, testComplete); });

                //Task.WhenAny(new Task[] { clientTask, serverTask, Task.Delay(10000) }).Wait();
                Task.WhenAny(new Task[] { clientTask, Task.Delay(2000) }).Wait();

                if (testComplete.Task.IsCompleted == false)
                {
                    // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                    testComplete.SetResult(false);
                }

                Assert.IsTrue(testComplete.Task.Result);

                logger.LogDebug($"Completed for test run {i}.");

                //Task.Delay(3000).Wait();
            }

            cancelServer.Cancel();
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate TLS IPv6 sockets using the loopback address.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void IPv6TlsLoopbackSendReceiveTest()
        {
            CancellationTokenSource cancelServer = new CancellationTokenSource();
            TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>();

            Assert.IsTrue(File.Exists(@"certs\localhost.pfx"), "The TLS transport channel test was missing the localhost.pfx certificate file.");

            var serverCertificate = new X509Certificate2(@"certs\localhost.pfx", "");
            var verifyCert = serverCertificate.Verify();
            logger.LogDebug("Server Certificate loaded from file, Subject=" + serverCertificate.Subject + ", valid=" + verifyCert + ".");

            var serverChannel = new SIPTLSChannel(serverCertificate, IPAddress.IPv6Loopback, 8062);
            serverChannel.DisableLocalTCPSocketsCheck = true;
            var clientChannel = new SIPTLSChannel(serverCertificate, IPAddress.IPv6Loopback, 8063);
            clientChannel.DisableLocalTCPSocketsCheck = true;

            var serverTask = Task.Run(() => { RunServer(serverChannel, cancelServer); });
            var clientTask = Task.Run(async () => { await RunClient(clientChannel, new SIPURI(SIPSchemesEnum.sips, serverChannel.SIPChannelEndPoint), testComplete); });

            Task.WhenAny(new Task[] { serverTask, clientTask, Task.Delay(3000) }).Wait();

            if (testComplete.Task.IsCompleted == false)
            {
                // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                testComplete.SetResult(false);
            }

            Assert.IsTrue(testComplete.Task.Result);

            cancelServer.Cancel();
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate IPv4 TLS sockets using the loopback address.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void IPv4TlsLoopbackSendReceiveTest()
        {
            CancellationTokenSource cancelServer = new CancellationTokenSource();
            TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>();

            Assert.IsTrue(File.Exists(@"certs\localhost.pfx"), "The TLS transport channel test was missing the localhost.pfx certificate file.");

            var serverCertificate = new X509Certificate2(@"certs\localhost.pfx", "");
            var verifyCert = serverCertificate.Verify();
            logger.LogDebug("Server Certificate loaded from file, Subject=" + serverCertificate.Subject + ", valid=" + verifyCert + ".");

            var serverChannel = new SIPTLSChannel(serverCertificate, IPAddress.Loopback, 8060);
            serverChannel.DisableLocalTCPSocketsCheck = true;
            var clientChannel = new SIPTLSChannel(serverCertificate, IPAddress.Loopback, 8061);
            clientChannel.DisableLocalTCPSocketsCheck = true;

            var serverTask = Task.Run(() => { RunServer(serverChannel, cancelServer); });
            var clientTask = Task.Run(async () => { await RunClient(clientChannel, new SIPURI(SIPSchemesEnum.sips, serverChannel.SIPChannelEndPoint), testComplete); });

            Task.WhenAny(new Task[] { serverTask, clientTask, Task.Delay(3000) }).Wait();

            if (testComplete.Task.IsCompleted == false)
            {
                // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                testComplete.SetResult(false);
            }

            Assert.IsTrue(testComplete.Task.Result);

            cancelServer.Cancel();
        }

        /// <summary>
        /// Tests that SIP messages can be correctly extracted from a TCP stream when arbitrarily fragmented.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void TcpTrickleReceiveTest()
        {
            TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>();

            IPEndPoint listenEP = new IPEndPoint(IPAddress.Loopback, 9066);
            var transport = new SIPTransport();
            var tcpChannel = new SIPTCPChannel(new IPEndPoint(IPAddress.Loopback, 9067));
            tcpChannel.DisableLocalTCPSocketsCheck = true;
            transport.AddSIPChannel(tcpChannel);

            int requestCount = 10;
            int recvdReqCount = 0;

            Task.Run(() =>
            {
                TcpListener listener = new TcpListener(listenEP);
                listener.Start();
                var tcpClient = listener.AcceptTcpClient();
                logger.LogDebug($"TCP listener accepted client with remote end point {tcpClient.Client.RemoteEndPoint}.");
                for (int i = 0; i < requestCount; i++)
                {
                    logger.LogDebug($"Sending request {i}.");

                    var req = transport.GetRequest(SIPMethodsEnum.OPTIONS, SIPURI.ParseSIPURIRelaxed($"{i}@sipsorcery.com"));
                    byte[] reqBytes = Encoding.UTF8.GetBytes(req.ToString());

                    tcpClient.GetStream().Write(reqBytes, 0, reqBytes.Length);
                    tcpClient.GetStream().Flush();

                    Task.Delay(30).Wait();
                }
                tcpClient.GetStream().Close();
            });

            transport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                logger.LogDebug($"Request received {localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipRequest.StatusLine}");
                logger.LogDebug(sipRequest.ToString());
                Interlocked.Increment(ref recvdReqCount);

                if(recvdReqCount == requestCount)
                {
                    testComplete.SetResult(true);
                }
            };

            transport.SIPTransportResponseReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse) =>
            {
                logger.LogDebug($"Response received {localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipResponse.ShortDescription}");
                logger.LogDebug(sipResponse.ToString());
            };

            tcpChannel.ConnectClientAsync(listenEP, null, null).Wait();

            Task.WhenAny(new Task[] { testComplete.Task, Task.Delay(5000) }).Wait();

            transport.Shutdown();

            Assert.IsTrue(testComplete.Task.IsCompleted);
            Assert.IsTrue(testComplete.Task.Result);
            Assert.AreEqual(requestCount, recvdReqCount, $"The count of {recvdReqCount} for the requests received did not match what was expected.");
        }

        /// <summary>
        /// Initialises a SIP transport to act as a server in single request/response exchange.
        /// </summary>
        /// <param name="testServerChannel">The server SIP channel to test.</param>
        /// <param name="cts">Cancellation token to tell the server when to shutdown.</param>
        private void RunServer(SIPChannel testServerChannel, CancellationTokenSource cts)
        {
            logger.LogDebug($"Starting server task for {testServerChannel.SIPChannelEndPoint.ToString()}.");

            var serverSIPTransport = new SIPTransport();

            try
            {
                serverSIPTransport.AddSIPChannel(testServerChannel);

                logger.LogDebug(serverSIPTransport.GetDefaultSIPEndPoint().ToString());

                serverSIPTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
                {
                    logger.LogDebug($"Request received {localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipRequest.StatusLine}");

                    if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
                    {
                        SIPResponse optionsResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        serverSIPTransport.SendResponse(optionsResponse);
                    }
                };

                cts.Token.WaitHandle.WaitOne();
                //WaitHandle.WaitAny(new[] { cts.Token.WaitHandle });
            }
            finally
            {
                logger.LogDebug($"Server task for completed for {testServerChannel.SIPChannelEndPoint.ToString()}.");
                serverSIPTransport.Shutdown();
            }
        }

        /// <summary>
        /// Initialises a SIP tranpsort to act as the client in a single request/response exchange.
        /// </summary>
        /// <param name="testClientChannel">The client SIP channel to test.</param>
        /// <param name="serverUri">The URI of the server end point to test the client against.</param>
        /// <param name="tcs">The task completion source that this method will set if it receives the expected response.</param>
        private async Task RunClient(SIPChannel testClientChannel, SIPURI serverUri, TaskCompletionSource<bool> tcs)
        {
            logger.LogDebug($"Starting client task for {testClientChannel.SIPChannelEndPoint.ToString()}.");

            var clientSIPTransport = new SIPTransport();

            try
            {
                clientSIPTransport.AddSIPChannel(testClientChannel);

                logger.LogDebug(clientSIPTransport.GetDefaultSIPEndPoint().ToString());

                clientSIPTransport.SIPTransportResponseReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse) =>
                {
                    logger.LogDebug($"Response received {localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipResponse.ShortDescription}");
                    logger.LogDebug(sipResponse.ToString());

                    if (sipResponse.Status == SIPResponseStatusCodesEnum.Ok)
                    {
                        // Got the expected response, set the signal.
                        tcs.SetResult(true);
                    }
                };

                var optionsRequest = clientSIPTransport.GetRequest(SIPMethodsEnum.OPTIONS, serverUri);

                logger.LogDebug(optionsRequest.ToString());

                clientSIPTransport.SendRequest(optionsRequest);

                await tcs.Task;
            }
            finally
            {
                logger.LogDebug($"Client task completed for {testClientChannel.SIPChannelEndPoint.ToString()}.");
                clientSIPTransport.Shutdown();
            }
        }
    }
}
