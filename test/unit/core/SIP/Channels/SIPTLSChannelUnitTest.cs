//-----------------------------------------------------------------------------
// Filename: SIPTLSChannelUnitTest.cs
//
// Description: Unit tests for the SIPTLSChannel class.
//
// History:
// 19 Aug 2026	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPTLSChannelUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPTLSChannelUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Creates a throwaway self signed certificate usable as a TLS server certificate.
        /// </summary>
        private static X509Certificate2 CreateCertificate(string commonName)
        {
            using (var key = RSA.Create(2048))
            {
                var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                var subjectAlternativeName = new SubjectAlternativeNameBuilder();
                subjectAlternativeName.AddDnsName(commonName);
                request.CertificateExtensions.Add(subjectAlternativeName.Build());

                var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

                // A certificate created in memory holds its private key in an ephemeral key
                // set, which the Windows TLS stack will not use to serve a handshake. The
                // PKCS#12 round trip gives back one whose key is usable on every platform.
                return LoadPkcs12(certificate.Export(X509ContentType.Pkcs12));
            }
        }

        private static X509Certificate2 LoadPkcs12(byte[] pkcs12)
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(pkcs12, null);
#else
            return new X509Certificate2(pkcs12);
#endif
        }

        private static X509Certificate2 LoadPublicOnly(byte[] raw)
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadCertificate(raw);
#else
            return new X509Certificate2(raw);
#endif
        }

        /// <summary>
        /// Connects to the channel and returns the subject of the certificate it presented.
        /// </summary>
        private static async Task<string> GetPresentedSubjectAsync(int port)
        {
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);

                string subject = null;

                using (var sslStream = new SslStream(client.GetStream(), false,
                    (sender, certificate, chain, errors) =>
                    {
                        // Self signed, so the chain will not validate. What is under test is
                        // which certificate arrives, not whether it is trusted.
                        subject = certificate.Subject;
                        return true;
                    }))
                {
                    await sslStream.AuthenticateAsClientAsync("first.example.com");
                }

                return subject;
            }
        }

        /// <summary>
        /// Tests that the certificate a TLS channel presents can be replaced while the
        /// channel is listening, which is what lets a renewed certificate be picked up
        /// without a restart that would drop every connection the server is holding.
        /// </summary>
        [Fact]
        public async Task ReplaceServerCertificateUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using (var first = CreateCertificate("first.example.com"))
            using (var second = CreateCertificate("second.example.com"))
            {
                var channel = new SIPTLSChannel(first, new IPEndPoint(IPAddress.Loopback, 0));

                try
                {
                    int port = channel.ListeningSIPEndPoint.Port;

                    Assert.Equal("CN=first.example.com", await GetPresentedSubjectAsync(port));

                    channel.ServerCertificate = second;

                    // Same channel, same socket, no rebinding - only what it presents changes.
                    Assert.Equal("CN=second.example.com", await GetPresentedSubjectAsync(port));
                    Assert.Equal(port, channel.ListeningSIPEndPoint.Port);
                }
                finally
                {
                    channel.Close();
                }
            }
        }

        /// <summary>
        /// Tests that a connection established before the certificate was replaced is left
        /// alone by the replacement. This is the reason the swap is worth doing at all: a
        /// restart to pick up a renewed certificate drops every connection the server is
        /// holding, and for a SIP server those are its registered clients.
        /// </summary>
        [Fact]
        public async Task ReplaceServerCertificateLeavesEstablishedConnectionsUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using (var first = CreateCertificate("first.example.com"))
            using (var second = CreateCertificate("second.example.com"))
            {
                var channel = new SIPTLSChannel(first, new IPEndPoint(IPAddress.Loopback, 0));

                try
                {
                    using (var established = new TcpClient())
                    {
                        await established.ConnectAsync(IPAddress.Loopback, channel.ListeningSIPEndPoint.Port);

                        using (var sslStream = new SslStream(established.GetStream(), false, (s1, c, ch, e) => true))
                        {
                            await sslStream.AuthenticateAsClientAsync("first.example.com");

                            channel.ServerCertificate = second;

                            // A read that times out means the connection is still up. Had the
                            // channel been torn down and rebuilt, this would complete with
                            // zero bytes for the close instead.
                            var buffer = new byte[1];
                            var read = sslStream.ReadAsync(buffer, 0, 1);
                            var finished = await Task.WhenAny(read, Task.Delay(2000));

                            Assert.NotSame(read, finished);
                            Assert.True(established.Client.Connected);
                        }
                    }
                }
                finally
                {
                    channel.Close();
                }
            }
        }

        /// <summary>
        /// Tests that a certificate with no private key is refused when it is set rather
        /// than being accepted and then failing every subsequent handshake, where nothing
        /// would say which certificate was at fault.
        /// </summary>
        [Fact]
        public void ReplaceWithCertificateMissingPrivateKeyUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using (var certificate = CreateCertificate("first.example.com"))
            {
                var channel = new SIPTLSChannel(certificate, new IPEndPoint(IPAddress.Loopback, 0));

                try
                {
                    using (var publicOnly = LoadPublicOnly(certificate.RawData))
                    {
                        Assert.Throws<ArgumentException>(() => { channel.ServerCertificate = publicOnly; });
                    }

                    Assert.Equal("CN=first.example.com", channel.ServerCertificate.Subject);
                }
                finally
                {
                    channel.Close();
                }
            }
        }

        /// <summary>
        /// Tests that the certificate cannot be removed from a channel that is using one,
        /// which would leave it listening but unable to complete any handshake.
        /// </summary>
        [Fact]
        public void ReplaceWithNullCertificateUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using (var certificate = CreateCertificate("first.example.com"))
            {
                var channel = new SIPTLSChannel(certificate, new IPEndPoint(IPAddress.Loopback, 0));

                try
                {
                    Assert.Throws<ArgumentNullException>(() => { channel.ServerCertificate = null; });
                }
                finally
                {
                    channel.Close();
                }
            }
        }
    }
}
