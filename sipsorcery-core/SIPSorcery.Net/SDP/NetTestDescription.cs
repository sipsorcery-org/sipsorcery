//-----------------------------------------------------------------------------
// Filename: NetTestDescription.cs
//
// Description: Descriptive fields for a network test
//
// NetTestDescription Payload:
// 0                   1                   2                   3
// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// |                Client Socket String                           |
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// |                Server Socket String                           |
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// 
// History:
// 09 Jan 2007	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using log4net;

namespace SIPSorcery.Net
{
    public class NetTestDescription
    {
        private const char DELIMITER_CHARACTER = '\a';

        public static NetTestDescription Empty = new NetTestDescription(Guid.Empty, null, null, null, null, null, null);

        public Guid TestId;
        public string ClientSocket;
        public string ClientISP;
        public string ServerSocket;
        public string ServerISP;
        public string Username;
        public string Comment;

        public NetTestDescription()
        { }

        public NetTestDescription(Guid testId, string clientSocket, string clientISP, string serverSocket, string serverISP, string username, string comment)
        {
            TestId = testId;
            ClientSocket = clientSocket;
            ClientISP = clientISP;
            ServerSocket = serverSocket;
            ServerISP = serverISP;
            Username = username;
            Comment = comment;
        }

        public NetTestDescription(byte[] bytes)
        {
            string netTestString = Encoding.ASCII.GetString(bytes);

            string[] netTestFields = netTestString.Split(DELIMITER_CHARACTER);

            TestId = new Guid(netTestFields[0]);
            ClientSocket = netTestFields[1];
            ServerSocket = netTestFields[2];
            ClientISP = netTestFields[3];
            ServerISP = netTestFields[4];
            Username = netTestFields[5];
            Comment = netTestFields[6];
        }

        public byte[] GetBytes()
        {
            string netTestString = TestId.ToString() + DELIMITER_CHARACTER + ClientSocket + DELIMITER_CHARACTER + ServerSocket + DELIMITER_CHARACTER + 
                ClientISP + DELIMITER_CHARACTER + ServerISP + DELIMITER_CHARACTER + Username + DELIMITER_CHARACTER + Comment;

            return Encoding.ASCII.GetBytes(netTestString);
        }
    }
}
