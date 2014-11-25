// ============================================================================
// FileName: STUNAppState.cs
//
// Description:
//  Holds application configuration information.
//
// Author(s):
//	Aaron Clauson
//
// History:
// 27 Dec 2006	Aaron Clauson	Created.
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
// ============================================================================

using System;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using log4net;

namespace SIPSorcery.Net
{
	/// <summary>
	/// This class maintains static application configuration settings that can be used by all classes within
	/// the AppDomain. This class is the one stop shop for retrieving or accessing application configuration settings.
	/// </summary>
	public class STUNAppState
	{
        public const int DEFAULT_STUN_PORT = 3478;

		public const string LOGGER_NAME = "stun";

		public static ILog logger = null;

		static STUNAppState()
		{
			try
			{
				// Configure logging.
				logger = log4net.LogManager.GetLogger(LOGGER_NAME);
			}
			catch(Exception excp)
			{
				Console.WriteLine("Exception STUNAppState: " + excp.Message);
			}
		}
	}

    public class Utility
    {
        public static UInt16 ReverseEndian(UInt16 val)
        {
            return Convert.ToUInt16(val << 8 & 0xff00 | (val >> 8));
        }

        public static UInt32 ReverseEndian(UInt32 val)
        {
            return Convert.ToUInt32((val << 24 & 0xff000000) | (val << 8 & 0x00ff0000) | (val >> 8 & 0xff00) | (val >> 24));
        }

        public static string PrintBuffer(byte[] buffer)
        {
            string bufferStr = null;

            for (int index = 0; index < buffer.Length; index++)
            {
                string byteStr = buffer[index].ToString("X");

                if (byteStr.Length == 1)
                {
                    bufferStr += "0" + byteStr;
                }
                else
                {
                    bufferStr += byteStr;
                }

                if ((index + 1) % 4 == 0)
                {
                    bufferStr += "\n";
                }
                else
                {
                    bufferStr += " | ";
                }
            }

            return bufferStr;
        }
    }
}