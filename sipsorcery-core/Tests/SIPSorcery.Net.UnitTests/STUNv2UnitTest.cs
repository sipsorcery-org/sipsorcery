﻿using System;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Net;

namespace SIPSorcery.SIP.Core.UnitTests
{
    [TestClass]
    public class STUNv2UnitTest
    {
        /// <summary>
        /// Parse a STUN request received from the Chrome browser's WebRTC stack.
        /// </summary>
        [TestMethod]
        public void ParseWebRTCSTUNRequestTestMethod()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            STUNv2Message stunMessage = STUNv2Message.ParseSTUNMessage(stunReq, stunReq.Length);
            STUNv2Header stunHeader = stunMessage.Header;

            Console.WriteLine("Request type = " + stunHeader.MessageType + ".");
            Console.WriteLine("Length = " + stunHeader.MessageLength + ".");
            Console.WriteLine("Transaction ID = " + BitConverter.ToString(stunHeader.TransactionId) + ".");

            Assert.AreEqual(STUNv2MessageTypesEnum.BindingRequest, stunHeader.MessageType);
            Assert.AreEqual(96, stunHeader.MessageLength);
            Assert.AreEqual(6, stunMessage.Attributes.Count);
        }

        /// <summary>
        /// Tests that a binding request with a username attribute is correctly output to a byte array.
        /// </summary>
        [TestMethod]
        public void BindingRequestWithUsernameToBytesUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNv2Message initMessage = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
            initMessage.AddUsernameAttribute("someusernamex");
            byte[] stunMessageBytes = initMessage.ToByteBuffer(null, false);

            Console.WriteLine(BitConverter.ToString(stunMessageBytes));

