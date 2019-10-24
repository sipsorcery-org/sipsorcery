//-----------------------------------------------------------------------------
// Filename: RTPPacket.cs
//
// Description: Encapsulation of an RTP packet.
// 
// History:
// 24 May 2005	Aaron Clauson	Created.
// 11 Aug 2019  Aaron Clauson   Added full license header.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2005-2019 Aaron Clauson (aaron@sipsorcery.com), Montreux, Switzerland (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd 
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

namespace SIPSorcery.Net
{
	public class RTPPacket
	{
		public RTPHeader Header;
		public byte[] Payload;

		public RTPPacket()
		{
			Header = new RTPHeader();
		}

		public RTPPacket(int payloadSize)
		{
			Header = new RTPHeader();
			Payload = new byte[payloadSize];
		}
		
		public RTPPacket(byte[] packet)
		{
			Header = new RTPHeader(packet);
            Payload = new byte[packet.Length - Header.Length];
            Array.Copy(packet, Header.Length, Payload, 0, Payload.Length);
		}

		public byte[] GetBytes()
		{
			byte[] header = Header.GetBytes();
            byte[] packet = new byte[header.Length + Payload.Length];
			
			Array.Copy(header, packet, header.Length);
			Array.Copy(Payload, 0, packet, header.Length, Payload.Length);

			return packet;
		}

		private byte[] GetNullPayload(int numBytes)
		{
			byte[] payload = new byte[numBytes]; 
			
			for(int byteCount=0; byteCount<numBytes; byteCount++)
			{
				payload[byteCount] = 0xff;
			}

			return payload;
		}
	}
}
