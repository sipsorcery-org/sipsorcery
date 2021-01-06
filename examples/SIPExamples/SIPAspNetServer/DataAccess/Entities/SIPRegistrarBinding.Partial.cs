// ============================================================================
// FileName: SIPRegistrarBinding.Partial.cs
//
// Description:
// Represents the SIPRegistrarBinding entity. This partial class is used to  
// apply additional properties or metadata to the audo generated 
// SIPRegistrarBinding class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 31 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using SIPSorcery.SIP;

#nullable disable

namespace demo.DataAccess
{
    public partial class SIPRegistrarBinding
    {
        internal string CallId;
        internal int CSeq;
        internal SIPEndPoint RemoteSIPEndPoint;     // The socket the REGISTER request the binding was received on.

        private SIPEndPoint m_proxySIPEndPoint;
        internal SIPEndPoint ProxySIPEndPoint       // This is the socket the request was received from and assumes that the prior SIP element was a SIP proxy.
        {
            get { return m_proxySIPEndPoint; }
            set { m_proxySIPEndPoint = value; }
        }

        private SIPEndPoint m_registrarSIPEndPoint;
        internal SIPEndPoint RegistrarSIPEndPoint
        {
            get { return m_registrarSIPEndPoint; }
        }

        public SIPRegistrarBinding()
        { }

        /// <summary></summary>
        /// <param name="uacRecvdEndPoint">If this is non-null it indicates the contact header should be mangled based on the public socket the register request was demmed
        /// to have originated from rather then relying on the contact value recieved from the uac.</param>
        public SIPRegistrarBinding(
            SIPAccount sipAccount,
            SIPURI bindingURI,
            string callId,
            int cseq,
            string userAgent,
            SIPEndPoint remoteSIPEndPoint,
            SIPEndPoint proxySIPEndPoint,
            SIPEndPoint registrarSIPEndPoint,
            int expirySeconds)
        {
            ID = Guid.NewGuid();
            LastUpdate = DateTime.UtcNow;
            SIPAccountID = sipAccount.ID;
            //sipaccountname = sipAccount.sipusername + "@" + sipAccount.domain.domain;
            ContactURI = bindingURI.ToString();
            //m_mangledContactURI = m_contactURI.CopyOf();
            CallId = callId;
            CSeq = cseq;
            UserAgent = userAgent;
            RemoteSIPEndPoint = remoteSIPEndPoint;
            m_proxySIPEndPoint = proxySIPEndPoint;
            m_registrarSIPEndPoint = registrarSIPEndPoint;

            ProxySIPSocket = proxySIPEndPoint?.ToString();
            RemoteSIPSocket = remoteSIPEndPoint.ToString();
            RegistrarSIPSocket = registrarSIPEndPoint?.ToString();
            LastUpdate = DateTime.UtcNow;

            //if (SIPTransport.IsPrivateAddress(sipRequest.Header.Contact[0].ContactURI.Host) && m_mangleUACContact)
            //if (!sipAccount.DontMangleEnabled && Regex.Match(m_mangledContactURI.Host, @"(\d+\.){3}\d+").Success)
            //{
            //    // The Contact URI Host is used by registrars as the contact socket for the user so it needs to be changed to reflect the socket
            //    // the intial request was received on in order to work around NAT. It's no good just relying on private addresses as a lot of User Agents
            //    // determine their public IP but NOT their public port so they send the wrong port in the Contact header.

            //    //logger.Debug("Mangling contact header from " + m_mangledContactURI.Host + " to " + IPSocket.GetSocketString(uacRecvdEndPoint) + ".");

            //    m_mangledContactURI.Host = remoteSIPEndPoint.GetIPEndPoint().ToString();
            //}

            Expiry = expirySeconds;

            if (expirySeconds > 0)
            {
                ExpiryTime = DateTime.Now.AddSeconds(expirySeconds);
            }
            else
            {
                ExpiryTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Refreshes a binding when the remote network information of the remote or proxy end point has changed.
        /// </summary>
        //public void RefreshBinding(int expiry, SIPEndPoint remoteSIPEndPoint, SIPEndPoint proxySIPEndPoint, SIPEndPoint registrarSIPEndPoint, bool dontMangle)
        //{
        //    Lastupdate = DateTimeOffset.UtcNow;
        //    RemoteSIPEndPoint = remoteSIPEndPoint;
        //    m_proxySIPEndPoint = proxySIPEndPoint;
        //    m_registrarSIPEndPoint = registrarSIPEndPoint;
        //    //RemovalReason = SIPBindingRemovalReason.Unknown;
        //    Expiry = expiry;

        //    //if (SIPTransport.IsPrivateAddress(sipRequest.Header.Contact[0].ContactURI.Host) && m_mangleUACContact)
        //    //if (!dontMangle && Regex.Match(m_mangledContactURI.Host, @"(\d+\.){3}\d+").Success)
        //    //{
        //    // The Contact URI Host is used by registrars as the contact socket for the user so it needs to be changed to reflect the socket
        //    // the intial request was received on in order to work around NAT. It's no good just relying on private addresses as a lot of User Agents
        //    // determine their public IP but NOT their public port so they send the wrong port in the Contact header.

        //    //logger.Debug("Mangling contact header from " + m_mangledContactURI.Host + " to " + IPSocket.GetSocketString(uacRecvdEndPoint) + ".");

        //    //     m_mangledContactURI.Host = remoteSIPEndPoint.GetIPEndPoint().ToString();
        //    // }
        //}
    }
}
