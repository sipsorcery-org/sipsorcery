// ============================================================================
// FileName: SIPAddressBinding.cs
//
// Description:
// SIP Registrar that strives to be RFC3822 compliant.
//
// Author(s):
// Aaron Clauson
//
// History:
// 23 Aug 2008	Aaron Clauson	Created, refactored from RegistrarCore.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using BlueFace.Sys.Net;
using BlueFace.VoIP.Authentication;
using BlueFace.VoIP.Net;
using BlueFace.VoIP.Net.SIP;
using BlueFace.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace BlueFace.VoIP.SIPServer
{
    /// <summary>
    /// The SIPAddressBinding represents a single registered contact uri for a user. A user can have multiple registered contact uri's.
    /// </summary>
    [DataContract]
    public class SIPAddressBinding
    {
        public const int MAX_BINDING_LIFETIME = 3600;       // Bindings are currently not being expired once the expires time is reached and this is the maximum amount of time 
                                                            // a binding can stay valid for with probing before it is removed and the binding must be freshed with a REGISTER.

        private static ILog logger = AppState.GetLogger("registrar");

        public static XmlNode UserAgentMaxExpiryNodes = null;                                       // An XML node that lists max expiry settings via user agent.
        private static Dictionary<string, int> m_userAgentExpirys = new Dictionary<string, int>();  // Result of parsing user agent expiry values from the App.Config Xml Node.

        public Guid BindingId;

        private bool m_dirty = true;    // Set to true whenever a change is made to the binding.
        public bool Dirty
        {
            get { return m_dirty; }
            set { m_dirty = value; }
        }

        [DataMember]
        public string UserAgent;

        public SIPURI ContactURI
        {
            get { return m_contact.ContactURI.CopyOf(); }
        }

        public SIPParameters ContactParameters
        {
            get { return m_contact.ContactParameters.CopyOf(); }
        }

        [DataMember]
        private SIPURI m_mangledContactURI;

        public SIPURI MangledContactURI
        {
            get { return m_mangledContactURI.CopyOf(); }
        }

        [DataMember]
        private SIPContactHeader m_contact;

        [DataMember]
        private DateTime m_lastUpdateTime = DateTime.Now;
        public DateTime LastUpdateTime
        {
            get { return m_lastUpdateTime; }
        }

        [XmlIgnore]
        [DataMember]
        public IPEndPoint ReceivedEndPoint;     // The socket the REGISTER request the binding was received on.

        [DataMember]
        public string SDPOwner;                 // In some cases the UA's do not contain a User Agent header and another way to identify them is by this piece of information from an INVITE SDP body.

        public string CallId;
        public int CSeq;

        [DataMember]
        private int m_expires = 0;
        public int Expires          // The expiry time in seconds for a specific contact.
        {
            get
            {
                int expires = m_expires - Convert.ToInt32(DateTime.Now.Subtract(m_lastUpdateTime).TotalSeconds);

                return expires;
            }
        }

        public string Q             // The Q value on the on the Contact header to indicate relative priority among bindings for the same address of record.
        {
            get
            {
                if (ContactParameters != null)
                {
                    return ContactParameters.Get(SIPContactHeader.QVALUE_PARAMETER_KEY);
                }
                else
                {
                    return null;
                }
            }
            set
            {
                m_contact.ContactParameters.Set(SIPContactHeader.QVALUE_PARAMETER_KEY, value);
            }
        }

        private IPEndPoint m_proxyEndPoint;
        public IPEndPoint ProxyEndPoint    // This is the socket the request was received from and assumes that the prior SIP element was a SIP proxy.
        {
            get { return m_proxyEndPoint; }
        }

        private SIPProtocolsEnum m_proxyProtocol;
        public SIPProtocolsEnum ProxyProtocol
        {
            get { return m_proxyProtocol; }
        }

        private IPEndPoint m_registrarEndPoint;
        public IPEndPoint RegistrarEndPoint
        {
            get { return m_registrarEndPoint; }
        }

        [DataMember]
        public SIPBindingRemovalReason RemovalReason = SIPBindingRemovalReason.Unknown;

        public DateTime LastNATKeepAliveSendTime = DateTime.Now;

        public static Int64 Created;
        public static Int64 Destroyed;

        /// <summary>
        /// Should not be used. Provided to allow service serialisation only.
        /// </summary>
        public SIPAddressBinding()
        {
            Created++;
        }

        /// <summary></summary>
        /// <param name="uacRecvdEndPoint">If this is non-null it indicates the contact header should be mangled based on the public socket the register request was demmed
        /// to have originated from rather then relying on the contact value recieved from the uac.</param>
        public SIPAddressBinding(SIPContactHeader contactHeader, string callId, int cseq, string userAgent, IPEndPoint uacRecvdEndPoint, IPEndPoint proxyEndPoint, SIPProtocolsEnum proxyProtocol, IPEndPoint registrarEndPoint, int expiry)
        {
            Created++;
            BindingId = Guid.NewGuid();
            m_contact = contactHeader.CopyOf();
            m_mangledContactURI = m_contact.ContactURI.CopyOf();
            CallId = callId;
            CSeq = cseq;
            UserAgent = userAgent;
            ReceivedEndPoint = uacRecvdEndPoint;
            m_proxyEndPoint = proxyEndPoint;
            m_proxyProtocol = proxyProtocol;
            m_registrarEndPoint = registrarEndPoint;

            if (uacRecvdEndPoint != null)
            {
                //if (SIPTransport.IsPrivateAddress(sipRequest.Header.Contact[0].ContactURI.Host) && m_mangleUACContact)
                if (Regex.Match(m_mangledContactURI.Host, @"(\d+\.){3}\d+").Success)
                {
                    // The Contact URI Host is used by registrars as the contact socket for the user so it needs to be changed to reflect the socket
                    // the intial request was received on in order to work around NAT. It's no good just relying on private addresses as a lot of User Agents
                    // determine their public IP but NOT their public port so they send the wrong port in the Contact header.

                    //logger.Debug("Mangling contact header from " + m_mangledContactURI.Host + " to " + IPSocket.GetSocketString(uacRecvdEndPoint) + ".");

                    m_mangledContactURI.Host = IPSocket.GetSocketString(uacRecvdEndPoint);
                }
            }

            m_expires = expiry;
        }

        public SIPAddressBinding(DataRow row)
        {
            Created++;
            BindingId = new Guid(row["contactid"] as string);
            m_contact = SIPContactHeader.ParseContactHeader(row["contacturi"] as string)[0];
            m_mangledContactURI = m_contact.ContactURI;
            UserAgent = row["useragent"] as string;
            m_proxyEndPoint = (row["proxysocket"] != null && row["proxysocket"].ToString().Trim().Length > 0) ? IPSocket.ParseSocketString(row["proxysocket"] as string) : null;
            m_proxyProtocol = (row["proxyprotocol"] != null && row["proxyprotocol"].ToString().Trim().Length > 0) ? SIPProtocolsType.GetProtocolType(row["proxyprotocol"] as string) : SIPProtocolsEnum.UDP;
        }

        public void RefreshBinding(int expiry)
        {
            m_lastUpdateTime = DateTime.Now;
            m_dirty = true;
            RemovalReason = SIPBindingRemovalReason.Unknown;
            m_expires = expiry;
        }

        /// <summary>
        /// Refreshes a binding when the remote network information of the remote or proxy end point has changed.
        /// </summary>
        public void RefreshBindingAndRemote(int expiry, IPEndPoint uacRecvdEndPoint, IPEndPoint proxyEndPoint, SIPProtocolsEnum proxyProtocol, IPEndPoint registrarEndPoint)
        {
            m_lastUpdateTime = DateTime.Now;
            m_dirty = true;
            m_proxyEndPoint = proxyEndPoint;
            m_proxyProtocol = proxyProtocol;
            m_registrarEndPoint = registrarEndPoint;
            RemovalReason = SIPBindingRemovalReason.Unknown;
            m_expires = expiry;

            if (uacRecvdEndPoint != null)
            {
                //if (SIPTransport.IsPrivateAddress(sipRequest.Header.Contact[0].ContactURI.Host) && m_mangleUACContact)
                if (Regex.Match(m_mangledContactURI.Host, @"(\d+\.){3}\d+").Success)
                {
                    // The Contact URI Host is used by registrars as the contact socket for the user so it needs to be changed to reflect the socket
                    // the intial request was received on in order to work around NAT. It's no good just relying on private addresses as a lot of User Agents
                    // determine their public IP but NOT their public port so they send the wrong port in the Contact header.

                    //logger.Debug("Mangling contact header from " + m_mangledContactURI.Host + " to " + IPSocket.GetSocketString(uacRecvdEndPoint) + ".");

                    m_mangledContactURI.Host = IPSocket.GetSocketString(uacRecvdEndPoint);
                }
            }
        }

        public string ToContactString()
        {
            return "<" + m_contact.ContactURI.ToString() + ">;" + SIPContactHeader.EXPIRES_PARAMETER_KEY + "=" + Expires;
        }

        public string ToMangledContactString()
        {
            return "<" + m_mangledContactURI.ToString() + ">;" + SIPContactHeader.EXPIRES_PARAMETER_KEY + "=" + Expires;
        }

        ~SIPAddressBinding()
        {
            Destroyed++;
        }

        #region Unit testing.

#if UNITTEST

        [TestFixture]
		public class SIPRegistrarRecordUnitTest
		{

            [TestFixtureSetUp]
            public void Init()
            { }
                
            [TestFixtureTearDown]
			public void Dispose()
			{}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");

				Console.WriteLine("---------------------------------"); 
			}

            [Test]
            public void GetExpiryUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string registrarExpiryNode = 
                    "<registrarexpirys>" +
                    " <useragents>" +
                    "  <useragent expiry='3600'>fring</useragent>" +
                    "  <useragent expiry='113'>*</useragent>" +
                    " </useragents>" +
                    "</registrarexpirys>";
                XmlDocument regExpiryDom = new XmlDocument();
                regExpiryDom.LoadXml(registrarExpiryNode);

                UserAgentMaxExpiryNodes = regExpiryDom.DocumentElement;

                int fringExpiry = GetExpiry(3600, "fring");

                Assert.IsTrue(fringExpiry == 3600, "The expiry value for the fring ua was incorrect.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void GetCiscoExpiryUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string registrarExpiryNode =
                    "<registrarexpirys>" +
                    " <useragents>" +
                    "  <useragent expiry='3600'>fring</useragent>" +
                    "  <useragent expiry='300'>Cisco-CP7960G/8.0</useragent>" +
                    "  <useragent expiry='113'>*</useragent>" +
                    " </useragents>" +
                    "</registrarexpirys>";
                XmlDocument regExpiryDom = new XmlDocument();
                regExpiryDom.LoadXml(registrarExpiryNode);

                UserAgentMaxExpiryNodes = regExpiryDom.DocumentElement;

                int ciscoExpiry = GetExpiry(500, "Cisco-CP7960G/8.0");

                Assert.IsTrue(ciscoExpiry == 300, "The expiry value for the Cisco ua was incorrect, " + ciscoExpiry + ".");

                Console.WriteLine("---------------------------------");
            }


            [Test]
            public void GetDefaultExpiryUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string registrarExpiryNode =
                    "<registrarexpirys>" +
                    " <useragents>" +
                    "  <useragent expiry='3600'>fring</useragent>" +
                    "  <useragent expiry='113'>*</useragent>" +
                    " </useragents>" +
                    "</registrarexpirys>";
                XmlDocument regExpiryDom = new XmlDocument();
                regExpiryDom.LoadXml(registrarExpiryNode);

                UserAgentMaxExpiryNodes = regExpiryDom.DocumentElement;

                int fringExpiry = GetExpiry(DEFAULT_EXPIRY_SECONDS, "cisco");

                Assert.IsTrue(fringExpiry == DEFAULT_EXPIRY_SECONDS, "The expiry value for the unknown ua was incorrect.");

                Console.WriteLine("---------------------------------");
            }
        }

#endif

        #endregion
    }
}
