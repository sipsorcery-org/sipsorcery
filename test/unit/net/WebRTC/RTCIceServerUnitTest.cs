//-----------------------------------------------------------------------------
// Filename: RTCIceServerUnitTest.cs
//
// Description: Unit tests for the RTCIceServer class.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCIceServerUnitTest
    {
        [Theory]
        [InlineData("stun:stun.example.com", null, null)]
        [InlineData("turn:turn.example.com;user", "user", null)]
        [InlineData("turn:turn.example.com;user;pass", "user", "pass")]
        public void ParseUnitTest(string value, string expectedUsername, string expectedCredential)
        {
            var server = RTCIceServer.Parse(value);

            Assert.Equal(value.Split(';')[0], server.urls);
            Assert.Equal(expectedUsername, server.username);
            Assert.Equal(expectedCredential, server.credential);
            Assert.Equal(RTCIceCredentialType.password, server.credentialType);
        }
    }
}
