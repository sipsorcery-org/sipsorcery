// ============================================================================
// FileName: FastAGIRequest.cs
//
// Description:
// Encapsualtion of a new FastAGIRequest from Asterisk.
//
// Author(s):
// Aaron Clauson
//
// History:
// 24 Jan 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, London, UK
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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Asterisk.FastAGI
{
	public class FastAGIRequest
	{
        private ILog logger = LogManager.GetLogger("fastagi");
	
		public string Request;
		public string Channel;
		public string Language;
		public string Type;
		public string UniqueId;
		public string CallerId;
		public string Dnid;
		public string Rdnis;
		public string Context;
		public string Extension;
		public string Priority;
		public string Enhanced;
		public string AccountCode;

		public FastAGIRequest()
		{}

        public AppTimeMetric Run(Socket agiSocket, string request)
		{
            try
            {
                IPAddress callingServerAddress = ((IPEndPoint)agiSocket.RemoteEndPoint).Address;

                string[] variables = request.Split('\n');
                foreach (string variable in variables)
                {
                    ProcessAGIVariable(variable);
                }

                byte[] buffer = new byte[1024];

                agiSocket.Send(Encoding.ASCII.GetBytes("EXEC PLAYBACK tt-monkeys\n"));
                int bytesRead = agiSocket.Receive(buffer);
                logger.Debug(ASCIIEncoding.ASCII.GetString(buffer, 0, bytesRead));

                agiSocket.Send(Encoding.ASCII.GetBytes("EXEC PLAYBACK tt-weasels\n"));
                bytesRead = agiSocket.Receive(buffer);
                logger.Debug(ASCIIEncoding.ASCII.GetString(buffer, 0, bytesRead));

                agiSocket.Send(Encoding.ASCII.GetBytes("TRANSFER SIP/aaron@sipsorcery.com\n"));
                bytesRead = agiSocket.Receive(buffer);
                logger.Debug(ASCIIEncoding.ASCII.GetString(buffer, 0, bytesRead));

                return new AppTimeMetric();
            }
            catch (Exception excp)
            {
                logger.Error("Exception FastAGIRequest Run. " + excp.Message);
                throw ;
            }
		}

		private bool ProcessAGIVariable(string line)
		{
			int colon = line.IndexOf(':');
			if (colon < 0)
			{
				// End of initial variables.
				return false;
			}

			// Asterisk formats the variables as "name: value".
			string name = line.Substring(0, colon);
			string value = line.Substring(colon + 1).Trim();
			switch (name)
			{
				case "agi_request": this.Request = value; break;
				case "agi_channel": this.Channel = value; break;
				case "agi_language": this.Language = value; break;
				case "agi_type": this.Type = value; break;
				case "agi_uniqueid": this.UniqueId = value; break;
				case "agi_callerid": this.CallerId = value; break;
				case "agi_dnid": this.Dnid = value; break;
				case "agi_rdnis": this.Rdnis = value; break;
				case "agi_context": this.Context = value; break;
				case "agi_extension": this.Extension = value; break;
				case "agi_priority": this.Priority = value; break;
				case "agi_enhanced": this.Enhanced = value; break;
				case "agi_accountcode": this.AccountCode = value; break;
			}
			return true;
		}
	}
}
