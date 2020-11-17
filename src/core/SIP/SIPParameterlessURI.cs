//-----------------------------------------------------------------------------
// Filename: SIPParameterlessURI.cs
//
// Description: SIP URI that discards any parameters or headers. This type of URI is used for
// SIP Registrar address-of-record bindings.
//
// Author(s):
// Aaron Clauson
//
// History:
// 15 Dec 2006	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    [DataContract]
    public class SIPParameterlessURI
    {
        private static ILogger logger = Log.Logger;

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
        { }

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
            catch (Exception excp)
            {
                logger.LogError("Exception SIPParameterlessURI ToString. " + excp.Message);
                throw;
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