            Assert.IsTrue(stunMessageBytes.Length % 4 == 0);
        }

        /// <summary>
        /// Parse a STUN response received from the Chrome browser's WebRTC stack.
        /// </summary>
        [TestMethod]
        public void ParseWebRTCSTUNResponseTestMethod()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] stunResp = new byte[]{ 0x01, 0x01, 0x00, 0x2c, 0x21, 0x12, 0xa4, 0x42, 0x6a, 0x45, 0x38, 0x2b, 0x4e, 0x5a, 0x4b, 0x50,
                    0x64, 0x31, 0x70, 0x38, 0x00, 0x20, 0x00, 0x08, 0x00, 0x01, 0xe0, 0xda, 0xe1, 0xba, 0x85, 0x3f,
                    0x00, 0x08, 0x00, 0x14, 0x24, 0x37, 0x24, 0xa0, 0x05, 0x2d, 0x88, 0x97, 0xce, 0xa6, 0x4e, 0x90,
                    0x69, 0xf6, 0x39, 0x07, 0x7d, 0xb1, 0x6e, 0x71, 0x80, 0x28, 0x00, 0x04, 0xde, 0x6a, 0x05, 0xac};

            STUNv2Message stunMessage = STUNv2Message.ParseSTUNMessage(stunResp, stunResp.Length);

            STUNv2Header stunHeader = stunMessage.Header;

            Console.WriteLine("Request type = " + stunHeader.MessageType + ".");
            Console.WriteLine("Length = " + stunHeader.MessageLength + ".");
            Console.WriteLine("Transaction ID = " + BitConverter.ToString(stunHeader.TransactionId) + ".");

            foreach (STUNv2Attribute attribute in stunMessage.Attributes)
            {
                if (attribute.AttributeType == STUNv2AttributeTypesEnum.Username)
                {
                    Console.WriteLine(" " + attribute.AttributeType + " " + Encoding.UTF8.GetString(attribute.Value) + ".");
                }
                else
                {
                    Console.WriteLine(" " + attribute.AttributeType + " " + attribute.Value + ".");
                }
            }

            Assert.AreEqual(STUNv2MessageTypesEnum.BindingSuccessResponse, stunHeader.MessageType);
            Assert.AreEqual(44, stunHeader.MessageLength);
            Assert.AreEqual(3, stunMessage.Attributes.Count);
        }

        /// <summary>
        /// Tests that parsing an XOR-MAPPED-ADDRESS attribute correctly extracts the IP Address and Port.
        /// </summary>
        [TestMethod]
        public void ParseXORMappedAddressAttributeTestMethod()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] stunAttribute = new byte[]{0x00, 0x01, 0xe0, 0xda, 0xe1, 0xba, 0x85, 0x3f};

            STUNv2XORAddressAttribute xorAddressAttribute = new STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum.XORMappedAddress, stunAttribute);

            Assert.AreEqual(49608, xorAddressAttribute.Port);
            Assert.AreEqual("192.168.33.125", xorAddressAttribute.Address.ToString());
        }

        /// <summary>
        /// Tests that putting an XOR-MAPPED-ADDRESS attribute to a byte buffer works correctly.
        /// </summary>
        [TestMethod]
        public void PutXORMappedAddressAttributeToBufferTestMethod()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            STUNv2XORAddressAttribute xorAddressAttribute = new STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum.XORMappedAddress, 49608, IPAddress.Parse("192.168.33.125"));

            byte[] buffer = new byte[12];
            xorAddressAttribute.ToByteBuffer(buffer, 0);

            Assert.AreEqual(0x00, buffer[0]);
            Assert.AreEqual(0x20, buffer[1]);
            Assert.AreEqual(0x00, buffer[2]);
            Assert.AreEqual(0x08, buffer[3]);
            Assert.AreEqual(0x00, buffer[4]);
            Assert.AreEqual(0x01, buffer[5]);
            Assert.AreEqual(0xe0, buffer[6]);
            Assert.AreEqual(0xda, buffer[7]);
            Assert.AreEqual(0xe1, buffer[8]);
            Assert.AreEqual(0xba, buffer[9]);
            Assert.AreEqual(0x85, buffer[10]);
            Assert.AreEqual(0x3f, buffer[11]);
        }

        /// <summary>
        /// Tests that putting a STUN response to a byte buffer works correctly.
        /// </summary>
        [TestMethod]
        public void PutResponseToBufferTestMethod()
        {
            STUNv2Message stunResponse = new STUNv2Message(STUNv2MessageTypesEnum.BindingSuccessResponse);
            stunResponse.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
            //stunResponse.AddFingerPrintAttribute();
            stunResponse.AddXORMappedAddressAttribute(IPAddress.Parse("127.0.0.1"), 1234);
            
            byte[] buffer = stunResponse.ToByteBuffer(null, true);
        }

        /// <summary>
        /// Tests that the message integrity attribute is being correctly generated. The original STUN request packet
        /// was capture on the wire from the Google Chrom WebRTC stack.
        /// </summary>
        [TestMethod]
        public void TestMessageIntegrityAttributeForBindingRequest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] stunReq = new byte[]{
            0x00, 0x01, 0x00, 0x60, 0x21, 0x12, 0xa4, 0x42, 0x69, 0x64, 0x38, 0x2b, 0x4c, 0x45, 0x44, 0x57,
            0x4d, 0x31, 0x64, 0x30, 0x00, 0x06, 0x00, 0x21, 0x75, 0x4f, 0x35, 0x73, 0x69, 0x31, 0x75, 0x61,
            0x37, 0x63, 0x59, 0x34, 0x74, 0x38, 0x4d, 0x4d, 0x3a, 0x4c, 0x77, 0x38, 0x2f, 0x30, 0x43, 0x31,
            0x43, 0x72, 0x76, 0x68, 0x5a, 0x43, 0x31, 0x67, 0x62, 0x00, 0x00, 0x00, 0x80, 0x2a, 0x00, 0x08,
            0xc0, 0x3d, 0xf5, 0x13, 0x40, 0xf4, 0x22, 0x46, 0x00, 0x25, 0x00, 0x00, 0x00, 0x24, 0x00, 0x04,
            0x6e, 0x7f, 0x1e, 0xff, 0x00, 0x08, 0x00, 0x14, 0x55, 0x82, 0x69, 0xde, 0x17, 0x55, 0xcc, 0x66,
            0x29, 0x23, 0xe6, 0x7d, 0xec, 0x87, 0x6c, 0x07, 0x3a, 0xd6, 0x78, 0x15, 0x80, 0x28, 0x00, 0x04,
            0x1c, 0xae, 0x89, 0x2e};

            STUNv2Message stunMessage = STUNv2Message.ParseSTUNMessage(stunReq, stunReq.Length);
            STUNv2Header stunHeader = stunMessage.Header;

            Console.WriteLine("Request type = " + stunHeader.MessageType + ".");
            Console.WriteLine("Length = " + stunHeader.MessageLength + ".");
            Console.WriteLine("Transaction ID = " + BitConverter.ToString(stunHeader.TransactionId) + ".");

            Assert.AreEqual(STUNv2MessageTypesEnum.BindingRequest, stunHeader.MessageType);
            Assert.AreEqual(96, stunHeader.MessageLength);
            Assert.AreEqual(6, stunMessage.Attributes.Count);
            Assert.AreEqual("69-64-38-2B-4C-45-44-57-4D-31-64-30", BitConverter.ToString(stunMessage.Header.TransactionId));

            stunMessage.Attributes.Remove(stunMessage.Attributes.Where(x => x.AttributeType == STUNv2AttributeTypesEnum.MessageIntegrity).Single());
            stunMessage.Attributes.Remove(stunMessage.Attributes.Where(x => x.AttributeType == STUNv2AttributeTypesEnum.FingerPrint).Single());

            byte[] buffer = stunMessage.ToByteBufferStringKey("r89XhWC9k2kW4Pns75vmwHIa", true);

            Assert.AreEqual(BitConverter.ToString(stunReq), BitConverter.ToString(buffer));
        }

        /// <summary>
        /// Parse a STUN response received from the Coturn TURN server.
        /// </summary>
        [TestMethod]
        public void ParseCoturnSTUNResponseTestMethod()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] stunResp = new byte[]{ 0x01, 0x01, 0x00, 0x44, 0x21, 0x12, 0xa4, 0x42, 0x6b, 0x4c, 0xf3, 0x18, 0xd0, 0xa7, 0xf5, 0x40,
                    0x97, 0x30, 0x3a, 0x27, 0x00, 0x20, 0x00, 0x08, 0x00, 0x01, 0x9e, 0x90, 0x1a, 0xb5, 0x08, 0xf3,
                    0x00, 0x01, 0x00, 0x08, 0x00, 0x01, 0xbf, 0x82, 0x3b, 0xa7, 0xac, 0xb1, 0x80, 0x2b, 0x00, 0x08,
                    0x00, 0x01, 0x0d, 0x96, 0x67, 0x1d, 0x42, 0xf3, 0x80, 0x22, 0x00, 0x1a, 0x43, 0x6f, 0x74, 0x75,
                    0x72, 0x6e, 0x2d, 0x34, 0x2e, 0x35, 0x2e, 0x30, 0x2e, 0x33, 0x20, 0x27, 0x64, 0x61, 0x6e, 0x20,
                    0x45, 0x69, 0x64, 0x65, 0x72, 0x27, 0x77, 0x75};

            STUNv2Message stunMessage = STUNv2Message.ParseSTUNMessage(stunResp, stunResp.Length);

            STUNv2Header stunHeader = stunMessage.Header;

            Console.WriteLine("Request type = " + stunHeader.MessageType + ".");
            Console.WriteLine("Length = " + stunHeader.MessageLength + ".");
            Console.WriteLine("Transaction ID = " + BitConverter.ToString(stunHeader.TransactionId) + ".");

            foreach (STUNv2Attribute attribute in stunMessage.Attributes)
            {
                if (attribute.AttributeType == STUNv2AttributeTypesEnum.MappedAddress)
                {
                    STUNv2AddressAttribute addressAttribute = new STUNv2AddressAttribute(attribute.Value);
                    Console.WriteLine(" " + attribute.AttributeType + " " + addressAttribute.Address + ":" + addressAttribute.Port + ".");

                    Assert.AreEqual("59.167.172.177", addressAttribute.Address.ToString());
                    Assert.AreEqual(49026, addressAttribute.Port);
                }
                else if (attribute.AttributeType == STUNv2AttributeTypesEnum.XORMappedAddress)
                {
                    STUNv2XORAddressAttribute xorAddressAttribute = new STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum.XORMappedAddress, attribute.Value);
                    Console.WriteLine(" " + attribute.AttributeType + " " + xorAddressAttribute.Address + ":" + xorAddressAttribute.Port + ".");

                    Assert.AreEqual("59.167.172.177", xorAddressAttribute.Address.ToString());
                    Assert.AreEqual(49026, xorAddressAttribute.Port);
                }

                else
                {
                    Console.WriteLine(" " + attribute.AttributeType + " " + attribute.Value + ".");
                }
            }

            Assert.AreEqual(STUNv2MessageTypesEnum.BindingSuccessResponse, stunHeader.MessageType);
        }
    }
}
