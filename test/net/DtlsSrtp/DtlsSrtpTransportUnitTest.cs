//-----------------------------------------------------------------------------
// Filename: DtlsSrtpTransportUnitTest.cs
//
// Description: Unit tests for the DtlsSrtpTransport class.
//
// History:
// 03 Jul 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class DtlsSrtpTransportUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public DtlsSrtpTransportUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that creating a new client DtlsSrtpTransport instance works correctly.
        /// </summary>
        [Fact]
        public void CreateClientInstanceUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            DtlsSrtpTransport dtlsTransport = new DtlsSrtpTransport(new DtlsSrtpClient());

            Assert.NotNull(dtlsTransport);
        }

        /// <summary>
        /// Tests that creating a new server DtlsSrtpTransport instance works correctly.
        /// </summary>
        [Fact]
        public void CreateServerInstanceUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            DtlsSrtpTransport dtlsTransport = new DtlsSrtpTransport(new DtlsSrtpServer());

            Assert.NotNull(dtlsTransport);
        }

        /// <summary>
        /// Tests that creating a client and server DtlsSrtpTransport can perform a DTLS
        /// handshake successfully.
        /// </summary>
        [Fact]
        public void DoHandshakeUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var dtlsClient = new DtlsSrtpClient();
            var dtlsServer = new DtlsSrtpServer();

            DtlsSrtpTransport dtlsClientTransport = new DtlsSrtpTransport(dtlsClient);
            DtlsSrtpTransport dtlsServerTransport = new DtlsSrtpTransport(dtlsServer);

            dtlsClientTransport.OnDataReady += (buf) => dtlsServerTransport.WriteToRecvStream(buf);
            dtlsServerTransport.OnDataReady += (buf) => dtlsClientTransport.WriteToRecvStream(buf);

            var serverTask = Task.Run<bool>(dtlsServerTransport.DoHandshake);
            var clientTask = Task.Run<bool>(dtlsClientTransport.DoHandshake);

            bool didComplete = Task.WaitAll(new Task[] { serverTask, clientTask }, 5000);

            Assert.True(didComplete);
            Assert.True(serverTask.Result);
            Assert.True(clientTask.Result);

            logger.LogDebug($"DTLS client received server fingerprint {dtlsClient.RemoteFingerprint}.");
            logger.LogDebug($"DTLS server fingerprint {dtlsServer.Fingerprint}.");
            logger.LogDebug($"DTLS server received client fingerprint {DtlsUtils.Fingerprint(dtlsServer.ClientCertificate)}.");

            Assert.NotNull(dtlsClient.RemoteFingerprint);
            Assert.NotNull(dtlsServer.Fingerprint);
        }

        /// <summary>
        /// Tests that attempting a client handshake times out correctly.
        /// </summary>
        [Fact]
        public async void DoHandshakeClientTimeoutUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            DtlsSrtpTransport dtlsClientTransport = new DtlsSrtpTransport(new DtlsSrtpClient());
            dtlsClientTransport.TimeoutMilliseconds = 2000;

            var result = await Task.Run<bool>(dtlsClientTransport.DoHandshake);

            Assert.False(result);
        }

        /// <summary>
        /// Tests that attempting a server handshake times out correctly.
        /// </summary>
        [Fact]
        public async void DoHandshakeServerTimeoutUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            DtlsSrtpTransport dtlsServerTransport = new DtlsSrtpTransport(new DtlsSrtpServer());
            dtlsServerTransport.TimeoutMilliseconds = 2000;

            var result = await Task.Run<bool>(dtlsServerTransport.DoHandshake);

            Assert.False(result);
        }
    }
}
