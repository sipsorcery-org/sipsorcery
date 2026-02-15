//-----------------------------------------------------------------------------
// Filename: STUNErrorCodeAttributeUnitTest.cs
//
// Description: Unit tests for the STUNErrorCodeAttribute class.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class STUNErrorCodeAttributeUnitTest
    {
        /// <summary>
        /// Tests that the (int, string) constructor correctly sets ErrorClass and ErrorNumber.
        /// Previously, a bug caused ErrorClass to always be 0 because the constructor
        /// read from the ErrorCode property (which depends on ErrorClass) instead of
        /// the errorCode parameter.
        /// </summary>
        [Theory]
        [InlineData(401, "Unauthorized", 4, 1)]
        [InlineData(438, "Stale Nonce", 4, 38)]
        [InlineData(500, "Server Error", 5, 0)]
        [InlineData(300, "Try Alternate", 3, 0)]
        [InlineData(699, "Max Error", 6, 99)]
        public void IntStringConstructorSetsErrorCodeCorrectly(int errorCode, string reason, byte expectedClass, byte expectedNumber)
        {
            var attr = new STUNErrorCodeAttribute(errorCode, reason);

            Assert.Equal(expectedClass, attr.ErrorClass);
            Assert.Equal(expectedNumber, attr.ErrorNumber);
            Assert.Equal(errorCode, attr.ErrorCode);
            Assert.Equal(reason, attr.ReasonPhrase);
        }

        /// <summary>
        /// Tests that the (int, string) constructor populates the base Value field.
        /// Previously, null was passed to the base constructor, causing PaddedLength
        /// to return 0 and breaking STUN message serialization.
        /// </summary>
        [Fact]
        public void IntStringConstructorPopulatesValueField()
        {
            var attr = new STUNErrorCodeAttribute(401, "Unauthorized");

            Assert.NotNull(attr.Value);

            // Value should be: 2 reserved bytes + 1 class + 1 number + reason phrase
            var expectedLength = 4 + Encoding.UTF8.GetByteCount("Unauthorized");
            Assert.Equal(expectedLength, attr.Value.Length);

            // PaddedLength must be non-zero for correct message buffer allocation
            Assert.True(attr.PaddedLength > 0);
        }

        /// <summary>
        /// Tests that an error response built with the (int, string) constructor can be
        /// serialized into a complete STUN message without throwing.
        /// </summary>
        [Fact]
        public void ErrorResponseSerializesToByteBuffer()
        {
            var msg = new STUNMessage(STUNMessageTypesEnum.AllocateErrorResponse);
            msg.Header.TransactionId = new byte[12];
            msg.Attributes.Add(new STUNErrorCodeAttribute(401, "Unauthorized"));

            // This previously threw ArgumentException due to null Value / zero PaddedLength
            byte[] buffer = msg.ToByteBuffer(null, false);

            Assert.NotNull(buffer);
            Assert.True(buffer.Length > 20); // At least STUN header + error attribute
        }

        /// <summary>
        /// Tests round-trip: construct with (int, string), serialize to a STUN message,
        /// parse it back, and verify the error code is preserved.
        /// </summary>
        [Fact]
        public void ErrorAttributeRoundTrips()
        {
            var msg = new STUNMessage(STUNMessageTypesEnum.AllocateErrorResponse);
            msg.Header.TransactionId = new byte[12];
            msg.Attributes.Add(new STUNErrorCodeAttribute(437, "Allocation Mismatch"));

            byte[] buffer = msg.ToByteBuffer(null, false);
            var parsed = STUNMessage.ParseSTUNMessage(buffer, buffer.Length);

            Assert.NotNull(parsed);
            Assert.Single(parsed.Attributes);
            Assert.Equal(STUNAttributeTypesEnum.ErrorCode, parsed.Attributes[0].AttributeType);

            var errorAttr = new STUNErrorCodeAttribute(parsed.Attributes[0].Value);
            Assert.Equal(437, errorAttr.ErrorCode);
            Assert.Equal(4, errorAttr.ErrorClass);
            Assert.Equal(37, errorAttr.ErrorNumber);
            Assert.Equal("Allocation Mismatch", errorAttr.ReasonPhrase);
        }
    }
}
