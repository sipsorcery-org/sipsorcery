//-----------------------------------------------------------------------------
// Filename: SDPICECandidate.cs
//
// Description: The SDP attribute that gets used for specifying ICE candidates.
//
// Examples: 
// a=candidate:2675262800 1 udp 2122194687 10.1.1.2 49890 typ host generation 0
// a=candidate:2786641038 1 udp 2122260223 192.168.33.125 59754 typ host generation 0
// a=candidate:1788214045 1 udp 1686052607 dd.ee.ff.gg 59754 typ srflx raddr 192.168.33.125 rport 59754 generation 0
// a=candidate:2234295925 1 udp 41885439 aa.bb.cc.dd 61480 typ relay raddr dd.ee.ff.gg rport 59754 generation 0
//
// History:
// 10 Nov 2014	Aaron Clauson	Created.
//
// License: 
// Aaron Clauson
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
    public enum IceCandidateTypesEnum
    {
        Unknown = 0,
        host = 1,
        srflx = 2,
        relay = 3
    }

    public class SDPICECandidate
    {
        public const string m_CRLF = "\r\n";
        public const string REMOTE_ADDRESS_KEY = "raddr";
        public const string REMOTE_PORT_KEY = "rport";

        public string Transport;
        public string NetworkAddress;
        public int Port;
        public IceCandidateTypesEnum CandidateType;
        public string RemoteAddress;
        public int RemotePort;

        public SDPICECandidate()
        { }

        public static SDPICECandidate Parse(string candidateLine)
        {
            SDPICECandidate candidate = new SDPICECandidate();
            string[] candidateFields = candidateLine.Trim().Split(' ');
            candidate.Transport = candidateFields[2];
            candidate.NetworkAddress = candidateFields[4];
            candidate.Port = Convert.ToInt32(candidateFields[5]);
            Enum.TryParse<IceCandidateTypesEnum>(candidateFields[7], out candidate.CandidateType);

            if(candidateFields.Length > 8 && candidateFields[8] == REMOTE_ADDRESS_KEY)
            {
                candidate.RemoteAddress = candidateFields[9];
            }

            if (candidateFields.Length > 10 && candidateFields[10] == REMOTE_PORT_KEY)
            {
                candidate.RemotePort = Convert.ToInt32(candidateFields[11]);
            }
            
            return candidate; 
        }

        public override string ToString()
        {
            return null; // "c=" + ConnectionNetworkType + " " + ConnectionAddressType + " " + ConnectionAddress + m_CRLF;
        }
    }
}
