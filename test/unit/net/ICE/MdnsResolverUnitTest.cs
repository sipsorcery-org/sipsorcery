//-----------------------------------------------------------------------------
// Filename: MdnsResolverUnitTest.cs
//
// Description: Unit tests for the mDNS query builder and response parser
// in MdnsResolver.
//
// History:
// 24 Oct 2026	Claude	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class MdnsResolverUnitTest
    {
        private const string HOSTNAME = "0b4c6e8a-1d2f-4a3b-9c5d-7e6f8a9b0c1d.local";

        private Microsoft.Extensions.Logging.ILogger logger = null;

        public MdnsResolverUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that the query contains an A and an AAAA question for the hostname.
        /// </summary>
        [Fact]
        public void BuildQueryUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var query = MdnsResolver.BuildQuery(HOSTNAME);

            var name = EncodeName(HOSTNAME);
            var expected = new List<byte> { 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0 };
            expected.AddRange(name);
            expected.AddRange(new byte[] { 0, 1, 0, 1 });
            expected.AddRange(name);
            expected.AddRange(new byte[] { 0, 28, 0, 1 });

            Assert.Equal(expected.ToArray(), query);
        }

        /// <summary>
        /// Tests that a hostname with an empty label is rejected.
        /// </summary>
        [Fact]
        public void BuildQueryInvalidNameUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            Assert.Throws<System.ArgumentException>(() => MdnsResolver.BuildQuery("bad..local"));
        }

        /// <summary>
        /// Tests parsing a typical response with an A record that has the
        /// cache-flush bit set, as sent by Chrome.
        /// </summary>
        [Fact]
        public void ParseARecordUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var msg = new List<byte> { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0 };
            msg.AddRange(EncodeName(HOSTNAME));
            msg.AddRange(new byte[] { 0, 1, 0x80, 1, 0, 0, 0, 120, 0, 4, 192, 168, 0, 50 });

            var results = MdnsResolver.ParseAddressRecords(msg.ToArray(), msg.Count);

            Assert.Single(results);
            Assert.Equal(HOSTNAME, results[0].Key);
            Assert.Equal(IPAddress.Parse("192.168.0.50"), results[0].Value);
        }

        /// <summary>
        /// Tests parsing a response that echoes the question and uses name
        /// compression for an AAAA answer and an A record in the additional section.
        /// </summary>
        [Fact]
        public void ParseCompressedRecordsUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var msg = new List<byte> { 0, 0, 0x84, 0, 0, 1, 0, 1, 0, 0, 0, 1 };
            msg.AddRange(EncodeName(HOSTNAME)); // Question name at offset 12.
            msg.AddRange(new byte[] { 0, 28, 0, 1 });

            var v6 = IPAddress.Parse("fe80::1234:5678:9abc:def0");
            msg.AddRange(new byte[] { 0xC0, 12, 0, 28, 0x80, 1, 0, 0, 0, 120, 0, 16 });
            msg.AddRange(v6.GetAddressBytes());

            msg.AddRange(new byte[] { 0xC0, 12, 0, 1, 0x80, 1, 0, 0, 0, 120, 0, 4, 10, 0, 0, 7 });

            var results = MdnsResolver.ParseAddressRecords(msg.ToArray(), msg.Count);

            Assert.Equal(2, results.Count);
            Assert.All(results, x => Assert.Equal(HOSTNAME, x.Key));
            Assert.Contains(v6, results.Select(x => x.Value));
            Assert.Contains(IPAddress.Parse("10.0.0.7"), results.Select(x => x.Value));
        }

        /// <summary>
        /// Tests that queries are ignored and that truncated or looping
        /// messages don't throw.
        /// </summary>
        [Fact]
        public void ParseMalformedUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var query = MdnsResolver.BuildQuery(HOSTNAME);
            Assert.Empty(MdnsResolver.ParseAddressRecords(query, query.Length));

            var msg = new List<byte> { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0 };
            msg.AddRange(EncodeName(HOSTNAME));
            msg.AddRange(new byte[] { 0, 1, 0x80, 1, 0, 0, 0, 120, 0, 4, 192, 168 });
            Assert.Empty(MdnsResolver.ParseAddressRecords(msg.ToArray(), msg.Count));

            // Compression pointer that points at itself.
            var loop = new byte[] { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0xC0, 12, 0, 1, 0, 1, 0, 0, 0, 0, 0, 4, 1, 2, 3, 4 };
            Assert.Empty(MdnsResolver.ParseAddressRecords(loop, loop.Length));

            Assert.Empty(MdnsResolver.ParseAddressRecords(new byte[] { 0, 0, 0x84 }, 3));
        }

        private static byte[] EncodeName(string name)
        {
            var bytes = new List<byte>();
            foreach (var label in name.Split('.'))
            {
                bytes.Add((byte)label.Length);
                bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(label));
            }
            bytes.Add(0);
            return bytes.ToArray();
        }
    }
}
