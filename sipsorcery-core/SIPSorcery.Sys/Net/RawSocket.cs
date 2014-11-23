//-----------------------------------------------------------------------------
// Filename: RawSocket.cs
//
// Description: Allows the sending of hand crafted IP packets.
//
// History:
// 01 Feb 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Sys
{   
    /// <summary>
    ///     0                   1                   2                   3   
    /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |Version|  IHL  |Type of Service|          Total Length         |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |         Identification        |Flags|      Fragment Offset    |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |  Time to Live |    Protocol   |         Header Checksum       |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |                       Source Address                          |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |                    Destination Address                        |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |                    Options                    |    Padding    |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///
    /// </summary>
    public class IPv4Header
    {
        public const int MIN_HEADER_LEN = 5;            // Minimum length if header in 32 bit words.
        public const int IP_VERSION = 4;

        public int Version = IP_VERSION;
        public int HeaderLength = MIN_HEADER_LEN;       // Length of header in 32 bit words.
        public int TypeOfService;
        public int Length = MIN_HEADER_LEN;             // Total length of the IP packet in octets.
        public int Id;
        public int TTL = 255;
        public ProtocolType Protocol;                  // 1 = ICMP, 6 = TCP, 17 = UDP.
        public IPAddress SourceAddress;
        public IPAddress DestinationAddress;
        
        // Fragmentation flags. Bit 0=0, Bit 1=DF, Bit 2=MF
        public int DF = 1;              // 0 = May fragment, 1 = Don't fragment.
        public int MF = 0;              // 0 = Last fragment, 1 = More fragments.
        public int FragmentOffset;      // Indiciates where in the datagram the fragment belongs.

        public IPv4Header(ProtocolType protocol, int id, IPAddress sourceAddress, IPAddress dstAddress)
        {
            Protocol = protocol;
            Id = id;
            SourceAddress = sourceAddress;
            DestinationAddress = dstAddress;
        }
        
        public byte[] GetBytes()
        {
            byte[] header = new byte[HeaderLength * 4];

            header[0] = (byte)((Version << 4) + HeaderLength);
            header[1] = (byte)TypeOfService;
            header[2] = (byte)(Length >> 8);
            header[3] = (byte)Length;
            header[4] = (byte)(Id >> 8);
            header[5] = (byte)Id;
            header[6] = (byte)(DF * 64 + MF * 32  + (FragmentOffset >> 8));
            header[7] = (byte)FragmentOffset;
            header[8] = (byte)TTL;
            header[9] = (byte)Protocol;
            
            Buffer.BlockCopy(SourceAddress.GetAddressBytes(), 0, header, 12, 4);
            Buffer.BlockCopy(DestinationAddress.GetAddressBytes(), 0, header, 16, 4);

            UInt16 checksum = GetChecksum(header);
            header[10] = (byte)(checksum >> 8);
            header[11] = (byte)checksum;

            return header;
        }

        public UInt16 GetChecksum(byte[] buffer)
        {
            int checksum = 0;
            for(int index=0; index<buffer.Length-2; index=index+2)
            {
                checksum += (buffer[index] << 4) + buffer[index + 1];
            }

            //checksum = (checksum >> 16) + (checksum & 0xffff);
            //cksum += (checksum >> 16);
            return (UInt16) ~checksum;
        }
    }

    /// <summary>
    ///  0      7 8     15 16    23 24    31  
    ///  +--------+--------+--------+--------+ 
    ///  |     Source      |   Destination   | 
    ///  |      Port       |      Port       | 
    ///  +--------+--------+--------+--------+ 
    ///  |                 |                 | 
    ///  |     Length      |    Checksum     | 
    ///  +--------+--------+--------+--------+ 
    ///  |                                     
    ///  |          data octets ...            
    ///  +---------------- ...            
    ///  
    /// </summary>
    public class UDPPacket
    {
        public int SourcePort;
        public int DestinationPort;
        public byte[] Payload;

        public UDPPacket(int sourcePort, int destinationPort, byte[] payload)
        {
            SourcePort = sourcePort;
            DestinationPort = destinationPort;
            Payload = payload;
        }

        public byte[] GetBytes()
        {
            byte[] packet = new byte[8 + Payload.Length];

            packet[0] = (byte)(SourcePort >> 8);
            packet[1] = (byte)SourcePort;
            packet[2] = (byte)(DestinationPort >> 8);
            packet[3] = (byte)DestinationPort;
            packet[4] = (byte)(packet.Length >> 8);
            packet[5] = (byte)packet.Length;

            Buffer.BlockCopy(Payload, 0, packet, 8, Payload.Length);

            return packet;
        }
    }

    public class IPv4Packet
    {
        public IPv4Header Header;
        public byte[] Payload;

        public IPv4Packet(IPv4Header header, byte[] payload)
        {
            Header = header;
            Payload = payload;

            Header.Length = Header.HeaderLength * 2 + Payload.Length / 2;
        }

        public byte[] GetBytes()
        {
            byte[] packet = new byte[Header.Length * 2];
            Buffer.BlockCopy(Header.GetBytes(), 0, packet, 0, Header.HeaderLength * 4);
            Buffer.BlockCopy(Payload, 0, packet, Header.HeaderLength * 4, Payload.Length);

            return packet;
        }
    }

	public class RawSocket
	{
        public void SendSpoofedPacket(byte[] payload, IPEndPoint sourceSocket, IPEndPoint destinationSocket)
        {
           
        }

       	#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class IPSocketUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				Assert.IsTrue(true, "True was false.");
			}

            [Test]
            public void IPHeaderConstructionUnitTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                IPv4Header header = new IPv4Header(ProtocolType.Udp, 4567, IPAddress.Parse("194.213.29.54"), IPAddress.Parse("194.213.29.54"));
                byte[] headerData = header.GetBytes();

                int count = 0;
                foreach (byte headerByte in headerData)
                {
                    Console.Write("0x{0,-2:x} ", headerByte);
                    count++;
                    if (count % 4 == 0)
                    {
                        Console.Write("\n");
                    }
                }

                Console.WriteLine();

                Assert.IsTrue(true, "True was false.");
            }

            [Test]
            [Ignore("Will only work on WinXP or where raw socket privileges have been explicitly granted.")]
            public void PreconstructedIPPacketSendUnitTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                byte[] ipPacket = new byte[] {
                     // IP Header.
                    0x45, 0x30, 0x0, 0x4, 
                    0xe4, 0x29, 0x0, 0x0, 
                    0x80, 0x0, 0x62, 0x3d, 
                    0x0a, 0x0, 0x0, 0x64,
                    0xc2, 0xd5, 0x1d, 0x36,
                    // Data.
                    0x0, 0x0, 0x0, 0x0
                };

                Socket rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                rawSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, 1);

                try
                {
                    rawSocket.SendTo(ipPacket, new IPEndPoint(IPAddress.Parse("194.213.29.54"), 5060));
                }
                catch (SocketException sockExcp)
                {
                    Console.WriteLine("Socket exception error code= " + sockExcp.ErrorCode + ". " + sockExcp.Message);
                    throw sockExcp;
                }

                rawSocket.Shutdown(SocketShutdown.Both);
                rawSocket.Close();

                Assert.IsTrue(true, "True was false.");
            }

            [Test]
            [Ignore("Will only work on WinXP or where raw socket privileges have been explicitly granted.")]
            public void IPEmptyPacketSendUnitTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                UDPPacket udpPacket = new UDPPacket(4001, 4001, new byte[] { 0x1, 0x2, 0x3, 0x4 });
                IPv4Header header = new IPv4Header(ProtocolType.Udp, 7890, IPAddress.Parse("194.213.29.54"), IPAddress.Parse("194.213.29.54"));
                byte[] headerData = header.GetBytes();

                foreach (byte headerByte in headerData)
                {
                    Console.Write("0x{0:x} ", headerByte);
                }

                Console.WriteLine();

                Socket rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                //rawSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, 1);
                rawSocket.SendTo(headerData, new IPEndPoint(IPAddress.Parse("194.213.29.54"), 5060));

                rawSocket.Shutdown(SocketShutdown.Both);
                rawSocket.Close();

                Assert.IsTrue(true, "True was false.");
            }
           
        }

        #endif

        #endregion
    }
}
