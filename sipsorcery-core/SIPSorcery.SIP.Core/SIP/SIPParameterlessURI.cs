//-----------------------------------------------------------------------------
// Filename: SIPParameterlessURI.cs
//
// Description: SIP URI that discards any parameters or headers. This type of URI is used for
// SIP Registrar address-of-record bindings.
//
// History:
// 15 Dec 2006	Aaron Clauson	Created.
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
using System.Collections;
using System.Collections.Specialized;
using System.Net;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
    [DataContract]
    public class SIPParameterlessURI 
	{
        private static ILog logger = AssemblyState.logger;

        private SIPURI m_uri;

        [DataMember]
        public SIPURI URI
        {
            get { return m_uri; }
            set
            {
                m_uri = value;
                m_uri.Parameters.RemoveAll();
                m_uri.Headers.RemoveAll();
            }
        }

        public SIPSchemesEnum Scheme
        {
            get { return m_uri.Scheme; }
            set { m_uri.Scheme = value; }
        }

        public string User
        {
            get { return m_uri.User; }
            set { m_uri.User = value; }
        }

        public string Host
        {
            get { return m_uri.Host; }
            set { m_uri.Host = value; }
        }
        
        private SIPParameterlessURI()
		{}

		public SIPParameterlessURI(SIPURI sipURI)
		{
            m_uri = sipURI;
		}

        public SIPParameterlessURI(SIPSchemesEnum scheme, string host, string user)
        {
            m_uri = new SIPURI(user, host, null);
            m_uri.Scheme = scheme;
        }

		public static SIPParameterlessURI ParseSIPParamterlessURI(string uri)
		{
			SIPURI sipURI = SIPURI.ParseSIPURI(uri);
            sipURI.Parameters.RemoveAll();
            sipURI.Headers.RemoveAll();

            return new SIPParameterlessURI(sipURI);
		}
	
		public new string ToString()
		{
			try
			{
                string uriStr = m_uri.Scheme.ToString() + SIPURI.SCHEME_ADDR_SEPARATOR;

                uriStr = (User != null) ? uriStr + User + SIPURI.USER_HOST_SEPARATOR + Host : uriStr + Host;

				return uriStr;
			}
			catch(Exception excp)
			{
				logger.Error("Exception SIPParameterlessURI ToString. " + excp.Message);
				throw excp;
			}
		}

        public static bool AreEqual(SIPParameterlessURI uri1, SIPParameterlessURI uri2)
        {
            if (uri1 == null || uri2 == null)
            {
                return false;
            }
            else if (uri1.m_uri.CanonicalAddress == uri2.m_uri.CanonicalAddress)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
	}
}
