// ============================================================================
// FileName: SIPMonitorControlClient.cs
//
// Description:
// This class describes a administrative control client connection to the SIP Monitor server.
// This type of connection will have a user on the other end and can receive control commands
// and set filters as well as receiv raw events.
//
// Author(s):
// Aaron Clauson
//
// History:
// 01 May 2006	Aaron Clauson	Created.
// 14 Nov 2008  Aaron Clauson   Refactored from existing code.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.SIP.App;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{	
    /// <summary>
    /// This class describes a administrative control client connection to the SIP Monitor server.
    /// This type of connection will have a user on the other end and can receive control commands
    /// and set filters as well as receiv raw events.
    /// </summary>
    public class SIPMonitorControlClient
	{
        public Guid ClientId = Guid.NewGuid();
        public DateTime Created = DateTime.UtcNow;
        public SIPMonitorFilter Filter;

        public Socket ClientSocket = null;         // If non null indicates the proxy client is a telnet socket.

        public string Filename = null;             // If non null indicates the proxy client is a log file.
        public int LogDurationMinutes = 0;
        public Stream FileStream = null;

        public bool Remove = false;                // Set to true when the proxy client should be deactivated.

        public string Username;                     // The authenticated username for the monitor connection.

        public SIPMonitorControlClient(Socket socket, SIPMonitorFilter filter, string username)
		{
			ClientSocket = socket;
			Filter = filter;
            Username = username;
		}

        public SIPMonitorControlClient(string filename, SIPMonitorFilter filter, string username)
        {
            Filename = filename;
            LogDurationMinutes = filter.FileLogDuration;
            Filter = filter;

            FileStream = new FileStream(filename, FileMode.Create);
            string logStartedMessage = "Log started at " + Created.ToString("dd MMM yyyy HH:mm:ss") + " requested duration " + LogDurationMinutes + " regex " + filter.RegexFilter + ".\r\n";
            FileStream.Write(Encoding.ASCII.GetBytes(logStartedMessage), 0, logStartedMessage.Length);
            FileStream.Flush();
            Username = username;
        }
	}
}
