//-----------------------------------------------------------------------------
// Filename: SIPTransportUnitTest.cs
//
// Description: Unit tests for the SIPTransport class.
// 
// History:
// 15 OCt 2019	Aaron Clauson	Created.
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

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Sys;

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
            TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>();

            string serverEP = $"[{IPAddress.IPv6Loopback}]:6060";
            string clientEP = $"[{IPAddress.IPv6Loopback}]:6061";

            // Server task (listen for SIP requests).
            var serverTask = Task.Run(async () => { await RunServer(serverEP, testComplete); });
            var clientTask = Task.Run(async () => { await RunClient(clientEP, serverEP, testComplete); });

            Task.WhenAny(new Task[] { serverTask, clientTask, Task.Delay(3000) }).Wait();

            if(testComplete.Task.IsCompleted == false)
            {
                // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                testComplete.SetResult(false);
            }

            Assert.IsTrue(testComplete.Task.Result);
        }

        /// <summary>
        /// Tests that an OPTIONS request can be sent and received on two separate IPv4 sockets using the loopback address.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void IPv4LoopbackSendReceiveTest()
        {
            TaskCompletionSource<bool> testComplete = new TaskCompletionSource<bool>();

            string serverEP = $"{IPAddress.Loopback}:6060";
            string clientEP = $"{IPAddress.Loopback}:6061";

            // Server task (listen for SIP requests).
            var serverTask = Task.Run(async () => { await RunServer(serverEP, testComplete); });
            var clientTask = Task.Run(async () => { await RunClient(clientEP, serverEP, testComplete); });

            Task.WhenAny(new Task[] { serverTask, clientTask, Task.Delay(3000) }).Wait();

            if (testComplete.Task.IsCompleted == false)
            {
                // The client did not set the completed signal. This means the delay task must have completed and hence the test failed.
                testComplete.SetResult(false);
            }

            Assert.IsTrue(testComplete.Task.Result);
        }

        /// <summary>
        /// Initialises a SIP transport to act as a server in single request/response exchange.
        /// </summary>
        /// <param name="listenEP">The end point for the server SIP tranport to listen on.</param>
        /// <param name="tcs">The task completion source gets set when the client receives the expected response.</param>
        private async Task RunServer(string listenEP, TaskCompletionSource<bool> tcs)
        {
            var serverSIPTransport = new SIPTransport();

            try
            {
                IPEndPoint ipv6EP = IPSocket.ParseSocketString(listenEP);
                var ipv6Channel = new SIPUDPChannel(ipv6EP);
                serverSIPTransport.AddSIPChannel(ipv6Channel);

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

                await tcs.Task;
            }
            finally
            {
                logger.LogDebug("Server task completed.");
                serverSIPTransport.Shutdown();
            }
        }

        /// <summary>
        /// Initialises a SIP tranpsort to act as the client in a single request/response exchange.
        /// </summary>
        /// <param name="listenEP">The end point for the client SIP transport to listen on.</param>
        /// <param name="serverEP">The server end point that the client will send the request to.</param>
        /// <param name="tcs">The task completion source that this method will set if it receives the expected response.</param>
        /// <returns></returns>
        private async Task RunClient(string listenEP, string serverEP, TaskCompletionSource<bool> tcs)
        {
            var clientSIPTransport = new SIPTransport();

            try
            {
                IPEndPoint ipv6EP = IPSocket.ParseSocketString(listenEP);
                var ipv6Channel = new SIPUDPChannel(ipv6EP);
                clientSIPTransport.AddSIPChannel(ipv6Channel);

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

                var optionsRequest = clientSIPTransport.GetRequest(SIPMethodsEnum.OPTIONS, SIPURI.ParseSIPURI($"sip:{serverEP}"));

                logger.LogDebug(optionsRequest.ToString());

                clientSIPTransport.SendRequest(optionsRequest);

                await tcs.Task;
            }
            finally
            {
                logger.LogDebug("Client task completed.");
                clientSIPTransport.Shutdown();
            }
        }
    }
}
