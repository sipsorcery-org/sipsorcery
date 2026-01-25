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

        [Fact]
        public void ParseCryptoSIPMessage()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpDescription =
                """
                v=0
                o=- 3784977145 3784977145 IN IP4 10.2.19.102
                s=pjmedia
                b=AS:84
                t=0 0
                a=X-nat:0
                m=audio 4000 RTP/AVP 8 0 101
                c=IN IP4 10.2.19.102
                b=TIAS:64000
                a=rtcp:4001 IN IP4 10.2.19.102
                a=sendrecv
                a=rtpmap:8 PCMA/8000
                a=rtpmap:0 PCMU/8000
                a=rtpmap:101 telephone-event/8000
                a=fmtp:101 0-16
                a=ssrc:370289018 cname:089912e5446c1847
                a=crypto:1 AES_256_CM_HMAC_SHA1_80 inline:i/aQZXuTQXF8NcIPG/8ClKLXjzJZiZkFqNerJJaWtX9ShjuamMQgFocXUEkWCQ==
                a=crypto:2 AES_256_CM_HMAC_SHA1_32 inline:WEXYOzOomH16+KpVRc8RKHkGUEW6DdvYHWSFKePVy9RzC5DB2Ciw+4t9huV8KA==
                a=crypto:3 AES_CM_128_HMAC_SHA1_80 inline:6wGxadTFLGO9iKPSC8XfRQsOFDRFgJdmpBfdWp9r
                a=crypto:4 AES_CM_128_HMAC_SHA1_32 inline:SdihJallj5frjwWc5yeXbMZlJSLlS+o2bkH3Jsle
                """
            ;

            SDP sdp = SDP.ParseSDPDescription(sdpDescription);
            Assert.NotNull(sdp);
            //Assert.Equal("10.2.19.102", sdp.Connection.ConnectionAddress);
            Assert.Equal("-", sdp.Username);
            Assert.Equal("pjmedia", sdp.SessionName);
            Assert.Equal(SDPMediaTypesEnum.audio, sdp.Media[0].Media);
            Assert.Equal(MediaStreamStatusEnum.SendRecv, sdp.Media[0].MediaStreamStatus);

            Assert.NotEmpty(sdp.Media[0].SecurityDescriptions);
            Assert.Equal("10.2.19.102", sdp.Media[0].Connection.ConnectionAddress);
            Assert.Equal(SDPSecurityDescription.CryptoSuites.AES_256_CM_HMAC_SHA1_80, sdp.Media[0].SecurityDescriptions[0].CryptoSuite);
            Assert.NotEmpty(sdp.Media[0].SecurityDescriptions[0].KeyParams);
            Assert.Null(sdp.Media[0].SecurityDescriptions[0].SessionParam);
            Assert.Equal(SDPSecurityDescription.CryptoSuites.AES_256_CM_HMAC_SHA1_32, sdp.Media[0].SecurityDescriptions[1].CryptoSuite);
            Assert.Equal(2u, sdp.Media[0].SecurityDescriptions[1].Tag);
            Assert.Equal(SDPSecurityDescription.CryptoSuites.AES_CM_128_HMAC_SHA1_80, sdp.Media[0].SecurityDescriptions[2].CryptoSuite);
            Assert.Equal("6wGxadTFLGO9iKPSC8XfRQsOFDRFgJdmpBfdWp9r", sdp.Media[0].SecurityDescriptions[2].KeyParams[0].KeySaltBase64);
            Assert.Equal(SDPSecurityDescription.CryptoSuites.AES_CM_128_HMAC_SHA1_32, sdp.Media[0].SecurityDescriptions[3].CryptoSuite);
            Assert.Equal("SdihJallj5frjwWc5yeXbMZlJSLlS+o2bkH3Jsle", sdp.Media[0].SecurityDescriptions[3].KeyParams[0].KeySaltBase64);
        }
    }
}
