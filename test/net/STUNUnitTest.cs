//-----------------------------------------------------------------------------
// Author(s):
// Aaron Clauson
// 
// History:
// 
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class STUNUnitTest
    {
        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        public STUNUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void ParseAsteriskSTUNRequestTestMethod()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] stunReq = new byte[]{ 0x00, 0x01, 0x00, 0x24, 0x0d, 0x69, 0x62, 0x59, 0xac, 0xdb, 0xf4, 0x1d, 0x6d, 0x46, 0xdd, 0x5b,
                                         0xf3, 0x04, 0x6a, 0x30, 0x00, 0x06, 0x00, 0x20, 0x69, 0x59, 0x6d, 0x71, 0x52, 0x33, 0x51, 0x46,
                                         0x6d, 0x65, 0x65, 0x4c, 0x42, 0x62, 0x41, 0x6f, 0x37, 0x39, 0x30, 0x32, 0x30, 0x33, 0x65, 0x37,
                                         0x34, 0x31, 0x64, 0x33, 0x37, 0x32 };

            STUNMessage stunMessage = STUNMessage.ParseSTUNMessage(stunReq, stunReq.Length);
            STUNHeader stunHeader = stunMessage.Header;
            //STUNHeader stunHeader = STUNHeader.ParseSTUNHeader(stunReq);

            logger.LogDebug("Request type = " + stunHeader.MessageType + ".");
            logger.LogDebug("Length = " + stunHeader.MessageLength + ".");
            logger.LogDebug("Transaction ID = " + BitConverter.ToString(stunHeader.TransactionId) + ".");
        }

        [Fact]
        public void STUNWithUsernameToBytesUnitTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNMessage initMessage = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            initMessage.AddUsernameAttribute("someusername");
            byte[] stunMessageBytes = initMessage.ToByteBuffer();

            logger.LogDebug(BitConverter.ToString(stunMessageBytes));
        }

        [Fact]
        public void ParseSTUNResponseTestMethod()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] stunResp = new byte[]{ 0x01, 0x01, 0x00, 0x30, 0x73, 0x63, 0x78, 0x30, 0x46, 0x4f, 0x41, 0x64, 0x69, 0x30,
                0x79, 0x52, 0x5a, 0x51, 0x42, 0x50, 0x00, 0x06, 0x00, 0x20, 0x37, 0x39, 0x30, 0x32, 0x30, 0x33, 0x65, 0x37, 0x34,
                0x31, 0x64, 0x33, 0x37, 0x32, 0x65, 0x62, 0x69, 0x59, 0x6d, 0x71, 0x52, 0x33, 0x51, 0x46, 0x6d, 0x65, 0x65, 0x4c,
                0x42, 0x62};

            STUNMessage stunMessage = STUNMessage.ParseSTUNMessage(stunResp, stunResp.Length);

            STUNHeader stunHeader = stunMessage.Header;

            logger.LogDebug("Request type = " + stunHeader.MessageType + ".");
            logger.LogDebug("Length = " + stunHeader.MessageLength + ".");
            logger.LogDebug("Transaction ID = " + BitConverter.ToString(stunHeader.TransactionId) + ".");

            foreach (STUNAttribute attribute in stunMessage.Attributes)
            {
                if (attribute.AttributeType == STUNAttributeTypesEnum.Username)
                {
                    logger.LogDebug(" " + attribute.AttributeType + " " + Encoding.UTF8.GetString(attribute.Value) + ".");
                }
                else
                {
                    logger.LogDebug(" " + attribute.AttributeType + " " + attribute.Value + ".");
                }
            }
        }
    }
}
