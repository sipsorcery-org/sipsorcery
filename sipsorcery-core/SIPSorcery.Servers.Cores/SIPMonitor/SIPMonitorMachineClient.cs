// ============================================================================
// FileName: SIPMonitorMachineClient.cs
//
// Description:
// This class describes a machine client connection to the SIP Monitor server.
// This type of connection will have a machine on the other end that is configured
// to receive event notifications to initiate updates on a user interface.
//
// Author(s):
// Aaron Clauson
//
// History:
// 14 Nov 2008	Aaron Clauson	Created.
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
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{	
    /// <summary>
    /// This class describes a machine client connection to the SIP Monitor server.
    /// This type of connection will have a machine on the other end that is configured
    /// to receive event notifications to initiate updates on a user interface.
    /// </summary>
    public class SIPMonitorMachineClient
	{
        public Guid MachineId = Guid.NewGuid();
        public DateTime Created = DateTime.UtcNow;

        public Socket ClientSocket = null;         // If non null indicates the monitor client socket is connected.
        public bool Remove = false;                // Set to true when the proxy client should be deactivated.
        public string Owner;                        // If the user successfully logs in then their username will be set so that they can receive specific notifications.
        public string RemoteEndPoint;              // Set to the remote socket once connected.

        public SIPMonitorMachineClient(Socket socket)
		{
			ClientSocket = socket;
            RemoteEndPoint = ClientSocket.RemoteEndPoint.ToString();
		}
	}
}
