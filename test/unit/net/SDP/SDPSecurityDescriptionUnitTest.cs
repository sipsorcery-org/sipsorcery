using System;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    /// <summary>This class contains parameterized unit tests for SDPSecurityDescription</summary>
    [Trait("Category", "unit")]
    public partial class SDPSecurityDescriptionUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SDPSecurityDescriptionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void ParseTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            SDPSecurityDescription c1 = SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80  inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:4    FEC_ORDER=FEC_SRTP");
            Assert.Equal("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:4 FEC_ORDER=FEC_SRTP", c1.ToString());
            Assert.Equal(SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:4 FEC_ORDER=FEC_SRTP").ToString(), c1.ToString());
            Assert.Equal(SDPSecurityDescription.Parse(c1.ToString()).ToString(), c1.ToString());
            Assert.Equal(1u, c1.Tag);
            Assert.Equal(4u, c1.KeyParams[0].MkiLength);
            Assert.Equal(1u, c1.KeyParams[0].MkiValue);
            Assert.Equal(Math.Pow(2, 20), c1.KeyParams[0].LifeTime);
            Assert.Equal("2^20", c1.KeyParams[0].LifeTimeString);
            Assert.Equal("WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz", c1.KeyParams[0].KeySaltBase64);
            Assert.Equal("FEC_ORDER=FEC_SRTP", c1.SessionParam.ToString());

            SDPSecurityDescription c2 = SDPSecurityDescription.Parse("a=crypto:2 F8_128_HMAC_SHA1_80  inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^20|1:4;inline:QUJjZGVmMTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5|2^20|2:4     FEC_ORDER=FEC_SRTP");
            Assert.Equal("a=crypto:2 F8_128_HMAC_SHA1_80 inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^20|1:4;inline:QUJjZGVmMTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5|2^20|2:4 FEC_ORDER=FEC_SRTP", c2.ToString());
            Assert.Equal(2, c2.KeyParams.Count);
            Assert.Equal(2u, c2.Tag);
            Assert.Equal(4u, c2.KeyParams[0].MkiLength);
            Assert.Equal(1u, c2.KeyParams[0].MkiValue);
            Assert.Equal(4u, c2.KeyParams[1].MkiLength);
            Assert.Equal(2u, c2.KeyParams[1].MkiValue);
            Assert.Equal((ulong)Math.Pow(2, 20), c2.KeyParams[0].LifeTime);
            Assert.Equal("2^20", c2.KeyParams[0].LifeTimeString);
            Assert.Equal((ulong)Math.Pow(2, 20), c2.KeyParams[1].LifeTime);
            Assert.Equal("2^20", c2.KeyParams[1].LifeTimeString);
            Assert.Equal("MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm", c2.KeyParams[0].KeySaltBase64);
            Assert.Equal("QUJjZGVmMTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5", c2.KeyParams[1].KeySaltBase64);
            Assert.Equal("FEC_ORDER=FEC_SRTP", c2.SessionParam.ToString());

            SDPSecurityDescription c3 = SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80  inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|2^20|1:4");
            Assert.Equal("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|2^20|1:4", c3.ToString());
            Assert.Equal(SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80  inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|2^20|1:4").ToString(), c3.ToString());
            Assert.Equal(SDPSecurityDescription.Parse(c3.ToString()).ToString(), c3.ToString());
            Assert.Equal(1u, c3.Tag);
            Assert.Equal(4u, c3.KeyParams[0].MkiLength);
            Assert.Equal(1u, c3.KeyParams[0].MkiValue);
            Assert.Equal((ulong)Math.Pow(2, 20), c3.KeyParams[0].LifeTime);
            Assert.Equal("2^20", c3.KeyParams[0].LifeTimeString);
            Assert.Equal("PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR", c3.KeyParams[0].KeySaltBase64);
            Assert.Null(c3.SessionParam);

            // Simple inline key without optional parameters (RFC 4568 basic example)
            SDPSecurityDescription c4 = SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR");
            Assert.Equal("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR", c4.ToString());
            Assert.Equal(1u, c4.Tag);
            Assert.Equal(0u, c4.KeyParams[0].MkiLength);
            Assert.Equal(0u, c4.KeyParams[0].MkiValue);
            Assert.Equal(0ul, c4.KeyParams[0].LifeTime);
            Assert.Null(c4.KeyParams[0].LifeTimeString);
            Assert.Null(c4.SessionParam);

            // Lifetime without MKI
            SDPSecurityDescription c5 = SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|2^20");
            Assert.Equal("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|2^20", c5.ToString());
            Assert.Equal((ulong)Math.Pow(2, 20), c5.KeyParams[0].LifeTime);
            Assert.Equal("2^20", c5.KeyParams[0].LifeTimeString);
            Assert.Equal(0u, c5.KeyParams[0].MkiLength);

            // MKI without lifetime
            SDPSecurityDescription c6 = SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|1:32");
            Assert.Equal("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|1:32", c6.ToString());
            Assert.Equal(32u, c6.KeyParams[0].MkiLength);
            Assert.Equal(1u, c6.KeyParams[0].MkiValue);
            Assert.Equal(0ul, c6.KeyParams[0].LifeTime);

            // AES_256_CM_HMAC_SHA1_80 crypto suite
            SDPSecurityDescription c7 = SDPSecurityDescription.Parse("a=crypto:1 AES_256_CM_HMAC_SHA1_80 inline:d0RmdmcmVCspeEc3QGZiNWpVLFJhQX1cfHAwJSoj/Jl+xZQ4qrEUAzDN67dOAQ==");
            Assert.Equal(SDPSecurityDescription.CryptoSuites.AES_256_CM_HMAC_SHA1_80, c7.CryptoSuite);
            Assert.NotNull(c7.KeyParams[0].Key);
            Assert.NotNull(c7.KeyParams[0].Salt);

            // AEAD_AES_256_GCM crypto suite (different salt offset) - needs 32 byte key + 12 byte salt = 44 bytes
            SDPSecurityDescription c8 = SDPSecurityDescription.Parse("a=crypto:1 AEAD_AES_256_GCM inline:d0RmdmcmVCspeEc3QGZiNWpVLFJhQX1cfHAwJSoj/Jl+xZQ4qrEUAzDN67dOAUm8tQ==");
            Assert.Equal(SDPSecurityDescription.CryptoSuites.AEAD_AES_256_GCM, c8.CryptoSuite);
            Assert.NotNull(c8.KeyParams[0].Key);
            Assert.Equal(32, c8.KeyParams[0].Key.Length);
            Assert.NotNull(c8.KeyParams[0].Salt);
            Assert.Equal(12, c8.KeyParams[0].Salt.Length);

            // Numeric lifetime format (instead of 2^n)
            SDPSecurityDescription c9 = SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|1048576");
            Assert.Equal((ulong)Math.Pow(2, 20), c9.KeyParams[0].LifeTime);

            // Different session parameters - KDR
            SDPSecurityDescription c10 = SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR KDR=10");
            Assert.NotNull(c10.SessionParam);
            Assert.Equal(SDPSecurityDescription.SessionParameter.SrtpSessionParams.kdr, c10.SessionParam.SrtpSessionParam);
            Assert.Equal(10ul, c10.SessionParam.Kdr);

            // Different session parameters - WSH
            SDPSecurityDescription c11 = SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR WSH=128");
            Assert.NotNull(c11.SessionParam);
            Assert.Equal(SDPSecurityDescription.SessionParameter.SrtpSessionParams.wsh, c11.SessionParam.SrtpSessionParam);
            Assert.Equal(128ul, c11.SessionParam.Wsh);

            // Different session parameters - UNENCRYPTED_SRTP
            SDPSecurityDescription c12 = SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR UNENCRYPTED_SRTP");
            Assert.NotNull(c12.SessionParam);
            Assert.Equal(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNENCRYPTED_SRTP, c12.SessionParam.SrtpSessionParam);

            // Different session parameters - UNENCRYPTED_SRTCP
            SDPSecurityDescription c13 = SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR UNENCRYPTED_SRTCP");
            Assert.NotNull(c13.SessionParam);
            Assert.Equal(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNENCRYPTED_SRTCP, c13.SessionParam.SrtpSessionParam);

            // Different session parameters - UNAUTHENTICATED_SRTP
            SDPSecurityDescription c14 = SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR UNAUTHENTICATED_SRTP");
            Assert.NotNull(c14.SessionParam);
            Assert.Equal(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNAUTHENTICATED_SRTP, c14.SessionParam.SrtpSessionParam);

            // High tag value (edge case)
            SDPSecurityDescription c15 = SDPSecurityDescription.Parse("a=crypto:999999999 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR");
            Assert.Equal(999999999u, c15.Tag);

            // Different lifetime exponents
            SDPSecurityDescription c16 = SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|2^48");
            Assert.Equal((ulong)Math.Pow(2, 48), c16.KeyParams[0].LifeTime);
            Assert.Equal("2^48", c16.KeyParams[0].LifeTimeString);

            // AES_192_CM_HMAC_SHA1_80 crypto suite
            SDPSecurityDescription c17 = SDPSecurityDescription.Parse("a=crypto:1 AES_192_CM_HMAC_SHA1_80 inline:0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123");
            Assert.Equal(SDPSecurityDescription.CryptoSuites.AES_192_CM_HMAC_SHA1_80, c17.CryptoSuite);

            Assert.Null(SDPSecurityDescription.Parse(null));
            Assert.Null(SDPSecurityDescription.Parse(""));

            Assert.Throws<FormatException>(() => SDPSecurityDescription.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^20|1:4;inline:QUJjZGVmMTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5|2^20|2:4"));
            Assert.Throws<FormatException>(() => SDPSecurityDescription.Parse("a=crypto: AES_CM_128_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|2^20|1:4"));
            Assert.Throws<FormatException>(() => SDPSecurityDescription.Parse("a=crypto:1  inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|2^20|1:4"));
            Assert.Throws<FormatException>(() => SDPSecurityDescription.Parse("a=crypto:1 AES_CM_128_HMAC_SHA1_80 "));
            Assert.Throws<FormatException>(() => SDPSecurityDescription.Parse("a=crypto:1 AES_CM_HMAC_SHA1_80 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|2^20|1:4"));
            Assert.Throws<FormatException>(() => SDPSecurityDescription.Parse("a=crypto:1 1 inline:PS1uQCVeeCFCanVmcjkpPywjNWhcYD0mXXtxaVBR|2^20|1:4"));
        }
    }
}
