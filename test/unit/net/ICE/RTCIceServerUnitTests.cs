using System;
using SIPSorcery.Net;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class RTCIceServerUnitTests
    {
        [Theory]
        [InlineData("stun:server.com", "stun:server.com", null, null)]
        [InlineData("turn:server.com;user1", "turn:server.com", "user1", null)]
        [InlineData("turn:server.com;user1;pass1", "turn:server.com", "user1", "pass1")]
        [InlineData("", "", null, null)]
        [InlineData("stun:server.com;", "stun:server.com", "", null)]
        [InlineData(";user1;pass1", "", "user1", "pass1")]
        public void ParseUnitTest(string input, string expectedUrl, string expectedUsername, string expectedCredential)
        {
            var iceServer = input.AsSpan();
            var result = RTCIceServer.Parse(iceServer);
            Assert.Equal(expectedUrl, result.urls);
            Assert.Equal(expectedUsername, result.username);
            Assert.Equal(expectedCredential, result.credential);
        }
    }
}
