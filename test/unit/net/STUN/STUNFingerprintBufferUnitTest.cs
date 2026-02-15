//-----------------------------------------------------------------------------
// Filename: STUNFingerprintBufferUnitTest.cs
//
// Description: Unit tests for ParseSTUNMessage fingerprint validation with
// oversized buffers.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class STUNFingerprintBufferUnitTest
    {
        /// <summary>
        /// Verifies that fingerprint validation works when the buffer is exactly
        /// the size of the STUN message (baseline).
        /// </summary>
        [Fact]
        public void FingerprintValidWithExactBuffer()
        {
            string key = "SKYKPPYLTZOAVCLTGHDUODANRKSPOVQVKXJULOGG";

            var msg = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            msg.Header.TransactionId = Encoding.ASCII.GetBytes("abcdefghijkl");
            msg.AddUsernameAttribute("xxxx:yyyy");
            msg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Priority, BitConverter.GetBytes(1U)));

            var exact = msg.ToByteBufferStringKey(key, true);
            var parsed = STUNMessage.ParseSTUNMessage(exact, exact.Length);

            Assert.True(parsed.isFingerprintValid);
            Assert.True(parsed.CheckIntegrity(Encoding.UTF8.GetBytes(key)));
        }

        /// <summary>
        /// Verifies that fingerprint validation works when the message is in an
        /// oversized buffer (e.g., a pooled UDP receive buffer). Previously,
        /// ParseSTUNMessage used buffer.Length instead of bufferLength for the
        /// CRC computation, causing fingerprint validation to fail.
        /// </summary>
        [Fact]
        public void FingerprintValidWithOversizedBuffer()
        {
            string key = "SKYKPPYLTZOAVCLTGHDUODANRKSPOVQVKXJULOGG";

            var msg = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            msg.Header.TransactionId = Encoding.ASCII.GetBytes("abcdefghijkl");
            msg.AddUsernameAttribute("xxxx:yyyy");
            msg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Priority, BitConverter.GetBytes(1U)));

            var exact = msg.ToByteBufferStringKey(key, true);

            // Simulate a pooled receive buffer: copy message into a larger array
            // with trailing garbage bytes.
            var oversized = new byte[exact.Length + 200];
            Buffer.BlockCopy(exact, 0, oversized, 0, exact.Length);
            //new Random(42).NextBytes(oversized.AsSpan(exact.Length).ToArray()
            //    .CopyTo(oversized.AsSpan(exact.Length)));

            var parsed = STUNMessage.ParseSTUNMessage(oversized, exact.Length);

            Assert.True(parsed.isFingerprintValid,
                "Fingerprint should be valid even with oversized buffer");
            Assert.True(parsed.CheckIntegrity(Encoding.UTF8.GetBytes(key)),
                "Integrity should be valid even with oversized buffer");
        }
    }
}
