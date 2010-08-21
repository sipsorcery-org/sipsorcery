// ============================================================================
// FileName: RecordReader.cs
//
// Description:
// 
//
// Author(s):
// Alphons van der Heijden
//
// History:
// 28 Mar 2008	Aaron Clauson   Added to sipwitch code base based on http://www.codeproject.com/KB/library/DNS.NET_Resolver.aspx.
//
// License:
// http://www.opensource.org/licenses/gpl-license.php
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace Heijden.DNS
{
	public class RecordReader
	{
		private byte[] m_Data;
		private int m_Position;
		public RecordReader(byte[] data)
		{
			m_Data = data;
			m_Position = 0;
		}

		public int Position
		{
			get
			{
				return m_Position;
			}
			set
			{
				m_Position = value;
			}
		}

		public RecordReader(byte[] data, int Position)
		{
			m_Data = data;
			m_Position = Position;
		}


		public byte ReadByte()
		{
			if (m_Position >= m_Data.Length)
				return 0;
			else
				return m_Data[m_Position++];
		}

        public string ReadCharToStr()
        {
            return (string)ReadByte().ToString();
        }

		public char ReadChar()
		{
			return (char)ReadByte();
		}

		public ushort ReadShort()
		{
			return (ushort)(ReadByte() << 8 | ReadByte());
		}

		public ushort ReadShort(int offset)
		{
			m_Position += offset;
			return ReadShort();
		}

		public int ReadInt()
		{
			return ReadShort()<<16 | ReadShort();
		}

		public string ReadDomainName()
		{
			StringBuilder name = new StringBuilder();
			int length = 0;

			// get  the length of the first label
			while ((length = ReadByte()) != 0)
			{
				// top 2 bits set denotes domain name compression and to reference elsewhere
				if ((length & 0xc0) == 0xc0)
				{
					// work out the existing domain name, copy this pointer
					RecordReader newRecordReader = new RecordReader(m_Data, (length & 0x3f) << 8 | ReadByte());

					name.Append(newRecordReader.ReadDomainName());
					return name.ToString();
				}

				// if not using compression, copy a char at a time to the domain name
				while (length > 0)
				{
					name.Append(ReadChar());
					length--;
				}
				name.Append('.');
			}
			if (name.Length == 0)
				return ".";
			else
				return name.ToString();
		}

		public string ReadString()
		{
			short length = this.ReadByte();

			StringBuilder name = new StringBuilder();
			for(int intI=0;intI<length;intI++)
				name.Append(ReadChar());
			return name.ToString();
		}

		public byte[] ReadBytes(int intLength)
		{
			List<byte> list = new List<byte>();
			for(int intI=0;intI<intLength;intI++)
				list.Add(ReadByte());
			return list.ToArray();
		}

        public Record ReadRecord(DNSType type)
		{
            switch (type)
			{
                case DNSType.A:
					return new RecordA(this);
                case DNSType.NS:
					return new RecordNS(this);
                case DNSType.CNAME:
					return new RecordCNAME(this);
                case DNSType.SOA:
					return new RecordSOA(this);
                case DNSType.PTR:
					return new RecordPTR(this);
                case DNSType.HINFO:
					return new RecordHINFO(this);
                case DNSType.MINFO:
					return new RecordMINFO(this);
                case DNSType.MX:
					return new RecordMX(this);
                case DNSType.TXT:
					return new RecordTXT(this);

				// old stuff
                case DNSType.MD:
					return new RecordMD(this);
                case DNSType.MF:
					return new RecordMF(this);
                case DNSType.MB:
					return new RecordMB(this);
                case DNSType.MG:
					return new RecordMG(this);
                case DNSType.MR:
					return new RecordMR(this);
                case DNSType.NULL:
					return new RecordNULL(this);
                case DNSType.WKS:
					return new RecordWKS(this);

				// RFC 1886 IPV6
                case DNSType.AAAA:
					return new RecordAAAA(this);

                // ENUM Lookups
                case DNSType.NAPTR:
                    return new RecordNAPTR(this);

                // SRV Lookups
                case DNSType.SRV:
                    return new RecordSRV(this);

				default:
					return new RecordUnknown(this);
			}
		}

	}
}
