// ============================================================================
// FileName: SIPRegistrarBinding.cs
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
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using SIPSorcery.Persistence;
using SIPSorcery.Sys;
using log4net;

#if !SILVERLIGHT
using System.Data;
using System.Data.Linq;
using System.Data.Linq.Mapping;
#endif

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP.App
{
    public enum SIPBindingRemovalReason
    {
        Unknown = 0,
        ClientExpiredSpecific = 1,
        ClientExpiredAll = 2,
        ExceededPerUserLimit = 3,
        OptionsTimedOut = 4,
        OptionsErrorResponse = 5,
        MaxLifetimeReached = 6,
        Administrative = 7,
    }

    /// <summary>
    /// The SIPAddressBinding represents a single registered contact uri for a user. A user can have multiple registered contact uri's.
    /// </summary>
    [Table(Name = "sipregistrarbindings")]
    [DataContract]
    public class SIPRegistrarBinding : INotifyPropertyChanged, ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipregistrarbindings";
        public const string XML_ELEMENT_NAME = "sipregistrarbinding";
        public const int MAX_BINDING_LIFETIME = 3600;       // Bindings are currently not being expired once the expires time is reached and this is the maximum amount of time 
        // a binding can stay valid for with probing before it is removed and the binding must be freshed with a REGISTER.

        //public static readonly string SelectBindingsQuery = "select * from sipregistrarbindings where sipaccountid = ?1";
        //public static readonly string SelectExpiredBindingsQuery = "select * from sipregistrarbindings where expirytime < ?1";

        private static string m_newLine = AppState.NewLine;
        private static ILog logger = AppState.GetLogger("sipregistrar");

        public static int TimeZoneOffsetMinutes;

        private static Dictionary<string, int> m_userAgentExpirys = new Dictionary<string, int>();  // Result of parsing user agent expiry values from the App.Config Xml Node.

        [Column(Name = "id", DbType = "varchar(36)", IsPrimaryKey = true, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public Guid Id { get; set; }

        [Column(Name = "sipaccountid", DbType = "varchar(36)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public Guid SIPAccountId { get; set; }

        [Column(Name = "sipaccountname", DbType = "varchar(160)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string SIPAccountName { get; set; }          // Used for informational purposes only, no matching done against it and should not be relied on. Use SIPAccountId instead.

        [Column(Name = "owner", DbType = "varchar(32)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string Owner { get; set; }

        [Column(Name = "adminmemberid", DbType = "varchar(32)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string AdminMemberId { get; private set; }    // If set it designates this asset as a belonging to a user with the matching adminid.

        [Column(Name = "useragent", DbType = "varchar(1024)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string UserAgent { get; set; }

        private SIPURI m_contactURI;
        [Column(Name = "contacturi", DbType = "varchar(767)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string ContactURI
        {
            get { return (m_contactURI != null) ? m_contactURI.ToString() : null; }
            set { m_contactURI = (!value.IsNullOrBlank()) ? SIPURI.ParseSIPURI(value) : null; }
        }

        public SIPURI ContactSIPURI
        {
            get { return m_contactURI; }
            set { m_contactURI = value; }
        }

        private SIPURI m_mangledContactURI;
        [Column(Name = "mangledcontacturi", DbType = "varchar(767)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string MangledContactURI
        {
            get { return (m_mangledContactURI != null) ? m_mangledContactURI.ToString() : null; }
            set { m_mangledContactURI = (!value.IsNullOrBlank()) ? SIPURI.ParseSIPURI(value) : null; }
        }

        public SIPURI MangledContactSIPURI
        {
            get { return m_mangledContactURI; }
            set { m_mangledContactURI = value; }
        }

        private DateTimeOffset m_lastUpdate;
        [Column(Name = "lastupdate", DbType = "datetimeoffset", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public DateTimeOffset LastUpdate
        {
            get { return m_lastUpdate; }
            set { m_lastUpdate = value.ToUniversalTime(); }
        }

        public DateTimeOffset LastUpdateLocal
        {
            get { return LastUpdate.AddMinutes(TimeZoneOffsetMinutes); }
        }

        [IgnoreDataMember]
        public SIPEndPoint RemoteSIPEndPoint;     // The socket the REGISTER request the binding was received on.

        [Column(Name = "remotesipsocket", DbType = "varchar(64)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string RemoteSIPSocket
        {
            get { return (RemoteSIPEndPoint != null) ? RemoteSIPEndPoint.ToString() : null; }
            set
            {
                if (value.IsNullOrBlank())
                {
                    RemoteSIPEndPoint = null;
                }
                else
                {
                    RemoteSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(value);
                }
            }
        }

        [IgnoreDataMember]
        public string CallId;

        [IgnoreDataMember]
        public int CSeq;

        private int m_expiry = 0;               // The expiry time in seconds for the binding.
        [Column(Name = "expiry", DbType = "int", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public int Expiry
        {
            get
            {
                return m_expiry;
            }
            set
            {
                m_expiry = value;
            }
        }

        [Column(Name = "expirytime", DbType = "datetimeoffset", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public DateTimeOffset ExpiryTime
        {
            get
            {
                return m_lastUpdate.AddSeconds(m_expiry);
            }
            set { }     // The expiry time is stored in the database for info. It is calculated from expiry and lastudpate and does not need a setter.
        }

        [DataMember]
        public string Q         // The Q value on the on the Contact header to indicate relative priority among bindings for the same address of record.
        {
            get
            {
                if (m_contactURI.Parameters != null)
                {
                    return m_contactURI.Parameters.Get(SIPContactHeader.QVALUE_PARAMETER_KEY);
                }
                else
                {
                    return null;
                }
            }
            set { m_contactURI.Parameters.Set(SIPContactHeader.QVALUE_PARAMETER_KEY, value); }
        }

        private SIPEndPoint m_proxySIPEndPoint;

        [IgnoreDataMember]
        public SIPEndPoint ProxySIPEndPoint    // This is the socket the request was received from and assumes that the prior SIP element was a SIP proxy.
        {
            get { return m_proxySIPEndPoint; }
            set { m_proxySIPEndPoint = value; }
        }

        [Column(Name = "proxysipsocket", DbType = "varchar(64)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string ProxySIPSocket
        {
            get { return (m_proxySIPEndPoint != null) ? m_proxySIPEndPoint.ToString() : null; }
            set
            {
                if (value.IsNullOrBlank())
                {
                    ProxySIPEndPoint = null;
                }
                else
                {
                    ProxySIPEndPoint = SIPEndPoint.ParseSIPEndPoint(value);
                }
            }
        }

        private SIPEndPoint m_registrarSIPEndPoint;
        [IgnoreDataMember]
        public SIPEndPoint RegistrarSIPEndPoint
        {
            get { return m_registrarSIPEndPoint; }
        }

        [Column(Name = "registrarsipsocket", DbType = "varchar(64)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string RegistrarSIPSocket
        {
            get { return (m_registrarSIPEndPoint != null) ? m_registrarSIPEndPoint.ToString() : null; }
            set
            {
                if (value.IsNullOrBlank())
                {
                    m_registrarSIPEndPoint = null;
                }
                else
                {
                    m_registrarSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(value);
                }
            }
        }

        public SIPBindingRemovalReason RemovalReason = SIPBindingRemovalReason.Unknown;

        public event PropertyChangedEventHandler PropertyChanged;

        public SIPRegistrarBinding() { }

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
            int expiry)
        {
            Id = Guid.NewGuid();
            LastUpdate = DateTime.UtcNow;
            SIPAccountId = sipAccount.Id;
            SIPAccountName = sipAccount.SIPUsername + "@" + sipAccount.SIPDomain;
            Owner = sipAccount.Owner;
            AdminMemberId = sipAccount.AdminMemberId;
            m_contactURI = bindingURI.CopyOf();
            m_mangledContactURI = m_contactURI.CopyOf();
            CallId = callId;
            CSeq = cseq;
            UserAgent = userAgent;
            RemoteSIPEndPoint = remoteSIPEndPoint;
            m_proxySIPEndPoint = proxySIPEndPoint;
            m_registrarSIPEndPoint = registrarSIPEndPoint;

            //if (SIPTransport.IsPrivateAddress(sipRequest.Header.Contact[0].ContactURI.Host) && m_mangleUACContact)
            if (Regex.Match(m_mangledContactURI.Host, @"(\d+\.){3}\d+").Success)
            {
                // The Contact URI Host is used by registrars as the contact socket for the user so it needs to be changed to reflect the socket
                // the intial request was received on in order to work around NAT. It's no good just relying on private addresses as a lot of User Agents
                // determine their public IP but NOT their public port so they send the wrong port in the Contact header.

                //logger.Debug("Mangling contact header from " + m_mangledContactURI.Host + " to " + IPSocket.GetSocketString(uacRecvdEndPoint) + ".");

                m_mangledContactURI.Host = remoteSIPEndPoint.SocketEndPoint.ToString();
            }

            m_expiry = expiry;
        }

#if !SILVERLIGHT

        public SIPRegistrarBinding(DataRow row)
        {
            Load(row);
        }

        public DataTable GetTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add(new DataColumn("id", typeof(String)));
            table.Columns.Add(new DataColumn("sipaccountid", typeof(String)));
            table.Columns.Add(new DataColumn("owner", typeof(String)));
            table.Columns.Add(new DataColumn("adminmemberid", typeof(String)));
            table.Columns.Add(new DataColumn("sipaccountname", typeof(String)));
            table.Columns.Add(new DataColumn("useragent", typeof(String)));
            table.Columns.Add(new DataColumn("contacturi", typeof(String)));
            table.Columns.Add(new DataColumn("mangledcontacturi", typeof(String)));
            table.Columns.Add(new DataColumn("expiry", typeof(Int32)));
            table.Columns.Add(new DataColumn("expirytime", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("remotesipsocket", typeof(String)));
            table.Columns.Add(new DataColumn("proxysipsocket", typeof(String)));
            table.Columns.Add(new DataColumn("registrarsipsocket", typeof(String)));
            table.Columns.Add(new DataColumn("lastupdate", typeof(DateTimeOffset)));
            return table;
        }

        public void Load(DataRow row)
        {
            try
            {
                Id = new Guid(row["id"] as string);
                SIPAccountId = new Guid(row["sipaccountid"] as string);
                SIPAccountName = row["sipaccountname"] as string;
                Owner = row["owner"] as string;
                AdminMemberId = row["adminmemberid"] as string;
                UserAgent = row["useragent"] as string;
                m_contactURI = SIPURI.ParseSIPURI(row["contacturi"] as string);
                m_mangledContactURI = (!(row["mangledcontacturi"] as string).IsNullOrBlank()) ? SIPURI.ParseSIPURI(row["mangledcontacturi"] as string) : null;
                Expiry = Convert.ToInt32(row["expiry"]);
                RemoteSIPEndPoint = (!(row["remotesipsocket"] as string).IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(row["remotesipsocket"] as string) : null;
                m_proxySIPEndPoint = (!(row["proxysipsocket"] as string).IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(row["proxysipsocket"] as string) : null;
                m_registrarSIPEndPoint = (!(row["registrarsipsocket"] as string).IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(row["registrarsipsocket"] as string) : null;
                LastUpdate = DateTimeOffset.Parse(row["lastupdate"] as string);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegistrarBinding Load. " + excp.Message);
                throw;
            }
        }

        public Dictionary<Guid, object> Load(XmlDocument dom)
        {
            return SIPAssetXMLPersistor<SIPRegistrarBinding>.LoadAssetsFromXMLRecordSet(dom);
        }

#endif

        /// <summary>
        /// Refreshes a binding when the remote network information of the remote or proxy end point has changed.
        /// </summary>
        public void RefreshBinding(int expiry, SIPEndPoint remoteSIPEndPoint, SIPEndPoint proxySIPEndPoint, SIPEndPoint registrarSIPEndPoint)
        {
            LastUpdate = DateTimeOffset.UtcNow;
            RemoteSIPEndPoint = remoteSIPEndPoint;
            m_proxySIPEndPoint = proxySIPEndPoint;
            m_registrarSIPEndPoint = registrarSIPEndPoint;
            RemovalReason = SIPBindingRemovalReason.Unknown;
            m_expiry = expiry;

            //if (SIPTransport.IsPrivateAddress(sipRequest.Header.Contact[0].ContactURI.Host) && m_mangleUACContact)
            if (Regex.Match(m_mangledContactURI.Host, @"(\d+\.){3}\d+").Success)
            {
                // The Contact URI Host is used by registrars as the contact socket for the user so it needs to be changed to reflect the socket
                // the intial request was received on in order to work around NAT. It's no good just relying on private addresses as a lot of User Agents
                // determine their public IP but NOT their public port so they send the wrong port in the Contact header.

                //logger.Debug("Mangling contact header from " + m_mangledContactURI.Host + " to " + IPSocket.GetSocketString(uacRecvdEndPoint) + ".");

                m_mangledContactURI.Host = remoteSIPEndPoint.SocketEndPoint.ToString();
            }
        }

        public string ToContactString()
        {
            int secondsRemaining = Convert.ToInt32(ExpiryTime.Subtract(DateTime.UtcNow).TotalSeconds % Int32.MaxValue);
            return "<" + m_contactURI.ToString() + ">;" + SIPContactHeader.EXPIRES_PARAMETER_KEY + "=" + secondsRemaining;
        }

        public string ToMangledContactString()
        {
            int secondsRemaining = Convert.ToInt32(ExpiryTime.Subtract(DateTime.UtcNow).TotalSeconds % Int32.MaxValue);
            return "<" + m_mangledContactURI.ToString() + ">;" + SIPContactHeader.EXPIRES_PARAMETER_KEY + "=" + secondsRemaining;
        }

        public string ToXML()
        {
            string providerXML =
                " <" + XML_ELEMENT_NAME + ">" + m_newLine +
                ToXMLNoParent() +
                " </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return providerXML;
        }

        public string ToXMLNoParent()
        {
            string contactURIStr = (m_contactURI != null) ? m_contactURI.ToString() : null;
            string mangledContactURIStr = (m_mangledContactURI != null) ? m_mangledContactURI.ToString() : null;

            string registrarBindingXML =
                "   <id>" + Id + "</id>" + m_newLine +
                "   <sipaccountid>" + SIPAccountId + "</sipaccountid>" + m_newLine +
                "   <sipaccountname>" + SIPAccountName + "</sipaccountname>" + m_newLine +
                "   <owner>" + Owner + "</owner>" + m_newLine +
                "   <adminmemberid>" + AdminMemberId + "</adminmemberid>" + m_newLine +
                "   <contacturi>" + contactURIStr + "</contacturi>" + m_newLine +
                "   <mangledcontacturi>" + mangledContactURIStr + "</mangledcontacturi>" + m_newLine +
                "   <expiry>" + Expiry + "</expiry>" + m_newLine +
                "   <useragent>" + SafeXML.MakeSafeXML(UserAgent) + "</useragent>" + m_newLine +
                "   <remotesipsocket>" + RemoteSIPSocket + "</remotesipsocket>" + m_newLine +
                "   <proxysipsocket>" + ProxySIPSocket + "</proxysipsocket>" + m_newLine +
                "   <registrarsipsocket>" + RegistrarSIPSocket + "</registrarsipsocket>" + m_newLine +
                "   <lastupdate>" + m_lastUpdate.ToString("o") + "</lastupdate>" + m_newLine +
                "   <expirytime>" + ExpiryTime.ToString("o") + "</expirytime>" + m_newLine;

            return registrarBindingXML;
        }

        public string GetXMLElementName()
        {
            return XML_ELEMENT_NAME;
        }

        public string GetXMLDocumentElementName()
        {
            return XML_DOCUMENT_ELEMENT_NAME;
        }

        private void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
