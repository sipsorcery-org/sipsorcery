//-----------------------------------------------------------------------------
// Filename: SIPHeader.cs
//
// Description: SIP Header.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Sep 2005	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// The Via header only has parameters, no headers. Parameters of from ...;name=value;name2=value2
    /// Specific parameters: ttl, maddr, received, branch.
    /// 
    /// From page 179 of RFC3261:
    /// "Even though this specification mandates that the branch parameter be
    /// present in all requests, the BNF for the header field indicates that
    /// it is optional."
    /// 
    /// The branch parameter on a Via therefore appears to be optionally mandatory?!
    ///
    /// Any SIP application element that uses transactions depends on the branch parameter for transaction matching.
    /// Only the top Via header branch is used for transactions though so if the request has made it to this stack
    /// with missing branches then in theory it should be safe to proceed. It will be left up to the SIPTransaction
    /// class to reject any SIP requests that are missing the necessary branch.
    /// </summary>
    public class SIPViaHeader
    {
        private static char m_paramDelimChar = ';';
        private static char m_hostDelimChar = ':';

        private static string m_receivedKey = SIPHeaderAncillary.SIP_HEADERANC_RECEIVED;
        private static string m_rportKey = SIPHeaderAncillary.SIP_HEADERANC_RPORT;
        private static string m_branchKey = SIPHeaderAncillary.SIP_HEADERANC_BRANCH;

        /// <summary>
        /// Special SIP Via header that is recognised by the SIP transport classes Send methods. At send time this header will be replaced by 
        /// one with IP end point details that reflect the socket the request or response was sent from.
        /// </summary>
        public static SIPViaHeader GetDefaultSIPViaHeader(SIPProtocolsEnum protocol = SIPProtocolsEnum.udp)
        {
            return new SIPViaHeader(new IPEndPoint(IPAddress.Any, 0), CallProperties.CreateBranchId(), protocol);
        }

        public string Version;
        public SIPProtocolsEnum Transport;
        public string Host;
        public int Port = 0;
        public string Branch
        {
            get
            {
                if (ViaParameters != null && ViaParameters.Has(m_branchKey))
                {
                    return ViaParameters.Get(m_branchKey);
                }
                else
                {
                    return null;
                }
            }
            set { ViaParameters.Set(m_branchKey, value); }
        }
        public string ReceivedFromIPAddress     // IP Address contained in the received parameter.
        {
            get
            {
                if (ViaParameters != null && ViaParameters.Has(m_receivedKey))
                {
                    return ViaParameters.Get(m_receivedKey);
                }
                else
                {
                    return null;
                }
            }
            set { ViaParameters.Set(m_receivedKey, value); }
        }
        public int ReceivedFromPort             // Port contained in the rport parameter.
        {
            get
            {
                if (ViaParameters != null && ViaParameters.Has(m_rportKey))
                {
                    return Convert.ToInt32(ViaParameters.Get(m_rportKey));
                }
                else
                {
                    return 0;
                }
            }
            set { ViaParameters.Set(m_rportKey, value.ToString()); }
        }

        public SIPParameters ViaParameters = new SIPParameters(null, m_paramDelimChar);

        public string ContactAddress            // This the address placed into the Via header by the User Agent.
        {
            get
            {
                if (IPSocket.TryParseIPEndPoint(Host, out var ipEndPoint))
                {
                    if (ipEndPoint.Port == 0)
                    {
                        if (Port != 0)
                        {
                            ipEndPoint.Port = Port;
                            return ipEndPoint.ToString();
                        }
                        else
                        {
                            if (ipEndPoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                            {
                                return "[" + ipEndPoint.Address.ToString() + "]";
                            }
                            else
                            {
                                return ipEndPoint.Address.ToString();
                            }
                        }
                    }
                    else
                    {
                        return ipEndPoint.ToString();
                    }
                }
                else if (Port != 0)
                {
                    return Host + ":" + Port;
                }
                else
                {
                    return Host;
                }
            }
        }

        public string ReceivedFromAddress       // This is the socket the request was received on and is a combination of the Host and Received fields.
        {
            get
            {
                if (ReceivedFromIPAddress != null && ReceivedFromPort != 0)
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Parse(ReceivedFromIPAddress), ReceivedFromPort);
                    return ep.ToString();
                }
                else if (ReceivedFromIPAddress != null && Port != 0)
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Parse(ReceivedFromIPAddress), Port);
                    return ep.ToString();
                }
                else if (ReceivedFromIPAddress != null)
                {
                    return ReceivedFromIPAddress;
                }
                else if (ReceivedFromPort != 0)
                {
                    if (IPAddress.TryParse(Host, out IPAddress hostip))
                    {
                        IPEndPoint ep = new IPEndPoint(hostip, ReceivedFromPort);
                        return ep.ToString();
                    }
                    else
                    {
                        return Host + ":" + ReceivedFromPort;
                    }
                }
                else if (Port != 0)
                {
                    if (IPAddress.TryParse(Host, out IPAddress hostip))
                    {
                        IPEndPoint ep = new IPEndPoint(hostip, Port);
                        return ep.ToString();
                    }
                    else
                    {
                        return Host + ":" + Port;
                    }
                }
                else
                {
                    return Host;
                }
            }
        }

        public SIPViaHeader()
        { }

        public SIPViaHeader(string contactIPAddress, int contactPort, string branch, SIPProtocolsEnum protocol)
        {
            Version = SIPConstants.SIP_FULLVERSION_STRING;
            Transport = protocol;
            Host = contactIPAddress;
            Port = contactPort;
            Branch = branch;
            ViaParameters.Set(m_rportKey, null);
        }

        public SIPViaHeader(string contactIPAddress, int contactPort, string branch) :
            this(contactIPAddress, contactPort, branch, SIPProtocolsEnum.udp)
        { }

        public SIPViaHeader(IPEndPoint contactEndPoint, string branch) :
            this(contactEndPoint.Address.ToString(), contactEndPoint.Port, branch, SIPProtocolsEnum.udp)
        { }

        public SIPViaHeader(SIPEndPoint localEndPoint, string branch) :
            this(localEndPoint.GetIPEndPoint(), branch, localEndPoint.Protocol)
        { }

        public SIPViaHeader(string contactEndPoint, string branch) :
            this(IPSocket.ParseSocketString(contactEndPoint), branch, SIPProtocolsEnum.udp)
        { }

        public SIPViaHeader(IPEndPoint contactEndPoint, string branch, SIPProtocolsEnum protocol) :
            this(contactEndPoint.Address.ToString(), contactEndPoint.Port, branch, protocol)
        { }

        public static SIPViaHeader[] ParseSIPViaHeader(string viaHeaderStr)
        {
            List<SIPViaHeader> viaHeadersList = new List<SIPViaHeader>();

            if (!viaHeaderStr.IsNullOrBlank())
            {
                viaHeaderStr = viaHeaderStr.Trim();

                // Multiple Via headers can be contained in a single line by separating them with a comma.
                string[] viaHeaders = SIPParameters.GetKeyValuePairsFromQuoted(viaHeaderStr, ',');

                foreach (string viaHeaderStrItem in viaHeaders)
                {
                    if (viaHeaderStrItem == null || viaHeaderStrItem.Trim().Length == 0)
                    {
                        throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "No Contact address.");
                    }
                    else
                    {
                        SIPViaHeader viaHeader = new SIPViaHeader();
                        string header = viaHeaderStrItem.Trim();

                        int firstSpacePosn = header.IndexOf(" ");
                        if (firstSpacePosn == -1)
                        {
                            throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "No Contact address.");
                        }
                        else
                        {
                            string versionAndTransport = header.Substring(0, firstSpacePosn);
                            viaHeader.Version = versionAndTransport.Substring(0, versionAndTransport.LastIndexOf('/'));
                            viaHeader.Transport = SIPProtocolsType.GetProtocolType(versionAndTransport.Substring(versionAndTransport.LastIndexOf('/') + 1));

                            string nextField = header.Substring(firstSpacePosn, header.Length - firstSpacePosn).Trim();

                            int delimIndex = nextField.IndexOf(';');
                            string contactAddress = null;

                            // Some user agents include branch but have the semi-colon missing, that's easy to cope with by replacing "branch" with ";branch".
                            if (delimIndex == -1 && nextField.Contains(m_branchKey))
                            {
                                nextField = nextField.Replace(m_branchKey, ";" + m_branchKey);
                                delimIndex = nextField.IndexOf(';');
                            }

                            if (delimIndex == -1)
                            {
                                //logger.LogWarning("Via header missing semi-colon: " + header + ".");
                                //parserError = SIPValidationError.NoBranchOnVia;
                                //return null;
                                contactAddress = nextField.Trim();
                            }
                            else
                            {
                                contactAddress = nextField.Substring(0, delimIndex).Trim();
                                viaHeader.ViaParameters = new SIPParameters(nextField.Substring(delimIndex, nextField.Length - delimIndex), m_paramDelimChar);
                            }

                            if (contactAddress == null || contactAddress.Trim().Length == 0)
                            {
                                // Check that the branch parameter is present, without it the Via header is illegal.
                                //if (!viaHeader.ViaParameters.Has(m_branchKey))
                                //{
                                //    logger.LogWarning("Via header missing branch: " + header + ".");
                                //    parserError = SIPValidationError.NoBranchOnVia;
                                //    return null;
                                //}

                                throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "No Contact address.");
                            }

                            // Parse the contact address.
                            if (IPSocket.TryParseIPEndPoint(contactAddress, out var ipEndPoint))
                            {
                                viaHeader.Host = ipEndPoint.Address.ToString();
                                if (ipEndPoint.Port != 0)
                                {
                                    viaHeader.Port = ipEndPoint.Port;
                                }
                            }
                            else
                            {
                                // Now parsing non IP address contact addresses.
                                int colonIndex = contactAddress.IndexOf(m_hostDelimChar);
                                if (colonIndex != -1)
                                {
                                    viaHeader.Host = contactAddress.Substring(0, colonIndex);

                                    if (!Int32.TryParse(contactAddress.Substring(colonIndex + 1), out viaHeader.Port))
                                    {
                                        throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "Non-numeric port for IP address.");
                                    }
                                    else if (viaHeader.Port > IPEndPoint.MaxPort)
                                    {
                                        throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "The port specified in a Via header exceeded the maximum allowed.");
                                    }
                                }
                                else
                                {
                                    viaHeader.Host = contactAddress;
                                }
                            }

                            viaHeadersList.Add(viaHeader);
                        }
                    }
                }
            }

            if (viaHeadersList.Count > 0)
            {
                return viaHeadersList.ToArray();
            }
            else
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "Via list was empty.");
            }
        }

        public new string ToString()
        {
            string sipViaHeader = SIPHeaders.SIP_HEADER_VIA + ": " + this.Version + "/" + this.Transport.ToString().ToUpper() + " " + ContactAddress;
            sipViaHeader += (ViaParameters != null && ViaParameters.Count > 0) ? ViaParameters.ToString() : null;

            return sipViaHeader;
        }
    }

    /// <bnf>
    /// From            =  ( "From" / "f" ) HCOLON from-spec
    /// from-spec       =  ( name-addr / addr-spec ) *( SEMI from-param )
    /// from-param      =  tag-param / generic-param
    /// name-addr		=  [ display-name ] LAQUOT addr-spec RAQUOT
    /// addr-spec		=  SIP-URI / SIPS-URI / absoluteURI
    /// tag-param       =  "tag" EQUAL token
    /// generic-param   =  token [ EQUAL gen-value ]
    /// gen-value       =  token / host / quoted-string
    /// </bnf>
    /// <remarks>
    /// The From header only has parameters, no headers. Parameters of from ...;name=value;name2=value2.
    /// Specific parameters: tag.
    /// </remarks>
    public class SIPFromHeader
    {
        //public const string DEFAULT_FROM_NAME = SIPConstants.SIP_DEFAULT_USERNAME;
        public const string DEFAULT_FROM_URI = SIPConstants.SIP_DEFAULT_FROMURI;
        public const string PARAMETER_TAG = SIPHeaderAncillary.SIP_HEADERANC_TAG;

        /// <summary>
        /// Special SIP From header that is recognised by the SIP transport classes Send methods. At send time this header will be replaced by 
        /// one with IP end point details that reflect the socket the request or response was sent from.
        /// </summary>
        public static SIPFromHeader GetDefaultSIPFromHeader(SIPSchemesEnum scheme)
        {
            return new SIPFromHeader(null, new SIPURI(scheme, IPAddress.Any, 0), CallProperties.CreateNewTag());
        }

        public string FromName
        {
            get { return m_userField.Name; }
            set { m_userField.Name = value; }
        }

        public SIPURI FromURI
        {
            get { return m_userField.URI; }
            set { m_userField.URI = value; }
        }

        public string FromTag
        {
            get { return FromParameters.Get(PARAMETER_TAG); }
            set
            {
                if (value != null && value.Trim().Length > 0)
                {
                    FromParameters.Set(PARAMETER_TAG, value);
                }
                else
                {
                    if (FromParameters.Has(PARAMETER_TAG))
                    {
                        FromParameters.Remove(PARAMETER_TAG);
                    }
                }
            }
        }

        public SIPParameters FromParameters
        {
            get { return m_userField.Parameters; }
            set { m_userField.Parameters = value; }
        }

        private SIPUserField m_userField = new SIPUserField();
        public SIPUserField FromUserField
        {
            get { return m_userField; }
            set { m_userField = value; }
        }

        private SIPFromHeader()
        { }

        public SIPFromHeader(string fromName, SIPURI fromURI, string fromTag)
        {
            m_userField = new SIPUserField(fromName, fromURI, null);
            FromTag = fromTag;
        }

        public static SIPFromHeader ParseFromHeader(string fromHeaderStr)
        {
            try
            {
                SIPFromHeader fromHeader = new SIPFromHeader();

                fromHeader.m_userField = SIPUserField.ParseSIPUserField(fromHeaderStr);

                return fromHeader;
            }
            catch (ArgumentException argExcp)
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.FromHeader, argExcp.Message);
            }
            catch
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.FromHeader, "The SIP From header was invalid.");
            }
        }

        public override string ToString()
        {
            return m_userField.ToString();
        }

        /// <summary>
        /// Returns a friendly description of the caller that's suitable for humans. Leaves out
        /// all the parameters etc.
        /// </summary>
        /// <returns>A string representing a friendly description of the From header.</returns>
        public string FriendlyDescription()
        {
            string caller = FromURI.ToAOR();
            caller = (!string.IsNullOrEmpty(FromName)) ? FromName + " " + caller : caller;
            return caller;
        }
    }

    /// <bnf>
    /// To				=  ( "To" / "t" ) HCOLON ( name-addr / addr-spec ) *( SEMI to-param )
    /// to-param		=  tag-param / generic-param
    /// name-addr		=  [ display-name ] LAQUOT addr-spec RAQUOT
    /// addr-spec		=  SIP-URI / SIPS-URI / absoluteURI
    /// tag-param       =  "tag" EQUAL token
    /// generic-param   =  token [ EQUAL gen-value ]
    /// gen-value       =  token / host / quoted-string
    /// </bnf>
    /// <remarks>
    /// The To header only has parameters, no headers. Parameters of from ...;name=value;name2=value2.
    /// Specific parameters: tag.
    /// </remarks>
    public class SIPToHeader
    {
        public const string PARAMETER_TAG = SIPHeaderAncillary.SIP_HEADERANC_TAG;

        public string ToName
        {
            get { return m_userField.Name; }
            set { m_userField.Name = value; }
        }

        public SIPURI ToURI
        {
            get { return m_userField.URI; }
            set { m_userField.URI = value; }
        }

        public string ToTag
        {
            get { return ToParameters.Get(PARAMETER_TAG); }
            set
            {
                if (value != null && value.Trim().Length > 0)
                {
                    ToParameters.Set(PARAMETER_TAG, value);
                }
                else
                {
                    if (ToParameters.Has(PARAMETER_TAG))
                    {
                        ToParameters.Remove(PARAMETER_TAG);
                    }
                }
            }
        }

        public SIPParameters ToParameters
        {
            get { return m_userField.Parameters; }
            set { m_userField.Parameters = value; }
        }

        private SIPUserField m_userField;
        public SIPUserField ToUserField
        {
            get { return m_userField; }
            set { m_userField = value; }
        }

        private SIPToHeader()
        { }

        public SIPToHeader(string toName, SIPURI toURI, string toTag)
        {
            m_userField = new SIPUserField(toName, toURI, null);
            ToTag = toTag;
        }

        public static SIPToHeader ParseToHeader(string toHeaderStr)
        {
            try
            {
                SIPToHeader toHeader = new SIPToHeader();

                toHeader.m_userField = SIPUserField.ParseSIPUserField(toHeaderStr);

                return toHeader;
            }
            catch (ArgumentException argExcp)
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.ToHeader, argExcp.Message);
            }
            catch
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.ToHeader, "The SIP To header was invalid.");
            }
        }

        public override string ToString()
        {
            return m_userField.ToString();
        }
    }

    /// <bnf>
    /// Contact        =  ("Contact" / "m" ) HCOLON ( STAR / (contact-param *(COMMA contact-param)))
    /// contact-param  =  (name-addr / addr-spec) *(SEMI contact-params)
    /// name-addr      =  [ display-name ] LAQUOT addr-spec RAQUOT
    /// addr-spec      =  SIP-URI / SIPS-URI / absoluteURI
    /// display-name   =  *(token LWS)/ quoted-string
    ///
    /// contact-params     =  c-p-q / c-p-expires / contact-extension
    /// c-p-q              =  "q" EQUAL qvalue
    /// c-p-expires        =  "expires" EQUAL delta-seconds
    /// contact-extension  =  generic-param
    /// delta-seconds      =  1*DIGIT
    /// generic-param  =  token [ EQUAL gen-value ]
    /// gen-value      =  token / host / quoted-string
    /// </bnf>
    /// <remarks>
    /// The Contact header only has parameters, no headers. Parameters of from ...;name=value;name2=value2
    /// Specific parameters: q, expires.
    /// </remarks>
    [DataContract]
    public class SIPContactHeader
    {
        public const string EXPIRES_PARAMETER_KEY = "expires";
        public const string QVALUE_PARAMETER_KEY = "q";

        //private static char[] m_nonStandardURIDelimChars = new char[] { '\n', '\r', ' ' };	// Characters that can delimit a SIP URI, supposed to be > but it is sometimes missing.

        /// <summary>
        /// Special SIP contact header that is recognised by the SIP transport classes Send methods. At send time this header will be replaced by 
        /// one with IP end point details that reflect the socket the request or response was sent from.
        /// </summary>
        public static SIPContactHeader GetDefaultSIPContactHeader()
        {
            return new SIPContactHeader(null, new SIPURI(SIPSchemesEnum.sip, IPAddress.Any, 0));
        }

        public string RawHeader;

        public string ContactName
        {
            get { return m_userField.Name; }
            set { m_userField.Name = value; }
        }

        public SIPURI ContactURI
        {
            get { return m_userField.URI; }
            set { m_userField.URI = value; }
        }

        public SIPParameters ContactParameters
        {
            get { return m_userField.Parameters; }
            set { m_userField.Parameters = value; }
        }

        // A value of -1 indicates the header did not contain an expires parameter setting.
        public int Expires
        {
            get
            {
                int expires = -1;

                if (ContactParameters.Has(EXPIRES_PARAMETER_KEY))
                {
                    string expiresStr = ContactParameters.Get(EXPIRES_PARAMETER_KEY);
                    Int32.TryParse(expiresStr, out expires);
                }

                return expires;
            }
            set { ContactParameters.Set(EXPIRES_PARAMETER_KEY, value.ToString()); }
        }
        public string Q
        {
            get { return ContactParameters.Get(QVALUE_PARAMETER_KEY); }
            set { ContactParameters.Set(QVALUE_PARAMETER_KEY, value); }
        }

        private SIPUserField m_userField;

        private SIPContactHeader()
        { }

        public SIPContactHeader(string contactName, SIPURI contactURI)
        {
            m_userField = new SIPUserField(contactName, contactURI, null);
        }

        public SIPContactHeader(SIPUserField contactUserField)
        {
            m_userField = contactUserField;
        }

        public static List<SIPContactHeader> ParseContactHeader(string contactHeaderStr)
        {
            try
            {
                if (contactHeaderStr == null || contactHeaderStr.Trim().Length == 0)
                {
                    return null;
                }

                //string[] contactHeaders = null;

                //// Broken User Agent fix (Aastra looking at you!)
                //if (contactHeaderStr.IndexOf('<') != -1 && contactHeaderStr.IndexOf('>') == -1)
                //{
                //    int nonStandardDelimPosn = contactHeaderStr.IndexOfAny(m_nonStandardURIDelimChars);

                //    if (nonStandardDelimPosn != -1)
                //    {
                //        // Add on the missing RQUOT and ignore whatever the rest of the header is.
                //        contactHeaders = new string[] { contactHeaderStr.Substring(0, nonStandardDelimPosn) + ">" };
                //    }
                //    else
                //    {
                //        // Can't work out what is going on with this header bomb out.
                //        throw new SIPValidationException(SIPValidationFieldsEnum.ContactHeader, "Contact header invalid.");
                //    }
                //}
                //else
                //{
                //    contactHeaders = SIPParameters.GetKeyValuePairsFromQuoted(contactHeaderStr, ',');
                //}

                string[] contactHeaders = SIPParameters.GetKeyValuePairsFromQuoted(contactHeaderStr, ',');

                List<SIPContactHeader> contactHeaderList = new List<SIPContactHeader>();

                foreach (string contactHeaderItemStr in contactHeaders)
                {
                    SIPContactHeader contactHeader = new SIPContactHeader();
                    contactHeader.RawHeader = contactHeaderStr;
                    contactHeader.m_userField = SIPUserField.ParseSIPUserField(contactHeaderItemStr);
                    contactHeaderList.Add(contactHeader);
                }

                return contactHeaderList;
            }
            catch (SIPValidationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.ContactHeader, "Contact header invalid, parse failed. " + excp.Message);
            }
        }

        public static List<SIPContactHeader> CreateSIPContactList(SIPURI sipURI)
        {
            List<SIPContactHeader> contactHeaderList = new List<SIPContactHeader>();
            contactHeaderList.Add(new SIPContactHeader(null, sipURI));

            return contactHeaderList;
        }

        /// <summary>
        /// Compares two contact headers to determine contact address equality.
        /// </summary>
        public static bool AreEqual(SIPContactHeader contact1, SIPContactHeader contact2)
        {
            if (!SIPURI.AreEqual(contact1.ContactURI, contact2.ContactURI))
            {
                return false;
            }
            else
            {
                // Compare invariant parameters.
                string[] contact1Keys = contact1.ContactParameters.GetKeys();

                if (contact1Keys != null && contact1Keys.Length > 0)
                {
                    foreach (string key in contact1Keys)
                    {
                        if (key == EXPIRES_PARAMETER_KEY || key == QVALUE_PARAMETER_KEY)
                        {
                            continue;
                        }
                        else if (contact1.ContactParameters.Get(key) != contact2.ContactParameters.Get(key))
                        {
                            return false;
                        }
                    }
                }

                // Need to do the reverse as well
                string[] contact2Keys = contact2.ContactParameters.GetKeys();

                if (contact2Keys != null && contact2Keys.Length > 0)
                {
                    foreach (string key in contact2Keys)
                    {
                        if (key == EXPIRES_PARAMETER_KEY || key == QVALUE_PARAMETER_KEY)
                        {
                            continue;
                        }
                        else if (contact2.ContactParameters.Get(key) != contact1.ContactParameters.Get(key))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        public override string ToString()
        {
            if (m_userField.URI.Host == SIPConstants.SIP_REGISTER_REMOVEALL)
            {
                return SIPConstants.SIP_REGISTER_REMOVEALL;
            }
            else
            {
                //if (m_userField.URI.Protocol == SIPProtocolsEnum.UDP)
                //{
                return m_userField.ToString();
                //}
                //else
                //{
                //    return m_userField.ToContactString();
                //}
            }
        }

        public SIPContactHeader CopyOf()
        {
            SIPContactHeader copy = new SIPContactHeader();
            copy.RawHeader = RawHeader;
            copy.m_userField = m_userField.CopyOf();

            return copy;
        }
    }

    public class SIPAuthenticationHeader
    {
        public SIPAuthorisationDigest SIPDigest;

        //public string Realm;
        //public string Nonce;
        //public string Username;
        //public string URI;
        //public string Response;

        private SIPAuthenticationHeader()
        {
            SIPDigest = new SIPAuthorisationDigest();
        }

        public SIPAuthenticationHeader(SIPAuthorisationDigest sipDigest)
        {
            SIPDigest = sipDigest;
        }

        public SIPAuthenticationHeader(SIPAuthorisationHeadersEnum authorisationType, string realm, string nonce)
        {
            SIPDigest = new SIPAuthorisationDigest(authorisationType);
            SIPDigest.Realm = realm;
            SIPDigest.Nonce = nonce;
        }

        public static SIPAuthenticationHeader ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum authorizationType, string headerValue)
        {
            try
            {
                SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader();
                authHeader.SIPDigest = SIPAuthorisationDigest.ParseAuthorisationDigest(authorizationType, headerValue);
                return authHeader;
            }
            catch
            {
                throw new ApplicationException("Error parsing SIP authentication header request, " + headerValue);
            }
        }

        public override string ToString()
        {
            if (SIPDigest != null)
            {
                string authHeader = null;
                SIPAuthorisationHeadersEnum authorisationHeaderType = (SIPDigest.AuthorisationResponseType != SIPAuthorisationHeadersEnum.Unknown) ? SIPDigest.AuthorisationResponseType : SIPDigest.AuthorisationType;

                if (authorisationHeaderType == SIPAuthorisationHeadersEnum.Authorize)
                {
                    authHeader = SIPHeaders.SIP_HEADER_AUTHORIZATION + ": ";
                }
                else if (authorisationHeaderType == SIPAuthorisationHeadersEnum.ProxyAuthenticate)
                {
                    authHeader = SIPHeaders.SIP_HEADER_PROXYAUTHENTICATION + ": ";
                }
                else if (authorisationHeaderType == SIPAuthorisationHeadersEnum.ProxyAuthorization)
                {
                    authHeader = SIPHeaders.SIP_HEADER_PROXYAUTHORIZATION + ": ";
                }
                else if (authorisationHeaderType == SIPAuthorisationHeadersEnum.WWWAuthenticate)
                {
                    authHeader = SIPHeaders.SIP_HEADER_WWWAUTHENTICATE + ": ";
                }
                else
                {
                    authHeader = SIPHeaders.SIP_HEADER_AUTHORIZATION + ": ";
                }

                return authHeader + SIPDigest.ToString();
            }
            else
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The SIPRoute class is used to represent both Route and Record-Route headers.
    /// </summary>
    /// <bnf>
    /// Route               =  "Route" HCOLON route-param *(COMMA route-param)
    /// route-param         =  name-addr *( SEMI rr-param )
    /// 
    /// Record-Route        =  "Record-Route" HCOLON rec-route *(COMMA rec-route)
    /// rec-route           =  name-addr *( SEMI rr-param )
    /// rr-param            =  generic-param
    ///
    /// name-addr           =  [ display-name ] LAQUOT addr-spec RAQUOT
    /// addr-spec           =  SIP-URI / SIPS-URI / absoluteURI
    /// display-name        =  *(token LWS)/ quoted-string
    /// generic-param       =  token [ EQUAL gen-value ]
    /// gen-value           =  token / host / quoted-string
    /// </bnf>
    /// <remarks>
    /// The Route and Record-Route headers only have parameters, no headers. Parameters of from ...;name=value;name2=value2
    /// There are no specific parameters.
    /// </remarks>
    public class SIPRoute
    {
        private static string m_looseRouterParameter = SIPConstants.SIP_LOOSEROUTER_PARAMETER;

        private static char[] m_angles = new char[] { '<', '>' };

        private SIPUserField m_userField;

        public string Host
        {
            get { return m_userField.URI.Host; }
            set { m_userField.URI.Host = value; }
        }

        public SIPURI URI
        {
            get { return m_userField.URI; }
        }

        public bool IsStrictRouter
        {
            get { return !m_userField.URI.Parameters.Has(m_looseRouterParameter); }
            set
            {
                if (value)
                {
                    m_userField.URI.Parameters.Remove(m_looseRouterParameter);
                }
                else
                {
                    m_userField.URI.Parameters.Set(m_looseRouterParameter, null);
                }
            }
        }

        private SIPRoute()
        { }

        public SIPRoute(string host)
        {
            if (host.IsNullOrBlank())
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.RouteHeader, "Cannot create a Route from an blank string.");
            }

            m_userField = SIPUserField.ParseSIPUserField(host);
        }

        public SIPRoute(string host, bool looseRouter)
        {
            if (host.IsNullOrBlank())
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.RouteHeader, "Cannot create a Route from an blank string.");
            }

            m_userField = SIPUserField.ParseSIPUserField(host);
            this.IsStrictRouter = !looseRouter;
        }

        public SIPRoute(SIPURI uri)
        {
            m_userField = new SIPUserField();
            m_userField.URI = uri;
        }

        public SIPRoute(SIPURI uri, bool looseRouter)
        {
            m_userField = new SIPUserField();
            m_userField.URI = uri;
            this.IsStrictRouter = !looseRouter;
        }

        public static SIPRoute ParseSIPRoute(string route)
        {
            if (route.IsNullOrBlank())
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.RouteHeader, "Cannot create a Route from an blank string.");
            }

            try
            {
                SIPRoute sipRoute = new SIPRoute();
                sipRoute.m_userField = SIPUserField.ParseSIPUserField(route);

                return sipRoute;
            }
            catch (Exception excp)
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.RouteHeader, excp.Message);
            }
        }

        public override string ToString()
        {
            //if (m_userField.URI.Protocol == SIPProtocolsEnum.UDP)
            //{
            return m_userField.ToString();
            //}
            //else
            //{
            //return m_userField.ToContactString();
            //}
        }

        public SIPEndPoint ToSIPEndPoint()
        {
            return URI.ToSIPEndPoint();
        }
    }

    public class SIPRouteSet
    {
        private List<SIPRoute> m_sipRoutes = new List<SIPRoute>();

        public int Length
        {
            get { return m_sipRoutes.Count; }
        }

        public static SIPRouteSet ParseSIPRouteSet(string routeSet)
        {
            SIPRouteSet sipRouteSet = new SIPRouteSet();

            string[] routes = SIPParameters.GetKeyValuePairsFromQuoted(routeSet, ',');
            foreach (string route in routes)
            {
                SIPRoute sipRoute = SIPRoute.ParseSIPRoute(route);
                sipRouteSet.AddBottomRoute(sipRoute);
            }

            return sipRouteSet;
        }

        public SIPRoute GetAt(int index)
        {
            return m_sipRoutes[index];
        }

        public void SetAt(int index, SIPRoute sipRoute)
        {
            m_sipRoutes[index] = sipRoute;
        }

        public SIPRoute TopRoute
        {
            get
            {
                if (m_sipRoutes != null && m_sipRoutes.Count > 0)
                {
                    return m_sipRoutes[0];
                }
                else
                {
                    return null;
                }
            }
        }

        public SIPRoute BottomRoute
        {
            get
            {
                if (m_sipRoutes != null && m_sipRoutes.Count > 0)
                {
                    return m_sipRoutes[m_sipRoutes.Count - 1];
                }
                else
                {
                    return null;
                }
            }
        }

        public void PushRoute(SIPRoute route)
        {
            m_sipRoutes.Insert(0, route);
        }

        public void PushRoute(string host)
        {
            m_sipRoutes.Insert(0, new SIPRoute(host, true));
        }

        public void PushRoute(IPEndPoint socket, SIPSchemesEnum scheme, SIPProtocolsEnum protcol)
        {
            m_sipRoutes.Insert(0, new SIPRoute(scheme + ":" + socket.ToString(), true));
        }

        public void AddBottomRoute(SIPRoute route)
        {
            m_sipRoutes.Insert(m_sipRoutes.Count, route);
        }

        public SIPRoute PopRoute()
        {
            SIPRoute route = null;

            if (m_sipRoutes.Count > 0)
            {
                route = m_sipRoutes[0];
                m_sipRoutes.RemoveAt(0);
            }

            return route;
        }

        public void RemoveBottomRoute()
        {
            if (m_sipRoutes.Count > 0)
            {
                m_sipRoutes.RemoveAt(m_sipRoutes.Count - 1);
            };
        }

        public SIPRouteSet Reversed()
        {
            if (m_sipRoutes != null && m_sipRoutes.Count > 0)
            {
                SIPRouteSet reversedSet = new SIPRouteSet();

                for (int index = 0; index < m_sipRoutes.Count; index++)
                {
                    reversedSet.PushRoute(m_sipRoutes[index]);
                }

                return reversedSet;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// If a route set is travelling from the public side of a proxy to the private side it can be required that the Record-Route set is modified.
        /// </summary>
        /// <param name="origSocket">The socket string in the original route set that needs to be replace.</param>
        /// <param name="replacementSocket">The socket string the original route is being replaced with.</param>
        public void ReplaceRoute(string origSocket, string replacementSocket)
        {
            foreach (SIPRoute route in m_sipRoutes)
            {
                if (route.Host == origSocket)
                {
                    route.Host = replacementSocket;
                }
            }
        }

        public new string ToString()
        {
            string routeStr = null;

            if (m_sipRoutes != null && m_sipRoutes.Count > 0)
            {
                for (int routeIndex = 0; routeIndex < m_sipRoutes.Count; routeIndex++)
                {
                    routeStr += (routeStr != null) ? "," + m_sipRoutes[routeIndex].ToString() : m_sipRoutes[routeIndex].ToString();
                }
            }

            return routeStr;
        }
    }

    public class SIPViaSet
    {
        private static string m_CRLF = SIPConstants.CRLF;

        private List<SIPViaHeader> m_viaHeaders = new List<SIPViaHeader>();

        public int Length
        {
            get { return m_viaHeaders.Count; }
        }

        public List<SIPViaHeader> Via
        {
            get
            {
                return m_viaHeaders;
            }
            set
            {
                m_viaHeaders = value;
            }
        }

        public SIPViaHeader TopViaHeader
        {
            get
            {
                if (m_viaHeaders != null && m_viaHeaders.Count > 0)
                {
                    return m_viaHeaders[0];
                }
                else
                {
                    return null;
                }
            }
        }

        public SIPViaHeader BottomViaHeader
        {
            get
            {
                if (m_viaHeaders != null && m_viaHeaders.Count > 0)
                {
                    return m_viaHeaders[m_viaHeaders.Count - 1];
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Pops top Via header off the array.
        /// </summary>
        public SIPViaHeader PopTopViaHeader()
        {
            SIPViaHeader topHeader = m_viaHeaders[0];
            m_viaHeaders.RemoveAt(0);

            return topHeader;
        }

        public void AddBottomViaHeader(SIPViaHeader viaHeader)
        {
            m_viaHeaders.Add(viaHeader);
        }

        /// <summary>
        /// Updates the topmost Via header by setting the received and rport parameters to the IP address and port
        /// the request came from.
        /// </summary>
        /// <remarks>The setting of the received parameter is documented in RFC3261 section 18.2.1 and in RFC3581
        /// section 4. RFC3581 states that the received parameter value must be set even if it's the same as the 
        /// address in the sent from field. The setting of the rport parameter is documented in RFC3581 section 4.
        /// An attempt was made to comply with the RFC3581 standard and only set the rport parameter if it was included
        /// by the client user agent however in the wild there are too many user agents that are behind symmetric NATs 
        /// not setting an empty rport and if it's not added then they will not be able to communicate.
        /// </remarks>
        /// <param name="msgRcvdEndPoint">The remote endpoint the request was received from.</param>
        public void UpateTopViaHeader(IPEndPoint msgRcvdEndPoint)
        {
            // Update the IP Address and port that this request was received on.
            SIPViaHeader topViaHeader = this.TopViaHeader;

            topViaHeader.ReceivedFromIPAddress = msgRcvdEndPoint.Address.ToString();
            topViaHeader.ReceivedFromPort = msgRcvdEndPoint.Port;
        }

        /// <summary>
        /// Pushes a new Via header onto the top of the array.
        /// </summary>
        public void PushViaHeader(SIPViaHeader viaHeader)
        {
            m_viaHeaders.Insert(0, viaHeader);
        }

        public new string ToString()
        {
            string viaStr = null;

            if (m_viaHeaders != null && m_viaHeaders.Count > 0)
            {
                for (int viaIndex = 0; viaIndex < m_viaHeaders.Count; viaIndex++)
                {
                    viaStr += (m_viaHeaders[viaIndex]).ToString() + m_CRLF;
                }
            }

            return viaStr;
        }
    }

    /// <bnf>
    /// header  =  "header-name" HCOLON header-value *(COMMA header-value)
    /// field-name: field-value CRLF
    /// </bnf>
    public class SIPHeader
    {
        public const int DEFAULT_CSEQ = 100;

        private static ILogger logger = Log.Logger;
        private static string m_CRLF = SIPConstants.CRLF;

        // RFC SIP headers.
        public string Accept;
        public string AcceptEncoding;
        public string AcceptLanguage;
        public string AlertInfo;
        public string Allow;
        public string AllowEvents;                          // RFC3265 SIP Events.
        public string AuthenticationInfo;
        public SIPAuthenticationHeader AuthenticationHeader;
        public string CallId;
        public string CallInfo;
        public List<SIPContactHeader> Contact = new List<SIPContactHeader>();
        public string ContentDisposition;
        public string ContentEncoding;
        public string ContentLanguage;
        public string ContentType;
        public int ContentLength = 0;
        public int CSeq = -1;
        public SIPMethodsEnum CSeqMethod;
        public string Date;
        public string ErrorInfo;
        public string ETag;                                 // added by Tilmann: look RFC3903
        public string Event;                                // RFC3265 SIP Events.
        public int Expires = -1;
        public SIPFromHeader From;
        public string InReplyTo;
        public int MinExpires = -1;
        public int MaxForwards = SIPConstants.DEFAULT_MAX_FORWARDS;
        public string MIMEVersion;
        public string Organization;
        public string Priority;
        public string ProxyRequire;
        public int RAckCSeq = -1;                          // RFC3262 the CSeq number the PRACK request is acknowledging.
        public SIPMethodsEnum RAckCSeqMethod;              // RFC3262 the CSeq method from the response the PRACK request is acknowledging.
        public int RAckRSeq = -1;                          // RFC3262 the RSeq number the PRACK request is acknowledging.
        public string Reason;
        public SIPRouteSet RecordRoutes = new SIPRouteSet();
        public string ReferredBy;                           // RFC 3515 "The Session Initiation Protocol (SIP) Refer Method"
        public string ReferSub;                             // RFC 4488 If set to false indicates the implicit REFER subscription should not be created.
        public string ReferTo;                              // RFC 3515 "The Session Initiation Protocol (SIP) Refer Method"
        public string ReplyTo;
        public string Replaces;
        public string Require;
        public string RetryAfter;
        public int RSeq = -1;                               // RFC3262 reliable provisional response sequence number.
        public SIPRouteSet Routes = new SIPRouteSet();
        public string Server;
        public string Subject;
        public string SubscriptionState;                    // RFC3265 SIP Events.
        public string Supported;
        public string Timestamp;
        public SIPToHeader To;
        public string Unsupported;
        public string UserAgent;
        public SIPViaSet Vias = new SIPViaSet();
        public string Warning;

        // Non-core custom SIP headers used to allow a SIP Proxy to communicate network info to internal server agents.
        public string ProxyReceivedOn;          // The Proxy socket that the SIP message was received on.
        public string ProxyReceivedFrom;        // The remote socket that the Proxy received the SIP message on.
        public string ProxySendFrom;            // The Proxy socket that the SIP message should be transmitted from.

        public List<string> UnknownHeaders = new List<string>();    // Holds any unrecognised headers.

        public List<SIPExtensions> RequiredExtensions = new List<SIPExtensions>();
        public string UnknownRequireExtension = null;
        public List<SIPExtensions> SupportedExtensions = new List<SIPExtensions>();

        public SIPHeader()
        { }

        public SIPHeader(string fromHeader, string toHeader, int cseq, string callId)
        {
            SIPFromHeader from = SIPFromHeader.ParseFromHeader(fromHeader);
            SIPToHeader to = SIPToHeader.ParseToHeader(toHeader);
            Initialise(null, from, to, cseq, callId);
        }

        public SIPHeader(string fromHeader, string toHeader, string contactHeader, int cseq, string callId)
        {
            SIPFromHeader from = SIPFromHeader.ParseFromHeader(fromHeader);
            SIPToHeader to = SIPToHeader.ParseToHeader(toHeader);
            List<SIPContactHeader> contact = SIPContactHeader.ParseContactHeader(contactHeader);
            Initialise(contact, from, to, cseq, callId);
        }

        public SIPHeader(SIPFromHeader from, SIPToHeader to, int cseq, string callId)
        {
            Initialise(null, from, to, cseq, callId);
        }

        public SIPHeader(SIPContactHeader contact, SIPFromHeader from, SIPToHeader to, int cseq, string callId)
        {
            List<SIPContactHeader> contactList = new List<SIPContactHeader>();
            if (contact != null)
            {
                contactList.Add(contact);
            }

            Initialise(contactList, from, to, cseq, callId);
        }

        public SIPHeader(List<SIPContactHeader> contactList, SIPFromHeader from, SIPToHeader to, int cseq, string callId)
        {
            Initialise(contactList, from, to, cseq, callId);
        }

        private void Initialise(List<SIPContactHeader> contact, SIPFromHeader from, SIPToHeader to, int cseq, string callId)
        {
            if (from == null)
            {
                throw new ApplicationException("The From header cannot be empty when creating a new SIP header.");
            }

            if (to == null)
            {
                throw new ApplicationException("The To header cannot be empty when creating a new SIP header.");
            }

            if (callId == null || callId.Trim().Length == 0)
            {
                throw new ApplicationException("The CallId header cannot be empty when creating a new SIP header.");
            }

            From = from;
            To = to;
            Contact = contact;
            CallId = callId;

            if (cseq > 0 && cseq < Int32.MaxValue)
            {
                CSeq = cseq;
            }
            else
            {
                CSeq = DEFAULT_CSEQ;
            }
        }

        public static string[] SplitHeaders(string message)
        {
            // SIP headers can be extended across lines if the first character of the next line is at least on whitespace character.
            message = Regex.Replace(message, m_CRLF + @"\s+", " ", RegexOptions.Singleline);

            // Some user agents couldn't get the \r\n bit right.
            message = Regex.Replace(message, "\r ", m_CRLF, RegexOptions.Singleline);

            return Regex.Split(message, m_CRLF);
        }

        public static SIPHeader ParseSIPHeaders(string[] headersCollection)
        {
            try
            {
                SIPHeader sipHeader = new SIPHeader();
                sipHeader.MaxForwards = -1;     // This allows detection of whether this header is present or not.
                string lastHeader = null;

                for (int lineIndex = 0; lineIndex < headersCollection.Length; lineIndex++)
                {
                    string headerLine = headersCollection[lineIndex];

                    if (headerLine.IsNullOrBlank())
                    {
                        // No point processing blank headers.
                        continue;
                    }

                    string headerName = null;
                    string headerValue = null;

                    // If the first character of a line is whitespace it's a continuation of the previous line.
                    if (headerLine.StartsWith(" "))
                    {
                        headerName = lastHeader;
                        headerValue = headerLine.Trim();
                    }
                    else
                    {
                        headerLine = headerLine.Trim();
                        int delimiterIndex = headerLine.IndexOf(SIPConstants.HEADER_DELIMITER_CHAR);

                        if (delimiterIndex == -1)
                        {
                            logger.LogWarning("Invalid SIP header, ignoring, " + headerLine + ".");
                            continue;
                        }

                        headerName = headerLine.Substring(0, delimiterIndex).Trim();
                        headerValue = headerLine.Substring(delimiterIndex + 1).Trim();
                    }

                    try
                    {
                        string headerNameLower = headerName.ToLower();

                        #region Via
                        if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_VIA ||
                            headerNameLower == SIPHeaders.SIP_HEADER_VIA.ToLower())
                        {
                            //sipHeader.RawVia += headerValue;

                            SIPViaHeader[] viaHeaders = SIPViaHeader.ParseSIPViaHeader(headerValue);

                            if (viaHeaders != null && viaHeaders.Length > 0)
                            {
                                foreach (SIPViaHeader viaHeader in viaHeaders)
                                {
                                    sipHeader.Vias.AddBottomViaHeader(viaHeader);
                                }
                            }
                        }
                        #endregion
                        #region CallId
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_CALLID ||
                                headerNameLower == SIPHeaders.SIP_HEADER_CALLID.ToLower())
                        {
                            sipHeader.CallId = headerValue;
                        }
                        #endregion
                        #region CSeq
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_CSEQ.ToLower())
                        {
                            //sipHeader.RawCSeq += headerValue;

                            string[] cseqFields = headerValue.Split(' ');
                            if (cseqFields == null || cseqFields.Length == 0)
                            {
                                logger.LogWarning("The " + SIPHeaders.SIP_HEADER_CSEQ + " was empty.");
                            }
                            else
                            {
                                if (!Int32.TryParse(cseqFields[0], out sipHeader.CSeq))
                                {
                                    logger.LogWarning(SIPHeaders.SIP_HEADER_CSEQ + " did not contain a valid integer, " + headerLine + ".");
                                }

                                if (cseqFields != null && cseqFields.Length > 1)
                                {
                                    sipHeader.CSeqMethod = SIPMethods.GetMethod(cseqFields[1]);
                                }
                                else
                                {
                                    logger.LogWarning("There was no " + SIPHeaders.SIP_HEADER_CSEQ + " method, " + headerLine + ".");
                                }
                            }
                        }
                        #endregion
                        #region Expires
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_EXPIRES.ToLower())
                        {
                            //sipHeader.RawExpires += headerValue;

                            if (!Int32.TryParse(headerValue, out sipHeader.Expires))
                            {
                                logger.LogWarning("The Expires value was not a valid integer, " + headerLine + ".");
                            }
                        }
                        #endregion
                        #region Min-Expires
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_MINEXPIRES.ToLower())
                        {
                            if (!Int32.TryParse(headerValue, out sipHeader.MinExpires))
                            {
                                logger.LogWarning("The Min-Expires value was not a valid integer, " + headerLine + ".");
                            }
                        }
                        #endregion
                        #region Contact
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_CONTACT ||
                            headerNameLower == SIPHeaders.SIP_HEADER_CONTACT.ToLower())
                        {
                            List<SIPContactHeader> contacts = SIPContactHeader.ParseContactHeader(headerValue);
                            if (contacts != null && contacts.Count > 0)
                            {
                                sipHeader.Contact.AddRange(contacts);
                            }
                        }
                        #endregion
                        #region From
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_FROM ||
                             headerNameLower == SIPHeaders.SIP_HEADER_FROM.ToLower())
                        {
                            //sipHeader.RawFrom = headerValue;
                            sipHeader.From = SIPFromHeader.ParseFromHeader(headerValue);
                        }
                        #endregion
                        #region To
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_TO ||
                            headerNameLower == SIPHeaders.SIP_HEADER_TO.ToLower())
                        {
                            //sipHeader.RawTo = headerValue;
                            sipHeader.To = SIPToHeader.ParseToHeader(headerValue);
                        }
                        #endregion
                        #region WWWAuthenticate
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_WWWAUTHENTICATE.ToLower())
                        {
                            //sipHeader.RawAuthentication = headerValue;
                            sipHeader.AuthenticationHeader = SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, headerValue);
                        }
                        #endregion
                        #region Authorization
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_AUTHORIZATION.ToLower())
                        {
                            //sipHeader.RawAuthentication = headerValue;
                            sipHeader.AuthenticationHeader = SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.Authorize, headerValue);
                        }
                        #endregion
                        #region ProxyAuthentication
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_PROXYAUTHENTICATION.ToLower())
                        {
                            //sipHeader.RawAuthentication = headerValue;
                            sipHeader.AuthenticationHeader = SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.ProxyAuthenticate, headerValue);
                        }
                        #endregion
                        #region ProxyAuthorization
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_PROXYAUTHORIZATION.ToLower())
                        {
                            sipHeader.AuthenticationHeader = SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.ProxyAuthorization, headerValue);
                        }
                        #endregion
                        #region UserAgent
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_USERAGENT.ToLower())
                        {
                            sipHeader.UserAgent = headerValue;
                        }
                        #endregion
                        #region MaxForwards
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_MAXFORWARDS.ToLower())
                        {
                            if (!Int32.TryParse(headerValue, out sipHeader.MaxForwards))
                            {
                                logger.LogWarning("The " + SIPHeaders.SIP_HEADER_MAXFORWARDS + " could not be parsed as a valid integer, " + headerLine + ".");
                            }
                        }
                        #endregion
                        #region ContentLength
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_CONTENTLENGTH ||
                            headerNameLower == SIPHeaders.SIP_HEADER_CONTENTLENGTH.ToLower())
                        {
                            if (!Int32.TryParse(headerValue, out sipHeader.ContentLength))
                            {
                                logger.LogWarning("The " + SIPHeaders.SIP_HEADER_CONTENTLENGTH + " could not be parsed as a valid integer.");
                            }
                        }
                        #endregion
                        #region ContentType
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_CONTENTTYPE ||
                            headerNameLower == SIPHeaders.SIP_HEADER_CONTENTTYPE.ToLower())
                        {
                            sipHeader.ContentType = headerValue;
                        }
                        #endregion
                        #region Accept
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_ACCEPT.ToLower())
                        {
                            sipHeader.Accept = headerValue;
                        }
                        #endregion
                        #region Route
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_ROUTE.ToLower())
                        {
                            SIPRouteSet routeSet = SIPRouteSet.ParseSIPRouteSet(headerValue);
                            if (routeSet != null)
                            {
                                while (routeSet.Length > 0)
                                {
                                    sipHeader.Routes.AddBottomRoute(routeSet.PopRoute());
                                }
                            }
                        }
                        #endregion
                        #region RecordRoute
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_RECORDROUTE.ToLower())
                        {
                            SIPRouteSet recordRouteSet = SIPRouteSet.ParseSIPRouteSet(headerValue);
                            if (recordRouteSet != null)
                            {
                                while (recordRouteSet.Length > 0)
                                {
                                    sipHeader.RecordRoutes.AddBottomRoute(recordRouteSet.PopRoute());
                                }
                            }
                        }
                        #endregion
                        #region Allow-Events
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_ALLOW_EVENTS || headerNameLower == SIPHeaders.SIP_COMPACTHEADER_ALLOWEVENTS)
                        {
                            sipHeader.AllowEvents = headerValue;
                        }
                        #endregion
                        #region Event
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_EVENT.ToLower() || headerNameLower == SIPHeaders.SIP_COMPACTHEADER_EVENT)
                        {
                            sipHeader.Event = headerValue;
                        }
                        #endregion
                        #region SubscriptionState.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_SUBSCRIPTION_STATE.ToLower())
                        {
                            sipHeader.SubscriptionState = headerValue;
                        }
                        #endregion
                        #region Timestamp.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_TIMESTAMP.ToLower())
                        {
                            sipHeader.Timestamp = headerValue;
                        }
                        #endregion
                        #region Date.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_DATE.ToLower())
                        {
                            sipHeader.Date = headerValue;
                        }
                        #endregion
                        #region Refer-Sub.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_REFERSUB.ToLower())
                        {
                            if (sipHeader.ReferSub == null)
                            {
                                sipHeader.ReferSub = headerValue;
                            }
                            else
                            {
                                throw new SIPValidationException(SIPValidationFieldsEnum.ReferToHeader, "Only a single Refer-Sub header is permitted.");
                            }
                        }
                        #endregion
                        #region Refer-To.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_REFERTO.ToLower() ||
                            headerNameLower == SIPHeaders.SIP_COMPACTHEADER_REFERTO)
                        {
                            if (sipHeader.ReferTo == null)
                            {
                                sipHeader.ReferTo = headerValue;
                            }
                            else
                            {
                                throw new SIPValidationException(SIPValidationFieldsEnum.ReferToHeader, "Only a single Refer-To header is permitted.");
                            }
                        }
                        #endregion
                        #region Referred-By.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_REFERREDBY.ToLower())
                        {
                            sipHeader.ReferredBy = headerValue;
                        }
                        #endregion
                        #region Replaces.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_REPLACES.ToLower())
                        {
                            sipHeader.Replaces = headerValue;
                        }
                        #endregion
                        #region Require.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_REQUIRE.ToLower())
                        {
                            sipHeader.Require = headerValue;

                            if (!String.IsNullOrEmpty(sipHeader.Require))
                            {
                                sipHeader.RequiredExtensions = SIPExtensionHeaders.ParseSIPExtensions(sipHeader.Require, out sipHeader.UnknownRequireExtension);
                            }
                        }
                        #endregion
                        #region Reason.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_REASON.ToLower())
                        {
                            sipHeader.Reason = headerValue;
                        }
                        #endregion
                        #region Proxy-ReceivedFrom.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_PROXY_RECEIVEDFROM.ToLower())
                        {
                            sipHeader.ProxyReceivedFrom = headerValue;
                        }
                        #endregion
                        #region Proxy-ReceivedOn.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_PROXY_RECEIVEDON.ToLower())
                        {
                            sipHeader.ProxyReceivedOn = headerValue;
                        }
                        #endregion
                        #region Proxy-SendFrom.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_PROXY_SENDFROM.ToLower())
                        {
                            sipHeader.ProxySendFrom = headerValue;
                        }
                        #endregion
                        #region Supported
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_SUPPORTED ||
                            headerNameLower == SIPHeaders.SIP_HEADER_SUPPORTED.ToLower())
                        {
                            sipHeader.Supported = headerValue;

                            if (!String.IsNullOrEmpty(sipHeader.Supported))
                            {
                                sipHeader.SupportedExtensions = SIPExtensionHeaders.ParseSIPExtensions(sipHeader.Supported, out _);
                            }
                        }
                        #endregion
                        #region Authentication-Info
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_AUTHENTICATIONINFO.ToLower())
                        {
                            sipHeader.AuthenticationInfo = headerValue;
                        }
                        #endregion
                        #region Accept-Encoding
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_ACCEPTENCODING.ToLower())
                        {
                            sipHeader.AcceptEncoding = headerValue;
                        }
                        #endregion
                        #region Accept-Language
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_ACCEPTLANGUAGE.ToLower())
                        {
                            sipHeader.AcceptLanguage = headerValue;
                        }
                        #endregion
                        #region Alert-Info
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_ALERTINFO.ToLower())
                        {
                            sipHeader.AlertInfo = headerValue;
                        }
                        #endregion
                        #region Allow
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_ALLOW.ToLower())
                        {
                            sipHeader.Allow = headerValue;
                        }
                        #endregion
                        #region Call-Info
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_CALLINFO.ToLower())
                        {
                            sipHeader.CallInfo = headerValue;
                        }
                        #endregion
                        #region Content-Disposition
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_CONTENT_DISPOSITION.ToLower())
                        {
                            sipHeader.ContentDisposition = headerValue;
                        }
                        #endregion
                        #region Content-Encoding
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_CONTENT_ENCODING.ToLower())
                        {
                            sipHeader.ContentEncoding = headerValue;
                        }
                        #endregion
                        #region Content-Language
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_CONTENT_LANGUAGE.ToLower())
                        {
                            sipHeader.ContentLanguage = headerValue;
                        }
                        #endregion
                        #region Error-Info
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_ERROR_INFO.ToLower())
                        {
                            sipHeader.ErrorInfo = headerValue;
                        }
                        #endregion
                        #region In-Reply-To
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_IN_REPLY_TO.ToLower())
                        {
                            sipHeader.InReplyTo = headerValue;
                        }
                        #endregion
                        #region MIME-Version
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_MIME_VERSION.ToLower())
                        {
                            sipHeader.MIMEVersion = headerValue;
                        }
                        #endregion
                        #region Organization
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_ORGANIZATION.ToLower())
                        {
                            sipHeader.Organization = headerValue;
                        }
                        #endregion
                        #region Priority
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_PRIORITY.ToLower())
                        {
                            sipHeader.Priority = headerValue;
                        }
                        #endregion
                        #region Proxy-Require
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_PROXY_REQUIRE.ToLower())
                        {
                            sipHeader.ProxyRequire = headerValue;
                        }
                        #endregion
                        #region Reply-To
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_REPLY_TO.ToLower())
                        {
                            sipHeader.ReplyTo = headerValue;
                        }
                        #endregion
                        #region Retry-After
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_RETRY_AFTER.ToLower())
                        {
                            sipHeader.RetryAfter = headerValue;
                        }
                        #endregion
                        #region Subject
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_SUBJECT.ToLower())
                        {
                            sipHeader.Subject = headerValue;
                        }
                        #endregion
                        #region Unsupported
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_UNSUPPORTED.ToLower())
                        {
                            sipHeader.Unsupported = headerValue;
                        }
                        #endregion
                        #region Warning
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_WARNING.ToLower())
                        {
                            sipHeader.Warning = headerValue;
                        }
                        #endregion
                        #region ETag
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_ETAG.ToLower())
                        {
                            sipHeader.ETag = headerValue;
                        }
                        #endregion
                        #region RAck
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_RELIABLE_ACK.ToLower())
                        {
                            string[] rackFields = headerValue.Split(' ');
                            if (rackFields?.Length == 0)
                            {
                                logger.LogWarning("The " + SIPHeaders.SIP_HEADER_RELIABLE_ACK + " was empty.");
                            }
                            else
                            {
                                if (!Int32.TryParse(rackFields[0], out sipHeader.RAckRSeq))
                                {
                                    logger.LogWarning(SIPHeaders.SIP_HEADER_RELIABLE_ACK + " did not contain a valid integer for the RSeq being acknowledged, " + headerLine + ".");
                                }

                                if (rackFields?.Length > 1)
                                {
                                    if (!Int32.TryParse(rackFields[1], out sipHeader.RAckCSeq))
                                    {
                                        logger.LogWarning(SIPHeaders.SIP_HEADER_RELIABLE_ACK + " did not contain a valid integer for the CSeq being acknowledged, " + headerLine + ".");
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("There was no " + SIPHeaders.SIP_HEADER_RELIABLE_ACK + " method, " + headerLine + ".");
                                }

                                if (rackFields?.Length > 2)
                                {
                                    sipHeader.RAckCSeqMethod = SIPMethods.GetMethod(rackFields[2]);
                                }
                                else
                                {
                                    logger.LogWarning("There was no " + SIPHeaders.SIP_HEADER_RELIABLE_ACK + " method, " + headerLine + ".");
                                }
                            }
                        }
                        #endregion
                        #region RSeq
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_RELIABLE_SEQ.ToLower())
                        {
                            if (!Int32.TryParse(headerValue, out sipHeader.RSeq))
                            {
                                logger.LogWarning("The Rseq value was not a valid integer, " + headerLine + ".");
                            }
                        }
                        #endregion
                        else
                        {
                            sipHeader.UnknownHeaders.Add(headerLine);
                        }

                        lastHeader = headerName;
                    }
                    catch (SIPValidationException)
                    {
                        throw;
                    }
                    catch (Exception parseExcp)
                    {
                        logger.LogError("Error parsing SIP header " + headerLine + ". " + parseExcp.Message);
                        throw new SIPValidationException(SIPValidationFieldsEnum.Headers, "Unknown error parsing Header.");
                    }
                }

                sipHeader.Validate();

                return sipHeader;
            }
            catch (SIPValidationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception ParseSIPHeaders. " + excp.Message);
                throw new SIPValidationException(SIPValidationFieldsEnum.Headers, "Unknown error parsing Headers.");
            }
        }

        /// <summary>
        /// Puts the SIP headers together into a string ready for transmission.
        /// </summary>
        /// <returns>String representing the SIP headers.</returns>
        public new string ToString()
        {
            try
            {
                StringBuilder headersBuilder = new StringBuilder();

                headersBuilder.Append(Vias.ToString());

                string cseqField = null;
                if (this.CSeq != 0)
                {
                    cseqField = (this.CSeqMethod != SIPMethodsEnum.NONE) ? this.CSeq + " " + this.CSeqMethod.ToString() : this.CSeq.ToString();
                }

                headersBuilder.Append((To != null) ? SIPHeaders.SIP_HEADER_TO + ": " + this.To.ToString() + m_CRLF : null);
                headersBuilder.Append((From != null) ? SIPHeaders.SIP_HEADER_FROM + ": " + this.From.ToString() + m_CRLF : null);
                headersBuilder.Append((CallId != null) ? SIPHeaders.SIP_HEADER_CALLID + ": " + this.CallId + m_CRLF : null);
                headersBuilder.Append((CSeq > 0) ? SIPHeaders.SIP_HEADER_CSEQ + ": " + cseqField + m_CRLF : null);

                #region Appending Contact header.

                if (Contact != null && Contact.Count == 1)
                {
                    headersBuilder.Append(SIPHeaders.SIP_HEADER_CONTACT + ": " + Contact[0].ToString() + m_CRLF);
                }
                else if (Contact != null && Contact.Count > 1)
                {
                    StringBuilder contactsBuilder = new StringBuilder();
                    contactsBuilder.Append(SIPHeaders.SIP_HEADER_CONTACT + ": ");

                    bool firstContact = true;
                    foreach (SIPContactHeader contactHeader in Contact)
                    {
                        if (firstContact)
                        {
                            contactsBuilder.Append(contactHeader.ToString());
                        }
                        else
                        {
                            contactsBuilder.Append("," + contactHeader.ToString());
                        }

                        firstContact = false;
                    }

                    headersBuilder.Append(contactsBuilder.ToString() + m_CRLF);
                }

                #endregion

                headersBuilder.Append((MaxForwards >= 0) ? SIPHeaders.SIP_HEADER_MAXFORWARDS + ": " + this.MaxForwards + m_CRLF : null);
                headersBuilder.Append((Routes != null && Routes.Length > 0) ? SIPHeaders.SIP_HEADER_ROUTE + ": " + Routes.ToString() + m_CRLF : null);
                headersBuilder.Append((RecordRoutes != null && RecordRoutes.Length > 0) ? SIPHeaders.SIP_HEADER_RECORDROUTE + ": " + RecordRoutes.ToString() + m_CRLF : null);
                headersBuilder.Append((UserAgent != null && UserAgent.Trim().Length != 0) ? SIPHeaders.SIP_HEADER_USERAGENT + ": " + this.UserAgent + m_CRLF : null);
                headersBuilder.Append((Expires != -1) ? SIPHeaders.SIP_HEADER_EXPIRES + ": " + this.Expires + m_CRLF : null);
                headersBuilder.Append((MinExpires != -1) ? SIPHeaders.SIP_HEADER_MINEXPIRES + ": " + this.MinExpires + m_CRLF : null);
                headersBuilder.Append((Accept != null) ? SIPHeaders.SIP_HEADER_ACCEPT + ": " + this.Accept + m_CRLF : null);
                headersBuilder.Append((AcceptEncoding != null) ? SIPHeaders.SIP_HEADER_ACCEPTENCODING + ": " + this.AcceptEncoding + m_CRLF : null);
                headersBuilder.Append((AcceptLanguage != null) ? SIPHeaders.SIP_HEADER_ACCEPTLANGUAGE + ": " + this.AcceptLanguage + m_CRLF : null);
                headersBuilder.Append((Allow != null) ? SIPHeaders.SIP_HEADER_ALLOW + ": " + this.Allow + m_CRLF : null);
                headersBuilder.Append((AlertInfo != null) ? SIPHeaders.SIP_HEADER_ALERTINFO + ": " + this.AlertInfo + m_CRLF : null);
                headersBuilder.Append((AuthenticationInfo != null) ? SIPHeaders.SIP_HEADER_AUTHENTICATIONINFO + ": " + this.AuthenticationInfo + m_CRLF : null);
                headersBuilder.Append((AuthenticationHeader != null) ? AuthenticationHeader.ToString() + m_CRLF : null);
                headersBuilder.Append((CallInfo != null) ? SIPHeaders.SIP_HEADER_CALLINFO + ": " + this.CallInfo + m_CRLF : null);
                headersBuilder.Append((ContentDisposition != null) ? SIPHeaders.SIP_HEADER_CONTENT_DISPOSITION + ": " + this.ContentDisposition + m_CRLF : null);
                headersBuilder.Append((ContentEncoding != null) ? SIPHeaders.SIP_HEADER_CONTENT_ENCODING + ": " + this.ContentEncoding + m_CRLF : null);
                headersBuilder.Append((ContentLanguage != null) ? SIPHeaders.SIP_HEADER_CONTENT_LANGUAGE + ": " + this.ContentLanguage + m_CRLF : null);
                headersBuilder.Append((Date != null) ? SIPHeaders.SIP_HEADER_DATE + ": " + Date + m_CRLF : null);
                headersBuilder.Append((ErrorInfo != null) ? SIPHeaders.SIP_HEADER_ERROR_INFO + ": " + this.ErrorInfo + m_CRLF : null);
                headersBuilder.Append((InReplyTo != null) ? SIPHeaders.SIP_HEADER_IN_REPLY_TO + ": " + this.InReplyTo + m_CRLF : null);
                headersBuilder.Append((Organization != null) ? SIPHeaders.SIP_HEADER_ORGANIZATION + ": " + this.Organization + m_CRLF : null);
                headersBuilder.Append((Priority != null) ? SIPHeaders.SIP_HEADER_PRIORITY + ": " + Priority + m_CRLF : null);
                headersBuilder.Append((ProxyRequire != null) ? SIPHeaders.SIP_HEADER_PROXY_REQUIRE + ": " + this.ProxyRequire + m_CRLF : null);
                headersBuilder.Append((ReplyTo != null) ? SIPHeaders.SIP_HEADER_REPLY_TO + ": " + this.ReplyTo + m_CRLF : null);
                headersBuilder.Append((Require != null) ? SIPHeaders.SIP_HEADER_REQUIRE + ": " + Require + m_CRLF : null);
                headersBuilder.Append((RetryAfter != null) ? SIPHeaders.SIP_HEADER_RETRY_AFTER + ": " + this.RetryAfter + m_CRLF : null);
                headersBuilder.Append((Server != null && Server.Trim().Length != 0) ? SIPHeaders.SIP_HEADER_SERVER + ": " + this.Server + m_CRLF : null);
                headersBuilder.Append((Subject != null) ? SIPHeaders.SIP_HEADER_SUBJECT + ": " + Subject + m_CRLF : null);
                headersBuilder.Append((Supported != null) ? SIPHeaders.SIP_HEADER_SUPPORTED + ": " + Supported + m_CRLF : null);
                headersBuilder.Append((Timestamp != null) ? SIPHeaders.SIP_HEADER_TIMESTAMP + ": " + Timestamp + m_CRLF : null);
                headersBuilder.Append((Unsupported != null) ? SIPHeaders.SIP_HEADER_UNSUPPORTED + ": " + Unsupported + m_CRLF : null);
                headersBuilder.Append((Warning != null) ? SIPHeaders.SIP_HEADER_WARNING + ": " + Warning + m_CRLF : null);
                headersBuilder.Append((ETag != null) ? SIPHeaders.SIP_HEADER_ETAG + ": " + ETag + m_CRLF : null);
                headersBuilder.Append(SIPHeaders.SIP_HEADER_CONTENTLENGTH + ": " + this.ContentLength + m_CRLF);
                if (this.ContentType != null && this.ContentType.Trim().Length > 0)
                {
                    headersBuilder.Append(SIPHeaders.SIP_HEADER_CONTENTTYPE + ": " + this.ContentType + m_CRLF);
                }

                // Non-core SIP headers.
                headersBuilder.Append((AllowEvents != null) ? SIPHeaders.SIP_HEADER_ALLOW_EVENTS + ": " + AllowEvents + m_CRLF : null);
                headersBuilder.Append((Event != null) ? SIPHeaders.SIP_HEADER_EVENT + ": " + Event + m_CRLF : null);
                headersBuilder.Append((SubscriptionState != null) ? SIPHeaders.SIP_HEADER_SUBSCRIPTION_STATE + ": " + SubscriptionState + m_CRLF : null);
                headersBuilder.Append((ReferSub != null) ? SIPHeaders.SIP_HEADER_REFERSUB + ": " + ReferSub + m_CRLF : null);
                headersBuilder.Append((ReferTo != null) ? SIPHeaders.SIP_HEADER_REFERTO + ": " + ReferTo + m_CRLF : null);
                headersBuilder.Append((ReferredBy != null) ? SIPHeaders.SIP_HEADER_REFERREDBY + ": " + ReferredBy + m_CRLF : null);
                headersBuilder.Append((Replaces != null) ? SIPHeaders.SIP_HEADER_REPLACES + ": " + Replaces + m_CRLF : null);
                headersBuilder.Append((Reason != null) ? SIPHeaders.SIP_HEADER_REASON + ": " + Reason + m_CRLF : null);
                headersBuilder.Append((RSeq != -1) ? SIPHeaders.SIP_HEADER_RELIABLE_SEQ + ": " + RSeq + m_CRLF : null);
                headersBuilder.Append((RAckRSeq != -1) ? SIPHeaders.SIP_HEADER_RELIABLE_ACK + ": " + RAckRSeq + " " + RAckCSeq + " " + RAckCSeqMethod + m_CRLF : null);

                // Custom SIP headers.
                headersBuilder.Append((ProxyReceivedFrom != null) ? SIPHeaders.SIP_HEADER_PROXY_RECEIVEDFROM + ": " + ProxyReceivedFrom + m_CRLF : null);
                headersBuilder.Append((ProxyReceivedOn != null) ? SIPHeaders.SIP_HEADER_PROXY_RECEIVEDON + ": " + ProxyReceivedOn + m_CRLF : null);
                headersBuilder.Append((ProxySendFrom != null) ? SIPHeaders.SIP_HEADER_PROXY_SENDFROM + ": " + ProxySendFrom + m_CRLF : null);

                // Unknown SIP headers
                foreach (string unknownHeader in UnknownHeaders)
                {
                    headersBuilder.Append(unknownHeader + m_CRLF);
                }

                return headersBuilder.ToString();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPHeader ToString. " + excp.Message);
                throw;
            }
        }

        private void Validate()
        {
            if (Vias == null || Vias.Length == 0)
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "Invalid header, no Via.");
            }
        }

        public void SetDateHeader()
        {
            Date = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss ") + "GMT";
        }

        public SIPHeader Copy()
        {
            string headerString = this.ToString();
            string[] sipHeaders = SIPHeader.SplitHeaders(headerString);
            return ParseSIPHeaders(sipHeaders);
        }

        public string GetUnknownHeaderValue(string unknownHeaderName)
        {
            if (unknownHeaderName.IsNullOrBlank())
            {
                return null;
            }
            else if (UnknownHeaders == null || UnknownHeaders.Count == 0)
            {
                return null;
            }
            else
            {
                foreach (string unknonwHeader in UnknownHeaders)
                {
                    string trimmedHeader = unknonwHeader.Trim();
                    int delimiterIndex = trimmedHeader.IndexOf(SIPConstants.HEADER_DELIMITER_CHAR);

                    if (delimiterIndex == -1)
                    {
                        logger.LogWarning("Invalid SIP header, ignoring, " + unknonwHeader + ".");
                        continue;
                    }

                    string headerName = trimmedHeader.Substring(0, delimiterIndex).Trim();

                    if (headerName.ToLower() == unknownHeaderName.ToLower())
                    {
                        return trimmedHeader.Substring(delimiterIndex + 1).Trim();
                    }
                }

                return null;
            }
        }
    }
}
