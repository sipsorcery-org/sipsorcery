//-----------------------------------------------------------------------------
// Filename: TypeExtensionsUnitTest.cs
//
// Description: Unit tests for methods in the TypeExtensions class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// ??   Aaron Clauson   Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class InterlockedExUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public InterlockedExUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void CompareExchangeU32()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint value = 10;
            uint was = InterlockedEx.CompareExchange(ref value, value: 20, comparand: 10);

            Assert.Equal(10u, was);
            Assert.Equal(20u, value);
        }
    }
}
