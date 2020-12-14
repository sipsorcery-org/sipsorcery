//-----------------------------------------------------------------------------
// Filename: SIPTransportUnitTest.cs
//
// Description: Unit tests for the SIPTransport class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 15 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
// 14 Dec 2020  Aaron Clauson   Moved from unit to integration tests (while not 
//              really integration tests the duration is long'ish for a unit test).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.SIP.IntegrationTests
{
    [Trait("Category", "transport")]
    public class SIPTransportUnitTest
    {
        private const int TRANSPORT_TEST_TIMEOUT = 15000;

        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPTransportUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate IPv6 sockets using the loopback address.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6")]
        public void IPv6LoopbackSendReceiveTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            if (!Socket.OSSupportsIPv6)
            {
                logger.LogDebug("Test skipped as OS does not support IPv6.");
            }
            else
            {
                ManualResetEventSlim serverReadyEvent = new ManualResetEventSlim(false);
                CancellationTokenSource cancelServer = new CancellationTokenSource();
                TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                var serverChannel = new SIPUDPChannel(IPAddress.IPv6Loopback, 0);
                var clientChannel = new SIPUDPChannel(IPAddress.IPv6Loopback, 0);

                var serverTask = Task.Run(() => { RunServer(serverChannel, cancelServer, serverReadyEvent); });
                var clientTask = Task.Run(async () =>
                {
                    await RunClient(
    clientChannel,
    serverChannel.GetContactURI(SIPSchemesEnum.sip, new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.IPv6Loopback, 0))),
    testComplete,
    cancelServer,
    serverReadyEvent);
                });

                serverReadyEvent.Wait();
                if (!Task.WhenAny(new Task[] { serverTask, clientTask }).Wait(TRANSPORT_TEST_TIMEOUT))
                {
                    logger.LogWarning($"Tasks timed out");
                }

                if (testComplete.Task.IsCompleted == false)
                {
                    // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                    testComplete.SetResult(false);
                }

                Assert.True(testComplete.Task.Result);

                cancelServer.Cancel();
            }
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate IPv4 sockets using the loopback address.
        /// </summary>
        [Fact]
        public void IPv4LoopbackSendReceiveTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            ManualResetEventSlim serverReadyEvent = new ManualResetEventSlim(false);
            CancellationTokenSource cancelServer = new CancellationTokenSource();
            TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var serverChannel = new SIPUDPChannel(IPAddress.Loopback, 0);
            var clientChannel = new SIPUDPChannel(IPAddress.Loopback, 0);

            var serverTask = Task.Run(() => { RunServer(serverChannel, cancelServer, serverReadyEvent); });
            var clientTask = Task.Run(async () =>
            {
                await RunClient(
clientChannel,
serverChannel.GetContactURI(SIPSchemesEnum.sip, new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 0))),
testComplete,
cancelServer,
serverReadyEvent);
            });

            serverReadyEvent.Wait();
            if (!Task.WhenAny(new Task[] { serverTask, clientTask }).Wait(TRANSPORT_TEST_TIMEOUT))
            {
                logger.LogWarning($"Tasks timed out");
            }

            if (testComplete.Task.IsCompleted == false)
            {
                // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                testComplete.SetResult(false);
            }

            Assert.True(testComplete.Task.Result);
            cancelServer.Cancel();

            logger.LogDebug("Test complete.");
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate TCP IPv6 sockets using the loopback address.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6")]
        public void IPv6TcpLoopbackSendReceiveTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            if (!Socket.OSSupportsIPv6)
            {
                logger.LogDebug("Test skipped as OS does not support IPv6.");
            }
            else
            {
                ManualResetEventSlim serverReadyEvent = new ManualResetEventSlim(false);
                CancellationTokenSource cancelServer = new CancellationTokenSource();
                TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                var serverChannel = new SIPTCPChannel(IPAddress.IPv6Loopback, 0);
                serverChannel.DisableLocalTCPSocketsCheck = true;
                var clientChannel = new SIPTCPChannel(IPAddress.IPv6Loopback, 0);
                clientChannel.DisableLocalTCPSocketsCheck = true;

                var serverTask = Task.Run(() => { RunServer(serverChannel, cancelServer, serverReadyEvent); });
                var clientTask = Task.Run(async () =>
                {
                    await RunClient(
    clientChannel,
    serverChannel.GetContactURI(SIPSchemesEnum.sip, new SIPEndPoint(SIPProtocolsEnum.tcp, new IPEndPoint(IPAddress.IPv6Loopback, 0))),
    testComplete,
    cancelServer,
    serverReadyEvent);
                });

                serverReadyEvent.Wait();
                if (!Task.WhenAny(new Task[] { serverTask, clientTask }).Wait(TRANSPORT_TEST_TIMEOUT))
                {
                    logger.LogWarning($"Tasks timed out");
                }

                if (testComplete.Task.IsCompleted == false)
                {
                    // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                    testComplete.SetResult(false);
                }

                Assert.True(testComplete.Task.Result);

                cancelServer.Cancel();
            }
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate IPv4 TCP sockets using the loopback address.
        /// </summary>
        [Fact]
        public void IPv4TcpLoopbackSendReceiveTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            ManualResetEventSlim serverReadyEvent = new ManualResetEventSlim(false);
            CancellationTokenSource cancelServer = new CancellationTokenSource();
            TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var serverChannel = new SIPTCPChannel(IPAddress.Loopback, 0);
            serverChannel.DisableLocalTCPSocketsCheck = true;
            var clientChannel = new SIPTCPChannel(IPAddress.Loopback, 0);
            clientChannel.DisableLocalTCPSocketsCheck = true;

            Task.Run(() => { RunServer(serverChannel, cancelServer, serverReadyEvent); });
            var clientTask = Task.Run(async () =>
            {
                await RunClient(
clientChannel,
serverChannel.GetContactURI(SIPSchemesEnum.sip, new SIPEndPoint(SIPProtocolsEnum.tcp, new IPEndPoint(IPAddress.Loopback, 0))),
testComplete,
cancelServer,
serverReadyEvent);
            });

            serverReadyEvent.Wait();
            if (!Task.WhenAny(new Task[] { clientTask }).Wait(TRANSPORT_TEST_TIMEOUT))
            {
                logger.LogWarning($"Tasks timed out");
            }

            if (testComplete.Task.IsCompleted == false)
            {
                // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                testComplete.SetResult(false);
            }

            Assert.True(testComplete.Task.Result);

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
        [Fact]
        public void IPv4TcpLoopbackConsecutiveSendReceiveTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // This test fails on WSL and Linux due to closed TCP sockets going into the TIME_WAIT state.
            // See comment in SIPTCPChannel.OnSIPStreamDisconnected for additional info.
            if (Environment.OSVersion.Platform != PlatformID.Unix)
            {
                ManualResetEventSlim serverReadyEvent = new ManualResetEventSlim(false);
                CancellationTokenSource cancelServer = new CancellationTokenSource();
                var serverChannel = new SIPTCPChannel(IPAddress.Loopback, 0);
                serverChannel.DisableLocalTCPSocketsCheck = true;

                var serverTask = Task.Run(() => { RunServer(serverChannel, cancelServer, serverReadyEvent); });

                for (int i = 1; i < 3; i++)
                {
                    TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                    var clientChannel = new SIPTCPChannel(IPAddress.Loopback, 0);
                    clientChannel.DisableLocalTCPSocketsCheck = true;
                    SIPURI serverUri = serverChannel.GetContactURI(SIPSchemesEnum.sip, new SIPEndPoint(SIPProtocolsEnum.tcp, new IPEndPoint(IPAddress.Loopback, 0)));

                    logger.LogDebug($"Server URI {serverUri.ToString()}.");

                    var clientTask = Task.Run(async () => { await RunClient(clientChannel, serverUri, testComplete, cancelServer, serverReadyEvent); });

                    serverReadyEvent.Wait();
                    if (!Task.WhenAny(new Task[] { clientTask }).Wait(TRANSPORT_TEST_TIMEOUT))
                    {
                        logger.LogWarning($"Tasks timed out");
                    }

                    if (testComplete.Task.IsCompleted == false)
                    {
                        // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                        testComplete.SetResult(false);
                    }

                    Assert.True(testComplete.Task.Result);

                    logger.LogDebug($"Completed for test run {i}.");

                    Task.Delay(1000).Wait();
                }

                cancelServer.Cancel();
            }
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate TLS IPv6 sockets using the loopback address.
        /// </summary>
        /// <remarks>
        /// Fails on macosx, see https://github.com/dotnet/runtime/issues/23635. Fixed in .NET Core 5, 
        /// see https://github.com/dotnet/corefx/pull/42226.
        /// </remarks>
        [Fact]
        [Trait("Category", "IPv6")]
        public void IPv6TlsLoopbackSendReceiveTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            if (!Socket.OSSupportsIPv6)
            {
                logger.LogDebug("Test skipped as OS does not support IPv6.");
            }
            else if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                logger.LogDebug("Test skipped as MacOS is not able to load certificates from a .pfx file pre .NET Core 5.0.");
            }
            else
            {
                ManualResetEventSlim serverReadyEvent = new ManualResetEventSlim(false);
                CancellationTokenSource cancelServer = new CancellationTokenSource();
                TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                Assert.True(File.Exists(@"certs/localhost.pfx"), "The TLS transport channel test was missing the localhost.pfx certificate file.");

                var serverCertificate = new X509Certificate2(@"certs/localhost.pfx", "");
                var verifyCert = serverCertificate.Verify();
                logger.LogDebug("Server Certificate loaded from file, Subject=" + serverCertificate.Subject + ", valid=" + verifyCert + ".");

                var serverChannel = new SIPTLSChannel(serverCertificate, IPAddress.IPv6Loopback, 0);
                serverChannel.DisableLocalTCPSocketsCheck = true;
                var clientChannel = new SIPTLSChannel(new IPEndPoint(IPAddress.IPv6Loopback, 0));
                clientChannel.DisableLocalTCPSocketsCheck = true;

                var serverTask = Task.Run(() => { RunServer(serverChannel, cancelServer, serverReadyEvent); });
                var clientTask = Task.Run(async () =>
                {
                    await RunClient(
    clientChannel,
    serverChannel.GetContactURI(SIPSchemesEnum.sips, new SIPEndPoint(SIPProtocolsEnum.tls, new IPEndPoint(IPAddress.IPv6Loopback, 0))),
    testComplete,
    cancelServer,
    serverReadyEvent);
                });

                if (!Task.WhenAny(new Task[] { serverTask, clientTask }).Wait(TRANSPORT_TEST_TIMEOUT))
                {
                    logger.LogWarning($"Tasks timed out");
                }

                if (testComplete.Task.IsCompleted == false)
                {
                    // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                    testComplete.SetResult(false);
                }

                Assert.True(testComplete.Task.Result);

                cancelServer.Cancel();
            }
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate IPv4 TLS sockets using the loopback address.
        /// </summary>
        /// <remarks>
        /// Fails on macosx, see https://github.com/dotnet/runtime/issues/23635. Fixed in .NET Core 5, 
        /// see https://github.com/dotnet/corefx/pull/42226.
        /// </remarks>
        [Fact]
        public void IPv4TlsLoopbackSendReceiveTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                logger.LogDebug("Test skipped as MacOS is not able to load certificates from a .pfx file pre .NET Core 5.0.");
            }
            else
            {
                ManualResetEventSlim serverReadyEvent = new ManualResetEventSlim(false);
                CancellationTokenSource cancelServer = new CancellationTokenSource();
                TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                Assert.True(File.Exists(@"certs/localhost.pfx"), "The TLS transport channel test was missing the localhost.pfx certificate file.");

                var serverCertificate = new X509Certificate2(@"certs/localhost.pfx", "");
                var verifyCert = serverCertificate.Verify();
                logger.LogDebug("Server Certificate loaded from file, Subject=" + serverCertificate.Subject + ", valid=" + verifyCert + ".");

                var serverChannel = new SIPTLSChannel(serverCertificate, IPAddress.Loopback, 0);
                serverChannel.DisableLocalTCPSocketsCheck = true;
                var clientChannel = new SIPTLSChannel(new IPEndPoint(IPAddress.Loopback, 0));
                clientChannel.DisableLocalTCPSocketsCheck = true;

                var serverTask = Task.Run(() => { RunServer(serverChannel, cancelServer, serverReadyEvent); });
                var clientTask = Task.Run(async () =>
                {
                    await RunClient(
    clientChannel,
    serverChannel.GetContactURI(SIPSchemesEnum.sips, new SIPEndPoint(SIPProtocolsEnum.tls, new IPEndPoint(IPAddress.Loopback, 0))),
    testComplete,
    cancelServer,
    serverReadyEvent);
                });

                if (!Task.WhenAny(new Task[] { serverTask, clientTask }).Wait(TRANSPORT_TEST_TIMEOUT))
                {
                    logger.LogWarning($"Tasks timed out");
                }

                if (testComplete.Task.IsCompleted == false)
                {
                    // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                    testComplete.SetResult(false);
                }

                Assert.True(testComplete.Task.Result);

                cancelServer.Cancel();
            }
        }

        /// <summary>
        /// Tests that SIP messages can be correctly extracted from a TCP stream when arbitrarily fragmented.
        /// This test is a little bit tricky. The pseudo code is:
        /// - Create a standard TCP server (not a SIP channel) to listen on a random end point.
        /// - Create a SIP TCP channel to listen on a random end point.
        /// - Repeat N times:
        ///   - Initiate a connection from the SIP TCP channel to the listening server,
        ///   - The server will accept the connection and create a new OPTIONS request and send it 
        ///     to the client socket.
        ///   - The connection is closed.
        /// - Keep track of the number of requests that are received from the SIP TCP channel initiated
        ///   connections and if it matches N the test passes.
        /// </summary>
        [Fact]
        public void TcpTrickleReceiveTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // TCP server.
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var actualEP = listener.LocalEndpoint as IPEndPoint;

            // SIP TCP Channel
            var transport = new SIPTransport();
            var tcpChannel = new SIPTCPChannel(new IPEndPoint(IPAddress.Loopback, 0));
            tcpChannel.DisableLocalTCPSocketsCheck = true;
            transport.AddSIPChannel(tcpChannel);

            int requestCount = 10;
            int recvdReqCount = 0;

            Task.Run(() =>
            {
                try
                {
                    var tcpClient = listener.AcceptTcpClient();
                    logger.LogDebug($"Dummy TCP listener accepted client with remote end point {tcpClient.Client.RemoteEndPoint}.");
                    for (int i = 0; i < requestCount; i++)
                    {
                        logger.LogDebug($"Sending request {i}.");

                        var req = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, new SIPURI(SIPSchemesEnum.sip, tcpChannel.ListeningSIPEndPoint));
                        byte[] reqBytes = Encoding.UTF8.GetBytes(req.ToString());

                        tcpClient.GetStream().Write(reqBytes, 0, reqBytes.Length);
                        tcpClient.GetStream().Flush();

                        Task.Delay(30).Wait();
                    }
                    tcpClient.GetStream().Close();
                }
                catch (Exception excp)
                {
                    logger.LogError($"Exception on dummy TCP listener task. {excp.Message}");
                    testComplete.SetResult(false);
                }
            });

            transport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                logger.LogDebug($"Request received {localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipRequest.StatusLine}");
                logger.LogDebug(sipRequest.ToString());
                Interlocked.Increment(ref recvdReqCount);

                if (recvdReqCount == requestCount)
                {
                    if (!testComplete.TrySetResult(true))
                    {
                        logger.LogWarning($"TcpTrickleReceiveTest: FAILED to set result on CompletionSource.");
                    }
                }

                return Task.FromResult(0);
            };

            transport.SIPTransportResponseReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse) =>
            {
                logger.LogDebug($"Response received {localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipResponse.ShortDescription}");
                logger.LogDebug(sipResponse.ToString());

                return Task.FromResult(0);
            };

            if (!tcpChannel.ConnectClientAsync(actualEP, null, null).Wait(TimeSpan.FromMilliseconds(TRANSPORT_TEST_TIMEOUT)))
            {
                logger.LogWarning($"ConnectClientAsync timed out");
            }

            logger.LogDebug("Test client connected.");

            if (!Task.WhenAny(new Task[] { testComplete.Task }).Wait(TRANSPORT_TEST_TIMEOUT))
            {
                logger.LogWarning($"Tasks timed out");
            }

            logger.LogDebug("Test completed, shutting down SIP transport layer.");

            transport.Shutdown();

            logger.LogDebug("SIP transport layer shutdown.");

            // Give the SIP transport time to shutdown. Keeps exception messages out of the logs.
            Task.Delay(500).Wait();

            Assert.True(testComplete.Task.IsCompleted);
            Assert.True(testComplete.Task.Result);
            Assert.True(requestCount == recvdReqCount, $"The count of {recvdReqCount} for the requests received did not match what was expected.");
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent on a client web socket SIP channel and get
        /// received on server web socket SIP channel.
        /// </summary>
        [Fact]
        public async void WebSocketLoopbackSendReceiveTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var serverChannel = new SIPWebSocketChannel(IPAddress.Loopback, 9000);
            var clientChannel = new SIPClientWebSocketChannel();
            var sipTransport = new SIPTransport();
            sipTransport.AddSIPChannel(new List<SIPChannel> { serverChannel, clientChannel });

            ManualResetEvent gotResponseMre = new ManualResetEvent(false);

            sipTransport.SIPTransportRequestReceived += (localSIPEndPoint, remoteEndPoint, sipRequest) =>
            {
                SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                return sipTransport.SendResponseAsync(optionsResponse);
            };

            SIPResponse rtnResponse = null;

            sipTransport.SIPTransportResponseReceived += (localSIPEndPoint, remoteEndPoint, sipResponse) =>
            {
                rtnResponse = sipResponse;
                gotResponseMre.Set();
                return Task.CompletedTask;
            };

            var serverUri = serverChannel.GetContactURI(SIPSchemesEnum.sip, clientChannel.ListeningSIPEndPoint);
            var optionsRequest = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, serverUri);
            await sipTransport.SendRequestAsync(optionsRequest);

            gotResponseMre.WaitOne(TRANSPORT_TEST_TIMEOUT, false);

            Assert.NotNull(rtnResponse);

            sipTransport.Shutdown();

            logger.LogDebug("Test complete.");
        }

        /// <summary>
        /// Tests that large request and responses can be correctly sent and received via the web socket
        /// SIP channels. Web sockets have special rules about detecting the end of sends.
        /// </summary>
        [Fact]
        public async void WebSocketLoopbackLargeSendReceiveTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var serverChannel = new SIPWebSocketChannel(IPAddress.Loopback, 9001);
            var clientChannel = new SIPClientWebSocketChannel();
            var sipTransport = new SIPTransport();
            sipTransport.AddSIPChannel(new List<SIPChannel> { serverChannel, clientChannel });

            ManualResetEvent gotResponseMre = new ManualResetEvent(false);

            SIPRequest receivedRequest = null;
            SIPResponse receivedResponse = null;

            sipTransport.SIPTransportRequestReceived += (localSIPEndPoint, remoteEndPoint, sipRequest) =>
            {
                receivedRequest = sipRequest;

                SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                optionsResponse.Header.UnknownHeaders.Add($"X-Response-Random:{Crypto.GetRandomString(1000)}");
                optionsResponse.Header.UnknownHeaders.Add("X-Response-Final: TheEnd");

                return sipTransport.SendResponseAsync(optionsResponse);
            };

            sipTransport.SIPTransportResponseReceived += (localSIPEndPoint, remoteEndPoint, sipResponse) =>
            {
                receivedResponse = sipResponse;
                gotResponseMre.Set();
                return Task.CompletedTask;
            };

            var serverUri = serverChannel.GetContactURI(SIPSchemesEnum.sip, clientChannel.ListeningSIPEndPoint);
            var optionsRequest = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, serverUri);
            optionsRequest.Header.UnknownHeaders.Add($"X-Request-Random:{Crypto.GetRandomString(1000)}");
            optionsRequest.Header.UnknownHeaders.Add("X-Request-Final: TheEnd");
            await sipTransport.SendRequestAsync(optionsRequest);

            gotResponseMre.WaitOne(TRANSPORT_TEST_TIMEOUT, false);

            Assert.NotNull(receivedRequest);
            Assert.NotNull(receivedResponse);
            //rj2: confirm that we have received the whole SIP-Message by checking for the last x-header (issue #175, websocket fragmented send/receive)
            Assert.Contains("X-Request-Final: TheEnd", receivedRequest.Header.UnknownHeaders);
            Assert.Contains("X-Response-Final: TheEnd", receivedResponse.Header.UnknownHeaders);

            sipTransport.Shutdown();

            logger.LogDebug("Test complete.");
        }

        /// <summary>
        /// Initialises a SIP transport to act as a server in single request/response exchange.
        /// </summary>
        /// <param name="testServerChannel">The server SIP channel to test.</param>
        /// <param name="cts">Cancellation token to tell the server when to shutdown.</param>
        /// <param name="serverReadyEvent">Used to notify the client task that the server task is 
        /// ready for the unit test to commence.</param>
        private void RunServer(
            SIPChannel testServerChannel,
            CancellationTokenSource cts,
            ManualResetEventSlim serverReadyEvent)
        {
            logger.LogDebug($"RunServer test channel created on {testServerChannel.ListeningSIPEndPoint}.");

            var serverSIPTransport = new SIPTransport();

            try
            {
                serverSIPTransport.AddSIPChannel(testServerChannel);

                serverSIPTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
                {
                    logger.LogDebug($"Request received {localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipRequest.StatusLine}");

                    if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
                    {
                        SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        logger.LogDebug(optionsResponse.ToString());
                        return serverSIPTransport.SendResponseAsync(optionsResponse);
                    }

                    return Task.CompletedTask;
                };

                serverSIPTransport.SIPRequestInTraceEvent += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
                {
                    logger.LogDebug($"SERVER REQUEST IN {localSIPEndPoint}<-{remoteEndPoint}");
                    logger.LogDebug(sipRequest.ToString());
                };

                serverSIPTransport.SIPResponseOutTraceEvent += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse) =>
                {
                    logger.LogDebug($"SERVER RESPONSE OUT {localSIPEndPoint}->{remoteEndPoint}");
                    logger.LogDebug(sipResponse.ToString());
                };

                serverReadyEvent.Set();

                cts.Token.WaitHandle.WaitOne();
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception RunServer. {excp.Message}");
            }
            finally
            {
                logger.LogDebug($"Server task for completed for {testServerChannel.ListeningSIPEndPoint}.");
                serverSIPTransport.Shutdown();
                logger.LogDebug($"Server task SIP transport shutdown.");
            }
        }

        /// <summary>
        /// Initialises a SIP transport to act as the client in a single request/response exchange.
        /// </summary>
        /// <param name="testClientChannel">The client SIP channel to test.</param>
        /// <param name="serverUri">The URI of the server end point to test the client against.</param>
        /// <param name="tcs">The task completion source that this method will set if it receives the expected response.</param>
        /// <param name="cts">Cancellation token in case the server task intialisation fails.</param>
        /// <param name="serverReadyEvent">Event to notify when the server thread is ready for the test to commence.</param>
        private async Task RunClient(
            SIPChannel testClientChannel,
            SIPURI serverUri,
            TaskCompletionSource<bool> tcs,
            CancellationTokenSource cts,
            ManualResetEventSlim serverReadyEvent)
        {
            logger.LogDebug($"RunClient Starting client task for {testClientChannel.ListeningSIPEndPoint}.");

            var clientSIPTransport = new SIPTransport();

            try
            {
                clientSIPTransport.AddSIPChannel(testClientChannel);

                logger.LogDebug($"RunClient test channel created on {testClientChannel.ListeningSIPEndPoint}.");

                clientSIPTransport.SIPTransportResponseReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse) =>
                {
                    logger.LogDebug($"Expected response received {localSIPEndPoint}<-{remoteEndPoint}: {sipResponse.ShortDescription}");

                    if (sipResponse.Status == SIPResponseStatusCodesEnum.Ok)
                    {
                        // Got the expected response, set the signal.
                        if (!tcs.TrySetResult(true))
                        {
                            logger.LogWarning($"RunClient on test channel {testClientChannel.ListeningSIPEndPoint} FAILED to set result on CompletionSource.");
                        }
                    }

                    return Task.CompletedTask;
                };

                clientSIPTransport.SIPRequestOutTraceEvent += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
                {
                    logger.LogDebug($"CLIENT REQUEST OUT {localSIPEndPoint}->{remoteEndPoint}");
                    logger.LogDebug(sipRequest.ToString());
                };

                clientSIPTransport.SIPResponseInTraceEvent += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse) =>
                {
                    logger.LogDebug($"CLIENT RESPONSE IN {localSIPEndPoint}<-{remoteEndPoint}");
                    logger.LogDebug(sipResponse.ToString());
                };

                var optionsRequest = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, serverUri);

                logger.LogDebug($"RunClient waiting for server to get ready on {serverUri.CanonicalAddress}.");
                serverReadyEvent.Wait(cts.Token);

                await clientSIPTransport.SendRequestAsync(optionsRequest);

                await tcs.Task;
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception RunClient. {excp.Message}");
            }
            finally
            {
                logger.LogDebug($"Client task completed for {testClientChannel.ListeningSIPEndPoint}.");
                clientSIPTransport.Shutdown();
                logger.LogDebug($"Client task SIP transport shutdown.");
            }
        }
    }
}
