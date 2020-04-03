using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    /// <summary>This class contains parameterized unit tests for KeyParameter</summary>
    [Trait("Category", "unit")]
    public partial class KeyParameterUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public KeyParameterUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }


        [Fact]
        public void KeySaltBase64Test()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPSecurityDescription.KeyParameter keyParameter = KeyParameterFactory.Create("ĀĀ\0\0\0\0\0\0\0\0\0\0\0\0\0\0", "ĀĀĀ\0\0\0\0\0\0\0\0\0\0\0");
            Assert.NotNull((object)keyParameter);
            Assert.Equal(Encoding.ASCII.GetBytes("ĀĀ\0\0\0\0\0\0\0\0\0\0\0\0\0\0"), keyParameter.Key);
            Assert.Equal(Encoding.ASCII.GetBytes("ĀĀĀ\0\0\0\0\0\0\0\0\0\0\0"), keyParameter.Salt);
            Assert.Equal(0uL, keyParameter.LifeTime);
            Assert.Equal((string)null, keyParameter.LifeTimeString);
            Assert.Equal(0u, keyParameter.MkiValue);
            Assert.Equal(0u, keyParameter.MkiLength);

            Assert.Equal(40, keyParameter.KeySaltBase64.Length);
        }

        [Fact]
        public void LifeTimeTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPSecurityDescription.KeyParameter keyParameter = KeyParameterFactory.Create("ĀĀ\0\0\0\0\0\0\0\0\0\0\0\0\0\0", "ĀĀĀ\0\0\0\0\0\0\0\0\0\0\0");
            Assert.Throws<ArgumentOutOfRangeException>(() => keyParameter.LifeTime = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => keyParameter.LifeTime = 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => keyParameter.LifeTime = 3);
            keyParameter.LifeTime = 2;
            Assert.Equal(2uL, keyParameter.LifeTime);
            Assert.Equal("2^1", keyParameter.LifeTimeString);
            keyParameter.LifeTime = 4;
            Assert.Equal(4uL, keyParameter.LifeTime);
            Assert.Equal("2^2", keyParameter.LifeTimeString);
            keyParameter.LifeTime = 64;
            Assert.Equal(64uL, keyParameter.LifeTime);
            Assert.NotEqual("2^1", keyParameter.LifeTimeString);
            Assert.Equal("2^6", keyParameter.LifeTimeString);
        }

        [Fact]
        public void LifeTimeStringTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPSecurityDescription.KeyParameter keyParameter = KeyParameterFactory.Create("ĀĀ\0\0\0\0\0\0\0\0\0\0\0\0\0\0", "ĀĀĀ\0\0\0\0\0\0\0\0\0\0\0");
            Assert.Throws<ArgumentNullException>(() => keyParameter.LifeTimeString = null);
            Assert.Throws<ArgumentNullException>(() => keyParameter.LifeTimeString = "");
            Assert.Throws<ArgumentException>(() => keyParameter.LifeTimeString = "ĀĀ\0\0\0\0");
            Assert.Throws<FormatException>(() => keyParameter.LifeTimeString = "2^");
            Assert.Throws<OverflowException>(() => keyParameter.LifeTimeString = "2^-1");
            Assert.Throws<FormatException>(() => keyParameter.LifeTimeString = "2^0.");
            Assert.Throws<ArgumentOutOfRangeException>(() => keyParameter.LifeTimeString = "2^0");
            Assert.Throws<FormatException>(() => keyParameter.LifeTimeString = "2^1.3");
            Assert.Throws<FormatException>(() => keyParameter.LifeTimeString = "2^1afg6");
            Assert.Throws<FormatException>(() => keyParameter.LifeTimeString = "2^\06");
            Assert.Throws<FormatException>(() => keyParameter.LifeTimeString = "2^6.0");

            keyParameter.LifeTimeString = "2^1";
            Assert.Equal(2uL, keyParameter.LifeTime);
            Assert.Equal("2^1", keyParameter.LifeTimeString);
            keyParameter.LifeTimeString = "2^2";
            Assert.Equal(4uL, keyParameter.LifeTime);
            Assert.Equal("2^2", keyParameter.LifeTimeString);
            keyParameter.LifeTimeString = "2^6";
            Assert.Equal(64uL, keyParameter.LifeTime);
            Assert.NotEqual("2^1", keyParameter.LifeTimeString);
            Assert.Equal("2^6", keyParameter.LifeTimeString);
            Assert.Equal(64uL, keyParameter.LifeTime);
        }

        [Fact]
        public void ParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPSecurityDescription.KeyParameter kp1 = SDPSecurityDescription.KeyParameter.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^20|1:4");
            Assert.Equal("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^20|1:4", kp1.ToString());
            Assert.Equal(kp1.ToString(), SDPSecurityDescription.KeyParameter.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^20|1:4").ToString());
            Assert.Equal(kp1.ToString(), SDPSecurityDescription.KeyParameter.Parse(kp1.ToString()).ToString());
            Assert.Equal(4u, kp1.MkiLength);
            Assert.Equal(1u, kp1.MkiValue);
            Assert.Equal((ulong)Math.Pow(2, 20), kp1.LifeTime);
            Assert.Equal("2^20", kp1.LifeTimeString);
            Assert.Equal("MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm", kp1.KeySaltBase64);

            SDPSecurityDescription.KeyParameter kp2 = SDPSecurityDescription.KeyParameter.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^20");
            Assert.Equal((ulong)Math.Pow(2, 20), kp2.LifeTime);
            Assert.Equal("2^20", kp2.LifeTimeString);
            Assert.Equal(0u, kp2.MkiLength);
            Assert.Equal(0u, kp2.MkiValue);

            SDPSecurityDescription.KeyParameter kp3 = SDPSecurityDescription.KeyParameter.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|1:4");
            Assert.Equal(0uL, kp3.LifeTime);
            Assert.Equal(4u, actual: kp3.MkiLength);
            Assert.Equal(1u, kp3.MkiValue);

            SDPSecurityDescription.KeyParameter kp4 = SDPSecurityDescription.KeyParameter.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|1:4|2^20");
            Assert.Equal("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^20|1:4", kp4.ToString());
            Assert.Equal(SDPSecurityDescription.KeyParameter.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^20|1:4").ToString(), kp4.ToString());
            Assert.Equal(SDPSecurityDescription.KeyParameter.Parse(kp4.ToString()).ToString(), kp4.ToString());
            Assert.Equal(4u, kp4.MkiLength);
            Assert.Equal(1u, kp4.MkiValue);
            Assert.Equal((ulong)Math.Pow(2, 20), kp4.LifeTime);
            Assert.Equal("2^20", kp4.LifeTimeString);
            Assert.Equal("MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm", kp4.KeySaltBase64);

            SDPSecurityDescription.KeyParameter kp5 = KeyParameterFactory.Create("ĀĀ\0\0\0\0\0\0\0\0\0\0\0\0\0\0", "ĀĀĀ\0\0\0\0\0\0\0\0\0\0\0");
            Assert.Equal(SDPSecurityDescription.KeyParameter.Parse(kp5.ToString()).ToString(), kp5.ToString());

            Assert.Throws<FormatException>(() => SDPSecurityDescription.KeyParameter.Parse(null));
            Assert.Throws<FormatException>(() => SDPSecurityDescription.KeyParameter.Parse(""));

            Assert.Throws<FormatException>(() => SDPSecurityDescription.KeyParameter.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^20|1:4;inline:QUJjZGVmMTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5|2^20|2:4"));
            Assert.Throws<FormatException>(() => SDPSecurityDescription.KeyParameter.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|20^2|1:4"));
            Assert.Throws<FormatException>(() => SDPSecurityDescription.KeyParameter.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^0|1:4"));
            Assert.Throws<FormatException>(() => SDPSecurityDescription.KeyParameter.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTINDU2Nzg5QUJjZGVm|2^20|1:4"));
            Assert.Throws<FormatException>(() => SDPSecurityDescription.KeyParameter.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^20|14"));
            Assert.Throws<FormatException>(() => SDPSecurityDescription.KeyParameter.Parse("inline: MTIzNDU2Nzg5QUJDREUwMTINDU2Nzg5QUJjZGVm|2^20|1:4"));
        }
    }
}
