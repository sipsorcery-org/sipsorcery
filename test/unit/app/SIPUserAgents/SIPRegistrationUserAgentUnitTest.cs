using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPRegistrationUserAgentUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPRegistrationUserAgentUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void RegisterStartWithCustomHeaderTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            SIPTransport transport = new SIPTransport();
            MockSIPChannel channel = new MockSIPChannel(new IPEndPoint(IPAddress.Any, 0));
            transport.AddSIPChannel(channel);
            SIPRegistrationUserAgent userAgent = new SIPRegistrationUserAgent(
                transport,
                null,
                new SIPURI("alice", "192.168.11.50", null, SIPSchemesEnum.sip, SIPProtocolsEnum.udp),
                "alice",
                "password123",
                null,
                "192.168.11.50",
                new SIPURI(SIPSchemesEnum.sip, IPAddress.Any, 0),
                120,
                new[] { "My-Header: value" });

            userAgent.Start();

            channel.SIPMessageSent.WaitOne(5000);
            Assert.Contains("My-Header: value", channel.LastSIPMessageSent);

            userAgent.Stop();
        }

        [Fact]
        public void RegisterWithAdjustedRegisterHeaderTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            SIPTransport transport = new SIPTransport();
            MockSIPChannel channel = new MockSIPChannel(new System.Net.IPEndPoint(IPAddress.Any, 0));
            transport.AddSIPChannel(channel);
            SIPRegistrationUserAgent userAgent = new SIPRegistrationUserAgent(
                transport,
                null,
                new SIPURI("alice", "192.168.11.50", null, SIPSchemesEnum.sip, SIPProtocolsEnum.udp),
                "alice",
                "password123",
                null,
                "192.168.11.50",
                new SIPURI(SIPSchemesEnum.sip, IPAddress.Any, 0),
                120,
                new[] { "My-Header: value" });
            SIPContactHeader testHeader = new SIPContactHeader("Contact Name", new SIPURI("User", "Host", "Param=Value"));
            userAgent.AdjustRegister = register =>
            {
                register.Header.Contact = new List<SIPContactHeader>
                {
                    testHeader
                };
                return register;
            };

            userAgent.Start();

            channel.SIPMessageSent.WaitOne(5000);
            Assert.Contains(testHeader.ToString(), channel.LastSIPMessageSent);

            userAgent.Stop();
        }

        [Fact]
        public void RegisterWithHA1DigestResolverRespondsToSHA256ChallengeTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string username = "alice";
            string password = "password123";
            string registrarHost = "192.168.11.50";
            GetHA1DigestDelegate getHA1Digest = (resolvedUsername, realm, digestAlgorithm) =>
                resolvedUsername == username && realm == registrarHost && digestAlgorithm == DigestAlgorithmsEnum.SHA256
                    ? HTTPDigest.DigestCalcHA1(username, registrarHost, password, DigestAlgorithmsEnum.SHA256)
                    : null;

            SIPTransport transport = new SIPTransport();
            MockSIPChannel channel = new MockSIPChannel(new IPEndPoint(IPAddress.Any, 0));
            transport.AddSIPChannel(channel);

            SIPRegistrationUserAgent userAgent = new SIPRegistrationUserAgent(
                transport,
                null,
                new SIPURI(username, registrarHost, null, SIPSchemesEnum.sip, SIPProtocolsEnum.udp),
                getHA1Digest,
                username,
                null,
                registrarHost,
                new SIPURI(SIPSchemesEnum.sip, IPAddress.Any, 0),
                120,
                null);

            try
            {
                userAgent.Start();

                Assert.True(channel.SIPMessageSent.WaitOne(5000));
                SIPRequest initialRegister = SIPRequest.ParseSIPRequest(channel.LastSIPMessageSent);

                SIPResponse challenge = SIPResponse.GetResponse(
                    initialRegister,
                    SIPResponseStatusCodesEnum.Unauthorised,
                    "Authentication Required");
                SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader(
                    SIPAuthorisationHeadersEnum.WWWAuthenticate,
                    registrarHost,
                    "nonce123");
                authHeader.SIPDigest = null;
                authHeader.Value = $"Digest realm=\"{registrarHost}\",nonce=\"nonce123\",algorithm=SHA-256";
                challenge.Header.AuthenticationHeaders.Add(authHeader);

                channel.SIPMessageSent.Reset();
                channel.FireMessageReceived(
                    new SIPEndPoint(SIPProtocolsEnum.udp, IPAddress.Any, 0),
                    new SIPEndPoint(SIPProtocolsEnum.udp, IPAddress.Parse(registrarHost), SIPConstants.DEFAULT_SIP_PORT),
                    challenge.GetBytes());

                Assert.True(channel.SIPMessageSent.WaitOne(5000));
                SIPRequest authenticatedRegister = SIPRequest.ParseSIPRequest(channel.LastSIPMessageSent);
                SIPAuthorisationDigest digest = authenticatedRegister.Header.AuthenticationHeaders[0].SIPDigest;

                Assert.Equal(DigestAlgorithmsEnum.SHA256, digest.DigestAlgorithm);
                Assert.Equal(username, digest.Username);
                Assert.Equal(registrarHost, digest.Realm);

                string expectedHA1 = HTTPDigest.DigestCalcHA1(
                    username,
                    registrarHost,
                    password,
                    DigestAlgorithmsEnum.SHA256);
                string expectedResponse = HTTPDigest.DigestCalcResponse(
                    expectedHA1,
                    digest.URI,
                    digest.Nonce,
                    digest.NonceCount == 0 ? null : digest.NonceCount.ToString().PadLeft(8, '0'),
                    digest.Cnonce,
                    digest.Qop,
                    SIPMethodsEnum.REGISTER.ToString(),
                    DigestAlgorithmsEnum.SHA256);
                Assert.Equal(expectedResponse, digest.Response);
            }
            finally
            {
                userAgent.Stop();
            }
        }
    }
}
