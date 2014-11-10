//-----------------------------------------------------------------------------
// Filename: SDPICECandidate.cs
//
// Description: The SDP attribute that gets used for specifying ICE candidates.
//
// Example: a=candidate:2675262800 1 udp 2122194687 10.1.1.2 49890 typ host generation 0
//
// History:
// 10 Nov 2014	Aaron Clauson	Created.
//
// License: 
// Aaron Clauson
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.Net
{
    public class SDPICECandidate
    {
        public const string m_CRLF = "\r\n";

        public string Transport;
        public string NetworkAddress;
        public int Port;

        public SDPICECandidate()
        { }

        public static SDPICECandidate Parse(string candidateLine)
        {
            SDPICECandidate candidate = new SDPICECandidate();
            string[] candidateFields = candidateLine.Trim().Split(' ');
            candidate.Transport = candidateFields[2];
            candidate.NetworkAddress = candidateFields[4];
            candidate.Port = Convert.ToInt32(candidateFields[5]);
            return candidate;
        }

        public override string ToString()
        {
            return null; // "c=" + ConnectionNetworkType + " " + ConnectionAddressType + " " + ConnectionAddress + m_CRLF;
        }
    }
}
