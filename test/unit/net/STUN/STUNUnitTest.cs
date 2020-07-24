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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class STUNUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public STUNUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Parse a STUN request received from the Chrome browser's WebRTC stack.
        /// </summary>
        [Fact]
        public void ParseWebRTCSTUNRequestTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] stunReq = new byte[]{ 0x00, 0x01, 0x00, 0x60, 0x21, 0x12, 0xa4, 0x42, 0x66, 0x55, 0x55, 0x43, 0x4b, 0x48, 0x74, 0x73, 0x68, 0x4e, 0x71, 0x56,
                                         // Att1: 
                                         0x00, 0x06, 0x00, 0x21,
                                         0x6d, 0x30, 0x71, 0x47, 0x77, 0x53, 0x71, 0x2f, 0x48, 0x56, 0x48, 0x71, 0x41, 0x62, 0x4b, 0x62, 0x3a, 0x73, 0x64, 0x43,
                                         0x48, 0x59, 0x6b, 0x35, 0x6e, 0x46, 0x34, 0x79, 0x44, 0x77, 0x55, 0x39, 0x53, 0x00, 0x00, 0x00,
                                         // Att2
                                         0x80, 0x2a, 0x00,  0x08,
                                         0xa0, 0x36, 0xc9, 0x6c, 0x30, 0xc6, 0x2f, 0xd2, 0x00, 0x25, 0x00, 0x00, 0x00, 0x24, 0x00, 0x04,
                                         0x6e, 0x7f, 0x1e, 0xff, 0x00, 0x08, 0x00, 0x14, 0x81, 0x4a, 0x4f, 0xaf, 0x3d, 0x99, 0x30, 0x67,
                                         0x66, 0xb9, 0x48, 0x67, 0x83, 0x72, 0xd5, 0xa0, 0x7a, 0x87, 0xb5, 0x3f, 0x80, 0x28, 0x00, 0x04,
                                         0x49, 0x7e, 0x51, 0x17 };

            STUNMessage stunMessage = STUNMessage.ParseSTUNMessage(stunReq, stunReq.Length);
            STUNHeader stunHeader = stunMessage.Header;

            logger.LogDebug("Request type = " + stunHeader.MessageType + ".");
            logger.LogDebug("Length = " + stunHeader.MessageLength + ".");
            logger.LogDebug("Transaction ID = " + BitConverter.ToString(stunHeader.TransactionId) + ".");

            Assert.Equal(STUNMessageTypesEnum.BindingRequest, stunHeader.MessageType);
            Assert.Equal(96, stunHeader.MessageLength);
            Assert.Equal(6, stunMessage.Attributes.Count);
        }

        /// <summary>
        /// Tests that a binding request with a username attribute is correctly output to a byte array.
        /// </summary>
        [Fact]
        public void BindingRequestWithUsernameToBytesUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNMessage initMessage = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            initMessage.AddUsernameAttribute("someusernamex");
            byte[] stunMessageBytes = initMessage.ToByteBuffer(null, false);

            logger.LogDebug(BitConverter.ToString(stunMessageBytes));

            Assert.True(stunMessageBytes.Length % 4 == 0);
        }

        /// <summary>
        /// Parse a STUN response received from the Chrome browser's WebRTC stack.
        /// </summary>
        [Fact]
        public void ParseWebRTCSTUNResponseTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] stunResp = new byte[]{ 0x01, 0x01, 0x00, 0x2c, 0x21, 0x12, 0xa4, 0x42, 0x6a, 0x45, 0x38, 0x2b, 0x4e, 0x5a, 0x4b, 0x50,
                    0x64, 0x31, 0x70, 0x38, 0x00, 0x20, 0x00, 0x08, 0x00, 0x01, 0xe0, 0xda, 0xe1, 0xba, 0x85, 0x3f,
                    0x00, 0x08, 0x00, 0x14, 0x24, 0x37, 0x24, 0xa0, 0x05, 0x2d, 0x88, 0x97, 0xce, 0xa6, 0x4e, 0x90,
                    0x69, 0xf6, 0x39, 0x07, 0x7d, 0xb1, 0x6e, 0x71, 0x80, 0x28, 0x00, 0x04, 0xde, 0x6a, 0x05, 0xac};

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

            Assert.Equal(STUNMessageTypesEnum.BindingSuccessResponse, stunHeader.MessageType);
            Assert.Equal(44, stunHeader.MessageLength);
            Assert.Equal(3, stunMessage.Attributes.Count);
        }

        /// <summary>
        /// Tests that parsing an XOR-MAPPED-ADDRESS attribute correctly extracts the IP Address and Port.
        /// </summary>
        [Fact]
        public void ParseXORMappedAddressAttributeTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] stunAttribute = new byte[] { 0x00, 0x01, 0xe0, 0xda, 0xe1, 0xba, 0x85, 0x3f };

            STUNXORAddressAttribute xorAddressAttribute = new STUNXORAddressAttribute(STUNAttributeTypesEnum.XORMappedAddress, stunAttribute);

            Assert.Equal(49608, xorAddressAttribute.Port);
            Assert.Equal("192.168.33.125", xorAddressAttribute.Address.ToString());
        }

        /// <summary>
        /// Tests that putting an XOR-MAPPED-ADDRESS attribute to a byte buffer works correctly.
        /// </summary>
        [Fact]
        public void PutXORMappedAddressAttributeToBufferTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNXORAddressAttribute xorAddressAttribute = new STUNXORAddressAttribute(STUNAttributeTypesEnum.XORMappedAddress, 49608, IPAddress.Parse("192.168.33.125"));

            byte[] buffer = new byte[12];
            xorAddressAttribute.ToByteBuffer(buffer, 0);

            Assert.Equal(0x00, buffer[0]);
            Assert.Equal(0x20, buffer[1]);
            Assert.Equal(0x00, buffer[2]);
            Assert.Equal(0x08, buffer[3]);
            Assert.Equal(0x00, buffer[4]);
            Assert.Equal(0x01, buffer[5]);
            Assert.Equal(0xe0, buffer[6]);
            Assert.Equal(0xda, buffer[7]);
            Assert.Equal(0xe1, buffer[8]);
            Assert.Equal(0xba, buffer[9]);
            Assert.Equal(0x85, buffer[10]);
            Assert.Equal(0x3f, buffer[11]);
        }

        /// <summary>
        /// Tests that putting a STUN response to a byte buffer works correctly.
        /// </summary>
        [Fact]
        public void PutResponseToBufferTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNMessage stunResponse = new STUNMessage(STUNMessageTypesEnum.BindingSuccessResponse);
            stunResponse.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
            //stunResponse.AddFingerPrintAttribute();
            stunResponse.AddXORMappedAddressAttribute(IPAddress.Parse("127.0.0.1"), 1234);

            byte[] buffer = stunResponse.ToByteBuffer(null, true);
        }

        /// <summary>
        /// Tests that the message integrity attribute is being correctly generated. The original STUN request packet
        /// was capture on the wire from the Google Chrom WebRTC stack.
        /// </summary>
        [Fact]
        public void TestMessageIntegrityAttributeForBindingRequest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] stunReq = new byte[]{
            0x00, 0x01, 0x00, 0x60, 0x21, 0x12, 0xa4, 0x42, 0x69, 0x64, 0x38, 0x2b, 0x4c, 0x45, 0x44, 0x57,
            0x4d, 0x31, 0x64, 0x30, 0x00, 0x06, 0x00, 0x21, 0x75, 0x4f, 0x35, 0x73, 0x69, 0x31, 0x75, 0x61,
            0x37, 0x63, 0x59, 0x34, 0x74, 0x38, 0x4d, 0x4d, 0x3a, 0x4c, 0x77, 0x38, 0x2f, 0x30, 0x43, 0x31,
            0x43, 0x72, 0x76, 0x68, 0x5a, 0x43, 0x31, 0x67, 0x62, 0x00, 0x00, 0x00, 0x80, 0x2a, 0x00, 0x08,
            0xc0, 0x3d, 0xf5, 0x13, 0x40, 0xf4, 0x22, 0x46, 0x00, 0x25, 0x00, 0x00, 0x00, 0x24, 0x00, 0x04,
            0x6e, 0x7f, 0x1e, 0xff, 0x00, 0x08, 0x00, 0x14, 0x55, 0x82, 0x69, 0xde, 0x17, 0x55, 0xcc, 0x66,
            0x29, 0x23, 0xe6, 0x7d, 0xec, 0x87, 0x6c, 0x07, 0x3a, 0xd6, 0x78, 0x15, 0x80, 0x28, 0x00, 0x04,
            0x1c, 0xae, 0x89, 0x2e};

            STUNMessage stunMessage = STUNMessage.ParseSTUNMessage(stunReq, stunReq.Length);
            STUNHeader stunHeader = stunMessage.Header;

            logger.LogDebug("Request type = " + stunHeader.MessageType + ".");
            logger.LogDebug("Length = " + stunHeader.MessageLength + ".");
            logger.LogDebug("Transaction ID = " + BitConverter.ToString(stunHeader.TransactionId) + ".");

            Assert.Equal(STUNMessageTypesEnum.BindingRequest, stunHeader.MessageType);
            Assert.Equal(96, stunHeader.MessageLength);
            Assert.Equal(6, stunMessage.Attributes.Count);
            Assert.Equal("69-64-38-2B-4C-45-44-57-4D-31-64-30", BitConverter.ToString(stunMessage.Header.TransactionId));

            stunMessage.Attributes.Remove(stunMessage.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.MessageIntegrity).Single());
            stunMessage.Attributes.Remove(stunMessage.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.FingerPrint).Single());

            byte[] buffer = stunMessage.ToByteBufferStringKey("r89XhWC9k2kW4Pns75vmwHIa", true);

            Assert.Equal(BitConverter.ToString(stunReq), BitConverter.ToString(buffer));
        }

        /// <summary>
        /// Parse a STUN response received from the Coturn TURN server.
        /// </summary>
        [Fact]
        public void ParseCoturnSTUNResponseTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] stunResp = new byte[]{ 0x01, 0x01, 0x00, 0x44, 0x21, 0x12, 0xa4, 0x42, 0x6b, 0x4c, 0xf3, 0x18, 0xd0, 0xa7, 0xf5, 0x40,
                    0x97, 0x30, 0x3a, 0x27, 0x00, 0x20, 0x00, 0x08, 0x00, 0x01, 0x9e, 0x90, 0x1a, 0xb5, 0x08, 0xf3,
                    0x00, 0x01, 0x00, 0x08, 0x00, 0x01, 0xbf, 0x82, 0x3b, 0xa7, 0xac, 0xb1, 0x80, 0x2b, 0x00, 0x08,
                    0x00, 0x01, 0x0d, 0x96, 0x67, 0x1d, 0x42, 0xf3, 0x80, 0x22, 0x00, 0x1a, 0x43, 0x6f, 0x74, 0x75,
                    0x72, 0x6e, 0x2d, 0x34, 0x2e, 0x35, 0x2e, 0x30, 0x2e, 0x33, 0x20, 0x27, 0x64, 0x61, 0x6e, 0x20,
                    0x45, 0x69, 0x64, 0x65, 0x72, 0x27, 0x77, 0x75};

            STUNMessage stunMessage = STUNMessage.ParseSTUNMessage(stunResp, stunResp.Length);

            STUNHeader stunHeader = stunMessage.Header;

            logger.LogDebug("Request type = " + stunHeader.MessageType + ".");
            logger.LogDebug("Length = " + stunHeader.MessageLength + ".");
            logger.LogDebug("Transaction ID = " + BitConverter.ToString(stunHeader.TransactionId) + ".");

            foreach (STUNAttribute attribute in stunMessage.Attributes)
            {
                if (attribute.AttributeType == STUNAttributeTypesEnum.MappedAddress)
                {
                    STUNAddressAttribute addressAttribute = new STUNAddressAttribute(attribute.Value);
                    logger.LogDebug(" " + attribute.AttributeType + " " + addressAttribute.Address + ":" + addressAttribute.Port + ".");

                    Assert.Equal("59.167.172.177", addressAttribute.Address.ToString());
                    Assert.Equal(49026, addressAttribute.Port);
                }
                else if (attribute.AttributeType == STUNAttributeTypesEnum.XORMappedAddress)
                {
                    STUNXORAddressAttribute xorAddressAttribute = new STUNXORAddressAttribute(STUNAttributeTypesEnum.XORMappedAddress, attribute.Value);
                    logger.LogDebug(" " + attribute.AttributeType + " " + xorAddressAttribute.Address + ":" + xorAddressAttribute.Port + ".");

                    Assert.Equal("59.167.172.177", xorAddressAttribute.Address.ToString());
                    Assert.Equal(49026, xorAddressAttribute.Port);
                }

                else
                {
                    logger.LogDebug(" " + attribute.AttributeType + " " + attribute.Value + ".");
                }
            }

            Assert.Equal(STUNMessageTypesEnum.BindingSuccessResponse, stunHeader.MessageType);
        }

        /// <summary>
        /// Tests that the fingerprint and hmac attributes get generated correctly.
        /// </summary>
        [Fact]
        public void GenerateHmacAndFingerprintTestMethod()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string icePassword = "SKYKPPYLTZOAVCLTGHDUODANRKSPOVQVKXJULOGG";

            STUNMessage msg = new STUNMessage(STUNMessageTypesEnum.BindingSuccessResponse);
            msg.Header.TransactionId = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            msg.AddXORMappedAddressAttribute(IPAddress.Loopback, 55477);
            var buffer = msg.ToByteBufferStringKey(icePassword, true);

            string hmac = "HMAC: ";
            for (int i = 36; i < 56; i++)
            {
                hmac += $"{buffer[i]:X2} ";
            }
            logger.LogDebug(hmac);

            logger.LogDebug($"Fingerprint: {buffer[buffer.Length - 4]:X2} {buffer[buffer.Length - 3]:X2} {buffer[buffer.Length - 2]:X2} {buffer[buffer.Length - 1]:X2}.");
        }

        /// <summary>
        /// Tests that the STUN header class type is correctly determined from the message type.
        /// </summary>
        [Fact]
        public void CheckCLassForSTUNMessageTypeUnitTest()
        {
            Assert.Equal(STUNClassTypesEnum.Request, (new STUNHeader(STUNMessageTypesEnum.BindingRequest).MessageClass));
            Assert.Equal(STUNClassTypesEnum.Request, (new STUNHeader(STUNMessageTypesEnum.Allocate).MessageClass)); 
            Assert.Equal(STUNClassTypesEnum.Request, (new STUNHeader(STUNMessageTypesEnum.Refresh).MessageClass));
            Assert.Equal(STUNClassTypesEnum.Request, (new STUNHeader(STUNMessageTypesEnum.Send).MessageClass));
            Assert.Equal(STUNClassTypesEnum.Request, (new STUNHeader(STUNMessageTypesEnum.Data).MessageClass));
            Assert.Equal(STUNClassTypesEnum.Request, (new STUNHeader(STUNMessageTypesEnum.CreatePermission).MessageClass));
            Assert.Equal(STUNClassTypesEnum.Request, (new STUNHeader(STUNMessageTypesEnum.ChannelBind).MessageClass));

            Assert.Equal(STUNClassTypesEnum.SuccesResponse, (new STUNHeader(STUNMessageTypesEnum.BindingSuccessResponse).MessageClass));
            Assert.Equal(STUNClassTypesEnum.SuccesResponse, (new STUNHeader(STUNMessageTypesEnum.AllocateSuccessResponse).MessageClass));
            Assert.Equal(STUNClassTypesEnum.SuccesResponse, (new STUNHeader(STUNMessageTypesEnum.CreatePermissionSuccessResponse).MessageClass));

            Assert.Equal(STUNClassTypesEnum.ErrorResponse, (new STUNHeader(STUNMessageTypesEnum.BindingErrorResponse).MessageClass));
            Assert.Equal(STUNClassTypesEnum.ErrorResponse, (new STUNHeader(STUNMessageTypesEnum.AllocateErrorResponse).MessageClass));
            Assert.Equal(STUNClassTypesEnum.ErrorResponse, (new STUNHeader(STUNMessageTypesEnum.CreatePermissionErrorResponse).MessageClass));

            Assert.Equal(STUNClassTypesEnum.Indication, (new STUNHeader(STUNMessageTypesEnum.DataIndication).MessageClass));
            Assert.Equal(STUNClassTypesEnum.Indication, (new STUNHeader(STUNMessageTypesEnum.SendIndication).MessageClass));
        }

        /// <summary>
        /// Tests that a locally signed STUN request can be verified.
        /// </summary>
        [Fact]
        public void IntegrityCheckUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string icePassword = "SKYKPPYLTZOAVCLTGHDUODANRKSPOVQVKXJULOGG";

            STUNMessage stunRequest = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            stunRequest.Header.TransactionId = Encoding.ASCII.GetBytes(Crypto.GetRandomString(STUNHeader.TRANSACTION_ID_LENGTH));
            stunRequest.AddUsernameAttribute("xxxx:yyyy");
            stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Priority, BitConverter.GetBytes(1)));

            var buffer = stunRequest.ToByteBufferStringKey(icePassword, true);

            //logger.LogDebug($"HMAC: {buffer.Skip(buffer.Length - ).Take(20).ToArray().HexStr()}.");
            //logger.LogDebug($"Fingerprint: {buffer.Skip(buffer.Length -4).ToArray().HexStr()}.");

            STUNMessage rndTripReq = STUNMessage.ParseSTUNMessage(buffer, buffer.Length);

            Assert.True(rndTripReq.isFingerprintValid);
            Assert.True(rndTripReq.CheckIntegrity(System.Text.Encoding.UTF8.GetBytes(icePassword)));
        }

        /// <summary>
        /// Tests that a known STUN request can be verified.
        /// </summary>
        [Fact]
        public void KnownSTUNBindingRequestIntegrityCheckUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string icePassword = "DVJSBHBUIBFSZFKVECMPRISQ";

            byte[] buffer = TypeExtensions.ParseHexStr(
                "0001003C2112A4424A5655444B44544753454455000600095A4C45423A4554454F00000000240" +
                "008CC3A28000000000000080014B295EDA4BC88A0BC885D745644D36E51FE3CBD1880280004EDF60FF7");

            STUNMessage stunRequest = STUNMessage.ParseSTUNMessage(buffer, buffer.Length);

            Assert.True(stunRequest.isFingerprintValid);
            Assert.True(stunRequest.CheckIntegrity(System.Text.Encoding.UTF8.GetBytes(icePassword)));
        }
    }
}
