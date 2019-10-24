//-----------------------------------------------------------------------------
// Filename: TurnServer.cs
//
// Description: Encapsulates the connection details for a TURN/STUN server.
//
// Author(s):
// Aaron Clauson
// 
// History:
// 26 Feb 2016	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;

namespace SIPSorcery.Net
{
    public class TurnServer
    {
        public IPEndPoint ServerEndPoint;
        public string Username;
        public string Password;
        public string Realm;
        public string Nonce;
        public int AuthorisationAttempts;
    }
}
