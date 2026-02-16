//-----------------------------------------------------------------------------
// Filename: IceServerUnitTest.cs
//
// Description: Unit tests for the IceServer class, specifically the ParseIceServer method.
//
// History:
// 2024    Created for comprehensive ParseIceServer coverage using Theory tests.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class IceServerUnitTest
    {
        private readonly ILogger logger;

        public IceServerUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        private string GetString(ReadOnlyMemory<byte> memory) => System.Text.Encoding.UTF8.GetString(memory.ToArray());

        /// <summary>
        /// Tests parsing STUN/TURN server URLs with various schemes and ports.
        /// </summary>
        [Theory]
        [InlineData("stun:stun.example.com:3478", STUNSchemesEnum.stun, "stun.example.com", 3478, false, false)]
        [InlineData("stun:stun.example.com", STUNSchemesEnum.stun, "stun.example.com", 3478, false, false)]
        [InlineData("stun.example.com:3478", STUNSchemesEnum.stun, "stun.example.com", 3478, false, false)]
        [InlineData("turn:turn.example.com:3478", STUNSchemesEnum.turn, "turn.example.com", 3478, false, false)]
        [InlineData("turn:turn.example.com?transport=tcp", STUNSchemesEnum.turn, "turn.example.com", 3478, false, false)]
        [InlineData("stun:[::1]:3478", STUNSchemesEnum.stun, "[::1]", 3478, false, false)]
        [InlineData("stuns:stuns.example.com:5349", STUNSchemesEnum.stuns, "stuns.example.com", 5349, false, false)]
        [InlineData("stuns:stuns.example.com", STUNSchemesEnum.stuns, "stuns.example.com", 5349, false, false)]
        [InlineData("turns:turns.example.com:5349", STUNSchemesEnum.turns, "turns.example.com", 5349, false, false)]
        [InlineData("turns:turns.example.com?transport=tcp", STUNSchemesEnum.turns, "turns.example.com", 5349, false, false)]
        [InlineData("stuns:[::1]:5349", STUNSchemesEnum.stuns, "[::1]", 5349, false, false)]
        [InlineData("turns:[::1]:5349", STUNSchemesEnum.turns, "[::1]", 5349, false, false)]
        public void ParseIceServerUriUnitTest(string input, STUNSchemesEnum expectedScheme, string expectedHost, int expectedPort, bool hasUsername, bool hasPassword)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var iceServer = IceServer.ParseIceServer(input.AsSpan());

            Assert.NotNull(iceServer);
            Assert.NotNull(iceServer.Uri);
            Assert.Equal(expectedScheme, iceServer.Uri.Scheme);
            Assert.Equal(expectedHost, iceServer.Uri.Host);
            Assert.Equal(expectedPort, iceServer.Uri.Port);

            if (hasUsername)
                Assert.False(iceServer.Username.IsEmpty);
            else
                Assert.True(iceServer.Username.IsEmpty);

            if (hasPassword)
                Assert.False(iceServer.Password.IsEmpty);
            else
                Assert.True(iceServer.Password.IsEmpty);
        }

        /// <summary>
        /// Tests parsing ICE servers with various credential configurations.
        /// </summary>
        [Theory]
        [InlineData("stun:stun.example.com:3478;user1", "user1", null)]
        [InlineData("stun:stun.example.com:3478;user1;pass1", "user1", "pass1")]
        [InlineData("turn:turn.example.com:3478?transport=tcp;user1;pass1", "user1", "pass1")]
        [InlineData("stun:stun.example.com;\"user1\";\"pass1\"", "user1", "pass1")]
        [InlineData("stun:stun.example.com;'user1';'pass1'", "user1", "pass1")]
        [InlineData("stun:stun.example.com;user1;p@ssw0rd!#$%", "user1", "p@ssw0rd!#$%")]
        [InlineData("stuns:stuns.example.com:5349;user1;pass1", "user1", "pass1")]
        [InlineData("turns:turns.example.com:5349;user1;pass1", "user1", "pass1")]
        [InlineData("stuns:stuns.example.com;\"user1\";\"pass1\"", "user1", "pass1")]
        [InlineData("turns:turns.example.com?transport=tcp;user1;pass1", "user1", "pass1")]
        public void ParseIceServerWithCredentialsUnitTest(string input, string expectedUsername, string expectedPassword)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var iceServer = IceServer.ParseIceServer(input.AsSpan());

            Assert.NotNull(iceServer);
            Assert.Equal(expectedUsername, GetString(iceServer.Username));
            if (expectedPassword != null)
                Assert.Equal(expectedPassword, GetString(iceServer.Password));
            else
                Assert.True(iceServer.Password.IsEmpty);
        }

        /// <summary>
        /// Tests parsing ICE servers with multiple URLs (should use first non-empty).
        /// </summary>
        [Theory]
        [InlineData("stun:stun1.example.com:3478,stun:stun2.example.com:3478", "stun1.example.com")]
        [InlineData("stun:stun1.example.com:3478 stun:stun2.example.com:3478", "stun1.example.com")]
        [InlineData("stun:stun1.example.com , , stun:stun2.example.com", "stun1.example.com")]
        [InlineData("stun:stun1.example.com stun:stun2.example.com stun:stun3.example.com", "stun1.example.com")]
        public void ParseIceServerMultipleUrlsUnitTest(string input, string expectedHost)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var iceServer = IceServer.ParseIceServer(input.AsSpan());

            Assert.NotNull(iceServer);
            Assert.NotNull(iceServer.Uri);
            Assert.Equal(expectedHost, iceServer.Uri.Host);
        }

        /// <summary>
        /// Tests parsing ICE servers with various whitespace patterns.
        /// </summary>
        [Theory]
        [InlineData("  stun:stun.example.com:3478  ", "stun.example.com")]
        [InlineData("stun:stun.example.com:3478 ; user1 ; pass1 ", "stun.example.com")]
        [InlineData("stun:stun.example.com:3478   ;user1;pass1", "stun.example.com")]
        public void ParseIceServerWithWhitespaceUnitTest(string input, string expectedHost)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var iceServer = IceServer.ParseIceServer(input.AsSpan());

            Assert.NotNull(iceServer);
            Assert.NotNull(iceServer.Uri);
            Assert.Equal(expectedHost, iceServer.Uri.Host);
        }

        /// <summary>
        /// Tests that invalid input throws ArgumentException with appropriate messages.
        /// </summary>
        [Theory]
        [InlineData("", "cannot be empty")]
        [InlineData("   ", "cannot be empty")]
        [InlineData(", , ", "Expected a STUN/TURN URI")]
        [InlineData("invalid://url", "Invalid ICE server URL")]
        [InlineData(", , , , ,", "Expected a STUN/TURN URI")]
        public void ParseIceServerInvalidInputThrowsUnitTest(string input, string expectedMessage)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var ex = Assert.Throws<ArgumentException>(() => IceServer.ParseIceServer(input.AsSpan()));
            Assert.Contains(expectedMessage, ex.Message);
        }

        /// <summary>
        /// Tests parsing ICE servers with null/empty credentials.
        /// </summary>
        [Theory]
        [InlineData("stun:stun.example.com;;pass1", true, false)]
        [InlineData("stun:stun.example.com;user1;", false, true)]
        [InlineData("stun:stun.example.com;  ;  ", true, true)]
        public void ParseIceServerWithEmptyCredentialsUnitTest(string input, bool usernameEmpty, bool passwordEmpty)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var iceServer = IceServer.ParseIceServer(input.AsSpan());

            Assert.NotNull(iceServer);

            if (usernameEmpty)
                Assert.True(iceServer.Username.IsEmpty);
            else
                Assert.False(iceServer.Username.IsEmpty);

            if (passwordEmpty)
                Assert.True(iceServer.Password.IsEmpty);
            else
                Assert.False(iceServer.Password.IsEmpty);
        }

        /// <summary>
        /// Tests parsing complex real-world TURN and STUN server configurations.
        /// </summary>
        [Theory]
        [InlineData("turn:turnserver.example.com:3478?transport=udp;username;credential",
      STUNSchemesEnum.turn, "turnserver.example.com", "username", "credential")]
        [InlineData("stun:stun.example.com:3478;testuser;testpass",
   STUNSchemesEnum.stun, "stun.example.com", "testuser", "testpass")]
        [InlineData("\"stun:stun.example.com:3478\";user1;pass1",
        STUNSchemesEnum.stun, "stun.example.com", "user1", "pass1")]
        public void ParseComplexIceServerUnitTest(string input, STUNSchemesEnum expectedScheme, string expectedHost, string expectedUsername, string expectedPassword)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var iceServer = IceServer.ParseIceServer(input.AsSpan());

            Assert.NotNull(iceServer);
            Assert.NotNull(iceServer.Uri);
            Assert.Equal(expectedScheme, iceServer.Uri.Scheme);
            Assert.Equal(expectedHost, iceServer.Uri.Host);
            Assert.Equal(expectedUsername, GetString(iceServer.Username));
            Assert.Equal(expectedPassword, GetString(iceServer.Password));
        }

        /// <summary>
        /// Tests various field extraction scenarios.
        /// </summary>
        [Theory]
        [InlineData("stun:stun.example.com;user1;pass1;extra", "user1", "pass1")]
        [InlineData("stun:stun.example.com:3478", "", "")]
        [InlineData("stun:stun.example.com:3478 ; user1 ; pass1 ", "user1", "pass1")]
        public void ParseIceServerFieldExtractionUnitTest(string input, string expectedUsername, string expectedPassword)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var iceServer = IceServer.ParseIceServer(input.AsSpan());

            Assert.NotNull(iceServer);
            if (!string.IsNullOrEmpty(expectedUsername))
                Assert.Equal(expectedUsername, GetString(iceServer.Username));
            else
                Assert.True(iceServer.Username.IsEmpty);

            if (!string.IsNullOrEmpty(expectedPassword))
                Assert.Equal(expectedPassword, GetString(iceServer.Password));
            else
                Assert.True(iceServer.Password.IsEmpty);
        }

        /// <summary>
        /// Tests that parsed IceServer instances are valid and properly configured.
        /// </summary>
        [Theory]
        [InlineData("stun:stun.example.com:3478")]
        [InlineData("turn:turn.example.com:3478")]
        [InlineData("stun:stun.example.com:3478;user1;pass1")]
        public void ParsedIceServerIsValidInstanceUnitTest(string input)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var iceServer = IceServer.ParseIceServer(input.AsSpan());

            Assert.NotNull(iceServer);
            Assert.IsType<IceServer>(iceServer);
            Assert.NotNull(iceServer.Uri);
            Assert.Equal(0, iceServer.Id);
            Assert.NotNull(iceServer.TransactionID);
            Assert.False(string.IsNullOrEmpty(iceServer.TransactionID));
        }

        /// <summary>
        /// Tests that credentials are properly stored as UTF-8 bytes.
        /// </summary>
        [Theory]
        [InlineData("stun:stun.example.com;testuser;testpass")]
        [InlineData("stun:stun.example.com;user_üñíçödé;passörd")]
        [InlineData("stun:stun.example.com;user;p@ssw0rd!#$%&")]
        public void CredentialsStoredAsUtf8BytesUnitTest(string input)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var iceServer = IceServer.ParseIceServer(input.AsSpan());

            Assert.NotNull(iceServer);

            // Verify they're stored as bytes
            Assert.IsType<ReadOnlyMemory<byte>>(iceServer.Username);
            Assert.IsType<ReadOnlyMemory<byte>>(iceServer.Password);

            // Verify they can be converted back to strings
            var username = GetString(iceServer.Username);
            var password = GetString(iceServer.Password);

            Assert.False(string.IsNullOrEmpty(username));
            Assert.False(string.IsNullOrEmpty(password));
        }

        /// <summary>
        /// Tests URL auto-prefixing and scheme handling.
        /// </summary>
        [Theory]
        [InlineData("example.com:3478", STUNSchemesEnum.stun, "example.com")]
        [InlineData("stun:stun.example.com:3478", STUNSchemesEnum.stun, "stun.example.com")]
        [InlineData("turn:turn.example.com:3478", STUNSchemesEnum.turn, "turn.example.com")]
        public void ParseUrlSchemePrefixingUnitTest(string input, STUNSchemesEnum expectedScheme, string expectedHost)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var iceServer = IceServer.ParseIceServer(input.AsSpan());

            Assert.NotNull(iceServer);
            Assert.NotNull(iceServer.Uri);
            Assert.Equal(expectedScheme, iceServer.Uri.Scheme);
            Assert.Equal(expectedHost, iceServer.Uri.Host);
        }

        /// <summary>
        /// Tests STUNS (STUN over TLS) and TURNS (TURN over TLS) URI parsing with various configurations.
        /// </summary>
        [Theory]
        [InlineData("stuns:stuns.example.com:5349", STUNSchemesEnum.stuns, "stuns.example.com", 5349)]
        [InlineData("stuns:stuns.example.com", STUNSchemesEnum.stuns, "stuns.example.com", 5349)]
        [InlineData("turns:turns.example.com:5349", STUNSchemesEnum.turns, "turns.example.com", 5349)]
        [InlineData("turns:turns.example.com", STUNSchemesEnum.turns, "turns.example.com", 5349)]
        [InlineData("stuns:[::1]:5349", STUNSchemesEnum.stuns, "[::1]", 5349)]
        [InlineData("turns:[::1]:5349", STUNSchemesEnum.turns, "[::1]", 5349)]
        [InlineData("turns:turns.example.com?transport=tcp", STUNSchemesEnum.turns, "turns.example.com", 5349)]
        [InlineData("turns:turns.example.com?transport=tls", STUNSchemesEnum.turns, "turns.example.com", 5349)]
        public void ParseTurnsAndStunsUrisUnitTest(string input, STUNSchemesEnum expectedScheme, string expectedHost, int expectedPort)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var iceServer = IceServer.ParseIceServer(input.AsSpan());

            Assert.NotNull(iceServer);
            Assert.NotNull(iceServer.Uri);
            Assert.Equal(expectedScheme, iceServer.Uri.Scheme);
            Assert.Equal(expectedHost, iceServer.Uri.Host);
            Assert.Equal(expectedPort, iceServer.Uri.Port);
            Assert.True(iceServer.Username.IsEmpty);
            Assert.True(iceServer.Password.IsEmpty);
        }

        /// <summary>
        /// Tests STUNS and TURNS URIs with authentication credentials.
        /// </summary>
        [Theory]
        [InlineData("stuns:stuns.example.com:5349;user1;pass1", STUNSchemesEnum.stuns, "stuns.example.com", "user1", "pass1")]
        [InlineData("turns:turns.example.com:5349;user1;pass1", STUNSchemesEnum.turns, "turns.example.com", "user1", "pass1")]
        [InlineData("stuns:stuns.example.com;\"user1\";\"pass1\"", STUNSchemesEnum.stuns, "stuns.example.com", "user1", "pass1")]
        [InlineData("turns:turns.example.com;'user1';'pass1'", STUNSchemesEnum.turns, "turns.example.com", "user1", "pass1")]
        [InlineData("stuns:[::1]:5349;user1;pass1", STUNSchemesEnum.stuns, "[::1]", "user1", "pass1")]
        [InlineData("turns:[::1]:5349;user1;pass1", STUNSchemesEnum.turns, "[::1]", "user1", "pass1")]
        [InlineData("turns:turns.example.com?transport=tcp;tlsuser;tlspass", STUNSchemesEnum.turns, "turns.example.com", "tlsuser", "tlspass")]
        public void ParseTurnsAndStunsWithCredentialsUnitTest(string input, STUNSchemesEnum expectedScheme, string expectedHost, string expectedUsername, string expectedPassword)
        {
            logger.LogDebug("--> {MethodName} with input: {Input}", TestHelper.GetCurrentMethodName(), input);
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var iceServer = IceServer.ParseIceServer(input.AsSpan());

            Assert.NotNull(iceServer);
            Assert.NotNull(iceServer.Uri);
            Assert.Equal(expectedScheme, iceServer.Uri.Scheme);
            Assert.Equal(expectedHost, iceServer.Uri.Host);
            Assert.Equal(expectedUsername, GetString(iceServer.Username));
            Assert.Equal(expectedPassword, GetString(iceServer.Password));
        }
    }
}
