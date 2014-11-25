//-----------------------------------------------------------------------------
// Filename: IPSocket.cs
//
// Description: Converts special charatcers in XML to their safe equivalent.
//
// History:
// 22 jun 2005	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
using System.Net;
using System.Text.RegularExpressions;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Sys
{
	public class IPSocket
	{
		/// <summary>
		/// Returns an IPv4 end point from a socket address in 10.0.0.1:5060 format.
		/// </summary>>
		public static IPEndPoint GetIPEndPoint(string IPSocket)
		{
			if(IPSocket == null || IPSocket.Trim().Length == 0)
			{
				throw new ApplicationException("IPSocket cannot parse an IPEndPoint from an empty string.");
			}
			
			try
			{
				int colonIndex = IPSocket.IndexOf(":");

				if(colonIndex != -1)
				{
					string ipAddress = IPSocket.Substring(0, colonIndex).Trim();
					int port = Convert.ToInt32(IPSocket.Substring(colonIndex+1).Trim());
					IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);

					return endPoint;
				}
				else
				{
					return new IPEndPoint(IPAddress.Parse(IPSocket.Trim()), 0);
				}
			}
			catch(Exception excp)
			{
				throw new ApplicationException(excp.Message + "(" + IPSocket + ")");
			}
		}

		public static string GetSocketString(IPEndPoint endPoint)
		{
			if(endPoint != null)
			{
				return endPoint.Address.ToString() + ":" + endPoint.Port;
			}
			else
			{
				return null;
			}
		}

		public static void ParseSocketString(string socket, out string host, out int port)
		{
			try
			{
				host = null;
				port = 0;

				if(socket == null || socket.Trim().Length == 0)
				{
					return;
				}
				else
				{
					int colonIndex = socket.IndexOf(":");
					if(colonIndex == -1)
					{
                        host = socket.Trim();
					}
					else
					{
                        host = socket.Substring(0, colonIndex).Trim();
						try
						{
							port = Int32.Parse(socket.Substring(colonIndex+1).Trim());
						}
						catch{}
					}
				}
			}
			catch(Exception excp)
			{
				throw new ApplicationException("Exception ParseSocketString (" + socket + "). " + excp.Message);
			}
		}

		public static IPEndPoint ParseSocketString(string socket)
		{
			string ipAddress;
			int port;

			ParseSocketString(socket, out ipAddress, out port);

			return new IPEndPoint(IPAddress.Parse(ipAddress), port);
		}

        public static string ParseHostFromSocket(string socket)
        {
            string host = socket;

            if (socket != null && socket.Trim().Length > 0 && socket.IndexOf(':') != -1)
            {
                host = socket.Substring(0, socket.LastIndexOf(':')).Trim();
            }

            return host;
        }

        public static int ParsePortFromSocket(string socket)
        {
            int port = 0;

            if (socket != null && socket.Trim().Length > 0 && socket.IndexOf(':') != -1)
            {
                int colonIndex = socket.LastIndexOf(':');
                port = Convert.ToInt32(socket.Substring(colonIndex + 1).Trim());
            }

            return port;
        }

        public static bool IsIPSocket(string socket)
        {
            if(socket == null || socket.Trim().Length == 0)
            {
                return false;
            }
            else
            {
#if SILVERLIGHT
                return Regex.Match(socket, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d{1,5})$").Success;
#else
                return Regex.Match(socket, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d{1,5})$", RegexOptions.Compiled).Success;
#endif
            }
        }

        public static bool IsIPAddress(string socket) {
            if (socket == null || socket.Trim().Length == 0) {
                return false;
            }
            else {
#if SILVERLIGHT
                return Regex.Match(socket, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$").Success;
#else
                return Regex.Match(socket, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$", RegexOptions.Compiled).Success;
#endif
            }
        }

        /// <summary>
        /// Checks the Contact SIP URI host and if it is recognised as a private address it is replaced with the socket
        /// the SIP message was received on.
        /// 
        /// Private address space blocks RFC 1597.
        ///		10.0.0.0        -   10.255.255.255
        ///		172.16.0.0      -   172.31.255.255
        ///		192.168.0.0     -   192.168.255.255
        ///
        /// </summary>
        public static bool IsPrivateAddress(string host)
        {
            if (host != null && host.Trim().Length > 0)
            {
                if (host.StartsWith("127.0.0.1") ||
                    host.StartsWith("10.") ||
                    Regex.Match(host, @"^172\.1[6-9]\.").Success ||
                    Regex.Match(host, @"^172\.2\d\.").Success ||
                    host.StartsWith("172.30.") ||
                    host.StartsWith("172.31.") ||
                    host.StartsWith("192.168."))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

       	#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class IPSocketUnitTest
		{
			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				Assert.IsTrue(true, "True was false.");
			}

            [Test]
            public void ParsePortFromSocketTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                int port = IPSocket.ParsePortFromSocket("localhost:5060");
                Console.WriteLine("port=" + port);
                Assert.IsTrue(port == 5060, "The port was not parsed correctly.");
            }

            [Test]
            public void ParseHostFromSocketTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string host = IPSocket.ParseHostFromSocket("localhost:5060");
                Console.WriteLine("host=" + host);
                Assert.IsTrue(host == "localhost", "The host was not parsed correctly.");
            }

            [Test]
            public void Test172IPRangeIsPrivate()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                Assert.IsFalse(IPSocket.IsPrivateAddress("172.15.1.1"), "Public IP address was mistakenly identified as private.");
                Assert.IsTrue(IPSocket.IsPrivateAddress("172.16.1.1"), "Private IP address was not correctly identified.");

                Console.WriteLine("-----------------------------------------");
            }
        }

        #endif

        #endregion
    }
}
