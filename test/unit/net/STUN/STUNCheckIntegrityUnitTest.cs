//-----------------------------------------------------------------------------
// Filename: STUNCheckIntegrityUnitTest.cs
//
// Description: Unit tests for STUNMessage.CheckIntegrity with and without
// FINGERPRINT attribute.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Text;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class STUNCheckIntegrityUnitTest
    {
        /// <summary>
        /// Verifies that CheckIntegrity succeeds for a message signed with
        /// MESSAGE-INTEGRITY but without a FINGERPRINT attribute.
        /// Per RFC 5389 Section 15.5, FINGERPRINT is optional.
        /// </summary>
        [Fact]
        public void CheckIntegrityWithoutFingerprint()
        {
            string key = "testpassword123";

            var msg = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            msg.Header.TransactionId = Encoding.ASCII.GetBytes("abcdefghijkl");
            msg.AddUsernameAttribute("testuser");

            // Serialize WITH integrity but WITHOUT fingerprint
            var buffer = msg.ToByteBufferStringKey(key, false);

            var parsed = STUNMessage.ParseSTUNMessage(buffer, buffer.Length);

            Assert.False(parsed.isFingerprintValid, "No FINGERPRINT was sent");
            Assert.True(parsed.CheckIntegrity(Encoding.UTF8.GetBytes(key)),
                "CheckIntegrity should succeed without FINGERPRINT");
        }

        /// <summary>
        /// Verifies that the existing behavior (CheckIntegrity with FINGERPRINT)
        /// still works correctly after the fix.
        /// </summary>
        [Fact]
        public void CheckIntegrityWithFingerprint()
        {
            string key = "testpassword123";

            var msg = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            msg.Header.TransactionId = Encoding.ASCII.GetBytes("abcdefghijkl");
            msg.AddUsernameAttribute("testuser");

            // Serialize WITH both integrity and fingerprint
            var buffer = msg.ToByteBufferStringKey(key, true);

            var parsed = STUNMessage.ParseSTUNMessage(buffer, buffer.Length);

            Assert.True(parsed.isFingerprintValid);
            Assert.True(parsed.CheckIntegrity(Encoding.UTF8.GetBytes(key)));
        }

        /// <summary>
        /// Verifies that CheckIntegrity fails with the wrong key,
        /// even without FINGERPRINT.
        /// </summary>
        [Fact]
        public void CheckIntegrityFailsWithWrongKey()
        {
            var msg = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            msg.Header.TransactionId = Encoding.ASCII.GetBytes("abcdefghijkl");
            msg.AddUsernameAttribute("testuser");

            var buffer = msg.ToByteBufferStringKey("correctkey", false);
            var parsed = STUNMessage.ParseSTUNMessage(buffer, buffer.Length);

            Assert.False(parsed.CheckIntegrity(Encoding.UTF8.GetBytes("wrongkey")));
        }
    }
}
