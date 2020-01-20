//-----------------------------------------------------------------------------
// Filename: RawSocket.cs
//
// Description: Allows the sending of hand crafted IP packets.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 01 Feb 2008	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;

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
        public int FragmentOffset;      // Indicates where in the datagram the fragment belongs.

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
            header[6] = (byte)(DF * 64 + MF * 32 + (FragmentOffset >> 8));
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
            for (int index = 0; index < buffer.Length - 2; index = index + 2)
            {
                checksum += (buffer[index] << 4) + buffer[index + 1];
            }

            //checksum = (checksum >> 16) + (checksum & 0xffff);
            //cksum += (checksum >> 16);
            return (UInt16)~checksum;
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
        /// <summary>
        /// The goal of this method was to send a dummy packet through a NAT gateway in an attempt to create a rule for 
        /// incoming packets. There are better ways to do this now, UPNP etc.
        /// </summary>
        public void SendSpoofedPacket(byte[] payload, IPEndPoint sourceSocket, IPEndPoint destinationSocket)
        {
            throw new NotImplementedException();
        }
    }
}
