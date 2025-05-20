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

            Assert.True(c1.Equals(c1));
            Assert.True(c1.ToString() == c1.ToString());
            Assert.True(c2.Equals(c2));
            Assert.True(c2.ToString() == c2.ToString());
            Assert.True(c3.Equals(c3));
            Assert.True(c3.ToString() == c3.ToString());

            Assert.True(c1.Equals(c2) == (c1.ToString() == c2.ToString()));
            Assert.True(c2.Equals(c3) == (c2.ToString() == c3.ToString()));
            Assert.True(c3.Equals(c1) == (c3.ToString() == c1.ToString()));

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
