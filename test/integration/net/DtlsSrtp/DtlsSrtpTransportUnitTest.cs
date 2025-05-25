//-----------------------------------------------------------------------------
// Filename: DtlsSrtpTransportUnitTest.cs
//
// Description: Unit tests for the DtlsSrtpTransport class.
//
// History:
// 03 Jul 2020	Aaron Clauson	Created.
// 14 Dec 2020  Aaron Clauson   Moved from unit to integration tests (while not 
//              really integration tests the duration is long'ish for a unit test).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Xunit;

namespace SIPSorcery.Net.IntegrationTests
{
    [Trait("Category", "integration")]
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
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var crypto = new BcTlsCrypto();
            (var tlsCert, var pvtKey) = DtlsUtils.CreateSelfSignedTlsCert(crypto);
            DtlsSrtpTransport dtlsTransport = new DtlsSrtpTransport(new DtlsSrtpClient(crypto, tlsCert, pvtKey));

            Assert.NotNull(dtlsTransport);
        }

        /// <summary>
        /// Tests that creating a new server DtlsSrtpTransport instance works correctly.
        /// </summary>
        [Fact]
        public void CreateServerInstanceUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            DtlsSrtpTransport dtlsTransport = new DtlsSrtpTransport(new DtlsSrtpServer(new BcTlsCrypto()));

            Assert.NotNull(dtlsTransport);
        }

        /// <summary>
        /// Tests that creating a client and server DtlsSrtpTransport can perform a DTLS
        /// handshake successfully.
        /// </summary>
        [Fact]
        public async Task DoHandshakeUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var dtlsClient = new DtlsSrtpClient(new BcTlsCrypto());
            var dtlsServer = new DtlsSrtpServer(new BcTlsCrypto());

            DtlsSrtpTransport dtlsClientTransport = new DtlsSrtpTransport(dtlsClient);
            dtlsClientTransport.TimeoutMilliseconds = 5000;
            DtlsSrtpTransport dtlsServerTransport = new DtlsSrtpTransport(dtlsServer);
            dtlsServerTransport.TimeoutMilliseconds = 5000;

            dtlsClientTransport.OnDataReady += (buf) =>
            {
                logger.LogDebug("DTLS client transport sending {BufferLength} bytes to server.", buf.Length);
                dtlsServerTransport.WriteToRecvStream(buf);
            };
            dtlsServerTransport.OnDataReady += (buf) =>
            {
                logger.LogDebug("DTLS server transport sending {BufferLength} bytes to client.", buf.Length);
                dtlsClientTransport.WriteToRecvStream(buf);
            };

            var serverTask = Task.Run<bool>(() => dtlsServerTransport.DoHandshake(out _));
            var clientTask = Task.Run<bool>(() => dtlsClientTransport.DoHandshake(out _));

            var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(5000));
            var winner = await Task.WhenAny(serverTask, clientTask, timeoutTask);

            if (winner == timeoutTask)
            {
                Assert.Fail($"Test timed out after 5000ms.");
            }

            Assert.True(await serverTask);
            Assert.True(await clientTask);

            logger.LogDebug("DTLS client fingerprint       : {Fingerprint}", dtlsServer.Fingerprint);
            //logger.LogDebug($"DTLS client server fingerprint: {dtlsClient.ServerFingerprint}.");
            logger.LogDebug("DTLS server fingerprint       : {Fingerprint}", dtlsServer.Fingerprint);
            //logger.LogDebug($"DTLS server client fingerprint: {dtlsServer.ClientFingerprint}.");

            Assert.NotNull(dtlsClient.GetRemoteCertificate());
            Assert.NotNull(dtlsServer.GetRemoteCertificate());
            //Assert.Equal(dtlsServer.Fingerprint.algorithm, dtlsClient.ServerFingerprint.algorithm);
            //Assert.Equal(dtlsServer.Fingerprint.value, dtlsClient.ServerFingerprint.value);
            //Assert.Equal(dtlsClient.Fingerprint.algorithm, dtlsServer.ClientFingerprint.algorithm);
            //Assert.Equal(dtlsClient.Fingerprint.value, dtlsServer.ClientFingerprint.value);
        }

        /// <summary>
        /// Tests that attempting a client handshake times out correctly.
        /// </summary>
        [Fact]
        public async Task DoHandshakeClientTimeoutUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            DtlsSrtpTransport dtlsClientTransport = new DtlsSrtpTransport(new DtlsSrtpClient(new BcTlsCrypto()));
            dtlsClientTransport.TimeoutMilliseconds = 2000;

            var result = await Task.Run<bool>(() => dtlsClientTransport.DoHandshake(out _));

            Assert.False(result);
        }

        /// <summary>
        /// Tests that attempting a server handshake times out correctly.
        /// </summary>
        [Fact]
        public async Task DoHandshakeServerTimeoutUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            DtlsSrtpTransport dtlsServerTransport = new DtlsSrtpTransport(new DtlsSrtpServer(new BcTlsCrypto()));
            dtlsServerTransport.TimeoutMilliseconds = 2000;

            var result = await Task.Run<bool>(() => dtlsServerTransport.DoHandshake(out _));

            Assert.False(result);
        }
    }
}
