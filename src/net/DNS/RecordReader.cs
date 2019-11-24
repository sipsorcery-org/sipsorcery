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
// 14 Oct 2019	Aaron Clauson   Added based on https://www.codeproject.com/Articles/23673/DNS-NET-Resolver-C.
//
// License:
// The Code Project Open License (CPOL) https://www.codeproject.com/info/cpol10.aspx
// ============================================================================

using System;
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

        public int Length
        {
            get
            {
                if (m_Data == null)
                    return 0;
                else
                    return m_Data.Length;
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

        public char ReadChar()
        {
            return (char)ReadByte();
        }

        public UInt16 ReadUInt16()
        {
            return (UInt16)(ReadByte() << 8 | ReadByte());
        }

        public UInt16 ReadUInt16(int offset)
        {
            m_Position += offset;
            return ReadUInt16();
        }

        public UInt32 ReadUInt32()
        {
            return (UInt32)(ReadUInt16() << 16 | ReadUInt16());
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
            StringBuilder str = new StringBuilder();
            for (int intI = 0; intI < length; intI++)
                str.Append(ReadChar());
            return str.ToString();
        }

        // changed 28 augustus 2008
        public byte[] ReadBytes(int intLength)
        {
            byte[] list = new byte[intLength];
            for (int intI = 0; intI < intLength; intI++)
                list[intI] = ReadByte();
            return list;
        }

        public Record ReadRecord(DnsType type, int Length)
        {
            switch (type)
            {
                case DnsType.A:
                    return new RecordA(this);
                case DnsType.NS:
                    return new RecordNS(this);
                //case DnsType.MD:
                //	return new RecordMD(this);
                //case DnsType.MF:
                //	return new RecordMF(this);
                case DnsType.CNAME:
                    return new RecordCNAME(this);
                case DnsType.SOA:
                    return new RecordSOA(this);
                //case DnsType.MB:
                //	return new RecordMB(this);
                //case DnsType.MG:
                //	return new RecordMG(this);
                //case DnsType.MR:
                //	return new RecordMR(this);
                case DnsType.NULL:
                    return new RecordNULL(this);
                //case DnsType.WKS:
                //	return new RecordWKS(this);
                case DnsType.PTR:
                    return new RecordPTR(this);
                //case DnsType.HINFO:
                //	return new RecordHINFO(this);
                //case DnsType.MINFO:
                //	return new RecordMINFO(this);
                case DnsType.MX:
                    return new RecordMX(this);
                //case DnsType.TXT:
                //	return new RecordTXT(this, Length);
                //case DnsType.RP:
                //	return new RecordRP(this);
                //case DnsType.AFSDB:
                //	return new RecordAFSDB(this);
                //case DnsType.X25:
                //	return new RecordX25(this);
                //case DnsType.ISDN:
                //	return new RecordISDN(this);
                //case DnsType.RT:
                //	return new RecordRT(this);
                //case DnsType.NSAP:
                //	return new RecordNSAP(this);
                //case DnsType.NSAPPTR:
                //	return new RecordNSAPPTR(this);
                //case DnsType.SIG:
                //	return new RecordSIG(this);
                //case DnsType.KEY:
                //	return new RecordKEY(this);
                //case DnsType.PX:
                //	return new RecordPX(this);
                //case DnsType.GPOS:
                //	return new RecordGPOS(this);
                case DnsType.AAAA:
                    return new RecordAAAA(this);
                //case DnsType.LOC:
                //	return new RecordLOC(this);
                //case DnsType.NXT:
                //	return new RecordNXT(this);
                //case DnsType.EID:
                //	return new RecordEID(this);
                //case DnsType.NIMLOC:
                //	return new RecordNIMLOC(this);
                case DnsType.SRV:
                    return new RecordSRV(this);
                //case DnsType.ATMA:
                //	return new RecordATMA(this);
                case DnsType.NAPTR:
                    return new RecordNAPTR(this);
                //case DnsType.KX:
                //	return new RecordKX(this);
                //case DnsType.CERT:
                //	return new RecordCERT(this);
                //case DnsType.A6:
                //	return new RecordA6(this);
                //case DnsType.DNAME:
                //	return new RecordDNAME(this);
                //case DnsType.SINK:
                //	return new RecordSINK(this);
                //case DnsType.OPT:
                //	return new RecordOPT(this);
                //case DnsType.APL:
                //	return new RecordAPL(this);
                //case DnsType.DS:
                //	return new RecordDS(this);
                //case DnsType.SSHFP:
                //	return new RecordSSHFP(this);
                //case DnsType.IPSECKEY:
                //	return new RecordIPSECKEY(this);
                //case DnsType.RRSIG:
                //	return new RecordRRSIG(this);
                //case DnsType.NSEC:
                //	return new RecordNSEC(this);
                //case DnsType.DNSKEY:
                //	return new RecordDNSKEY(this);
                //case DnsType.DHCID:
                //	return new RecordDHCID(this);
                //case DnsType.NSEC3:
                //	return new RecordNSEC3(this);
                //case DnsType.NSEC3PARAM:
                //	return new RecordNSEC3PARAM(this);
                //case DnsType.HIP:
                //	return new RecordHIP(this);
                //case DnsType.SPF:
                //	return new RecordSPF(this);
                //case DnsType.UINFO:
                //	return new RecordUINFO(this);
                //case DnsType.UID:
                //	return new RecordUID(this);
                //case DnsType.GID:
                //	return new RecordGID(this);
                //case DnsType.UNSPEC:
                //	return new RecordUNSPEC(this);
                //case DnsType.TKEY:
                //	return new RecordTKEY(this);
                //case DnsType.TSIG:
                //	return new RecordTSIG(this);
                default:
                    return new RecordUnknown(this);
            }
        }

    }
}
