using System;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    /// <summary>This class contains parameterized unit tests for SessionParameter</summary>
    [Trait("Category", "unit")]
    public partial class SessionParameterUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SessionParameterUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void ConstructorTestEnumParams()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            foreach (var e in Enum.GetValues(typeof(SDPSecurityDescription.SessionParameter.SrtpSessionParams)))
            {
                SDPSecurityDescription.SessionParameter sessionParameter = SessionParameterFactory.Create((SDPSecurityDescription.SessionParameter.SrtpSessionParams)e);
                Assert.NotNull((object)sessionParameter);

                Assert.Equal<SDPSecurityDescription.SessionParameter.SrtpSessionParams>((SDPSecurityDescription.SessionParameter.SrtpSessionParams)e, sessionParameter.SrtpSessionParam);
            }
        }
        [Fact]
        public void ConstructorTestFecKey()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPSecurityDescription.SessionParameter sessionParameter = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.fec_key);
            Assert.StartsWith(SDPSecurityDescription.SessionParameter.FEC_KEY_PREFIX, sessionParameter.ToString());
        }
        [Fact]
        public void ConstructorTestFecOrder()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPSecurityDescription.SessionParameter sessionParameter = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.fec_order);
            Assert.StartsWith(SDPSecurityDescription.SessionParameter.FEC_ORDER_PREFIX, sessionParameter.ToString());
        }
        [Fact]
        public void ConstructorTestWsh()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPSecurityDescription.SessionParameter sessionParameter = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.wsh);
            Assert.StartsWith(SDPSecurityDescription.SessionParameter.WSH_PREFIX, sessionParameter.ToString());
        }
        [Fact]
        public void ConstructorTestKdr()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPSecurityDescription.SessionParameter sessionParameter = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.kdr);
            Assert.StartsWith(SDPSecurityDescription.SessionParameter.KDR_PREFIX, sessionParameter.ToString());
        }
        [Fact]
        public void ConstructorTestUNEnums()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPSecurityDescription.SessionParameter sessionParameter = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNAUTHENTICATED_SRTP);
            Assert.Equal(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNAUTHENTICATED_SRTP.ToString(), sessionParameter.ToString());
            sessionParameter = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNENCRYPTED_SRTCP);
            Assert.Equal(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNENCRYPTED_SRTCP.ToString(), sessionParameter.ToString());
            sessionParameter = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNENCRYPTED_SRTP);
            Assert.Equal(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNENCRYPTED_SRTP.ToString(), sessionParameter.ToString());
        }

        [Fact]
        public void WshTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPSecurityDescription.SessionParameter sessionParameter = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.wsh);
            try
            {
                sessionParameter.Wsh = 0;
                throw new Exception
                          ("expected an exception of type ArgumentOutOfRangeException");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            try
            {
                sessionParameter.Wsh = 1;
                throw new Exception
                          ("expected an exception of type ArgumentOutOfRangeException");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            try
            {
                sessionParameter.Wsh = 3;
                throw new Exception
                          ("expected an exception of type ArgumentOutOfRangeException");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            sessionParameter.Wsh = 64;
            Assert.Equal(sessionParameter.ToString(), $"{SDPSecurityDescription.SessionParameter.WSH_PREFIX}64");
        }
        [Fact]
        public void KdrTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPSecurityDescription.SessionParameter sessionParameter = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.kdr);
            try
            {
                sessionParameter.Kdr = 100;
                throw new Exception
                          ("expected an exception of type ArgumentOutOfRangeException");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            sessionParameter.Kdr = 2;
            Assert.Equal($"{SDPSecurityDescription.SessionParameter.KDR_PREFIX}2", sessionParameter.ToString());
            sessionParameter.Kdr = 4;
            Assert.Equal($"{SDPSecurityDescription.SessionParameter.KDR_PREFIX}4", sessionParameter.ToString());
            sessionParameter.Kdr = 3;
            Assert.Equal($"{SDPSecurityDescription.SessionParameter.KDR_PREFIX}3", sessionParameter.ToString());
        }

        [Fact]
        public void ParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SDPSecurityDescription.SessionParameter sessionParameterKdr = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.kdr, 4);
            string sKdr = sessionParameterKdr.ToString();
            Assert.Equal(sKdr, SDPSecurityDescription.SessionParameter.Parse(sKdr).ToString());
            Assert.Equal("KDR=4", SDPSecurityDescription.SessionParameter.Parse(sKdr).ToString());

            SDPSecurityDescription.SessionParameter sessionParameterWsh = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.wsh, 64);
            string sWsh = sessionParameterWsh.ToString();
            Assert.Equal(sWsh, SDPSecurityDescription.SessionParameter.Parse(sWsh).ToString());
            Assert.Equal("WSH=64", SDPSecurityDescription.SessionParameter.Parse(sWsh).ToString());

            SDPSecurityDescription.SessionParameter sessionParameterFecOrder = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.fec_order, (uint)SDPSecurityDescription.SessionParameter.FecTypes.FEC_SRTP);
            string sFecOrder = sessionParameterFecOrder.ToString();
            Assert.Equal(sFecOrder, SDPSecurityDescription.SessionParameter.Parse(sFecOrder).ToString());
            Assert.Equal(sFecOrder, SDPSecurityDescription.SessionParameter.Parse("FEC_ORDER=FEC_SRTP").ToString());

            sessionParameterFecOrder.FecOrder = SDPSecurityDescription.SessionParameter.FecTypes.SRTP_FEC;
            Assert.NotEqual(sFecOrder, sessionParameterFecOrder.ToString());
            sFecOrder = sessionParameterFecOrder.ToString();
            Assert.Equal(sFecOrder, SDPSecurityDescription.SessionParameter.Parse(sFecOrder).ToString());

            SDPSecurityDescription.SessionParameter sessionParameterFecKey = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.fec_key);
            sessionParameterFecKey.FecKey = SDPSecurityDescription.KeyParameter.Parse("inline:MTIzNDU2Nzg5QUJDREUwMTIzNDU2Nzg5QUJjZGVm|2^20|1:4");
            string FecKey = sessionParameterFecKey.ToString();
            Assert.StartsWith(SDPSecurityDescription.SessionParameter.FEC_KEY_PREFIX, FecKey);
            Assert.EndsWith("1:4", FecKey);

            SDPSecurityDescription.SessionParameter sessionParameterUn1 = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNAUTHENTICATED_SRTP);
            SDPSecurityDescription.SessionParameter sessionParameterUn2 = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNENCRYPTED_SRTCP);
            SDPSecurityDescription.SessionParameter sessionParameterUn3 = SessionParameterFactory.Create(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNENCRYPTED_SRTP);
            string sUn1 = sessionParameterUn1.ToString();
            string sUn2 = sessionParameterUn2.ToString();
            string sUn3 = sessionParameterUn3.ToString();
            Assert.Equal(sUn1, SDPSecurityDescription.SessionParameter.Parse(sUn1).ToString());
            Assert.NotEqual(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNENCRYPTED_SRTP, SDPSecurityDescription.SessionParameter.Parse(sUn2).SrtpSessionParam);
            Assert.Equal(SDPSecurityDescription.SessionParameter.SrtpSessionParams.UNENCRYPTED_SRTP, SDPSecurityDescription.SessionParameter.Parse(sUn3).SrtpSessionParam);

            Assert.Null(SDPSecurityDescription.SessionParameter.Parse(null));
            Assert.Null(SDPSecurityDescription.SessionParameter.Parse(""));

            Assert.Throws<FormatException>(() => SDPSecurityDescription.SessionParameter.Parse("wsh=64"));
            Assert.Throws<FormatException>(() => SDPSecurityDescription.SessionParameter.Parse("ĀĀ\0\0\0\0\0\0\0\0\0\0\0\0\0\0"));
        }
    }
}

