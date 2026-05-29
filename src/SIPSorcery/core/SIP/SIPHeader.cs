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
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.Extensions.Logging;
using Polyfills;
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
                                return $"[{ipEndPoint.Address}]";
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
                    return $"{Host}:{Port}";
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
                        return $"{Host}:{ReceivedFromPort}";
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
                        return $"{Host}:{Port}";
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
                    if (string.IsNullOrWhiteSpace(viaHeaderStrItem))
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
                            var headerSpan = header.AsSpan();
                            var versionAndTransport = headerSpan.Slice(0, firstSpacePosn);
                            var transportDelimiterIndex = versionAndTransport.LastIndexOf('/');
                            viaHeader.Version = versionAndTransport.Slice(0, transportDelimiterIndex).ToString();
                            viaHeader.Transport = SIPProtocolsType.GetProtocolType(versionAndTransport.Slice(transportDelimiterIndex + 1).ToString());

                            var nextField = headerSpan.Slice(firstSpacePosn).Trim().ToString();

                            int delimIndex = nextField.IndexOf(';');
                            string contactAddress = null;

                            // Some user agents include branch but have the semi-colon missing, that's easy to cope with by replacing "branch" with ";branch".
                            if (delimIndex == -1 && nextField.Contains(m_branchKey))
                            {
                                nextField = nextField.Replace(m_branchKey, $";{m_branchKey}");
                                delimIndex = nextField.IndexOf(';');
                            }

                            if (delimIndex == -1)
                            {
                                //logger.LogWarning("Via header missing semi-colon: {header}.", header);
                                //parserError = SIPValidationError.NoBranchOnVia;
                                //return null;
                                contactAddress = nextField.Trim();
                            }
                            else
                            {
                                contactAddress = nextField.AsSpan(0, delimIndex).Trim().ToString();
                                viaHeader.ViaParameters = new SIPParameters(nextField.Substring(delimIndex, nextField.Length - delimIndex), m_paramDelimChar);
                            }

                            if (string.IsNullOrWhiteSpace(contactAddress))
                            {
                                // Check that the branch parameter is present, without it the Via header is illegal.
                                //if (!viaHeader.ViaParameters.Has(m_branchKey))
                                //{
                                //    logger.LogWarning("Via header missing branch: {header}.", header);
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

        public override string ToString()
        {
            return $"{SIPHeaders.SIP_HEADER_VIA}: {this.Version}/{this.Transport.ToString().ToUpper()} {ContactAddress}{((ViaParameters != null && ViaParameters.Count > 0) ? ViaParameters.ToString() : null)}";
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
                if (!string.IsNullOrWhiteSpace(value))
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
            caller = (!string.IsNullOrEmpty(FromName)) ? $"{FromName} {caller}" : caller;
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
                if (!string.IsNullOrWhiteSpace(value))
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
        public static SIPContactHeader GetDefaultSIPContactHeader(SIPSchemesEnum scheme)
        {
            return new SIPContactHeader(null, new SIPURI(scheme, IPAddress.Any, 0));
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
        public long Expires
        {
            get
            {
                long expires = -1;

                if (ContactParameters.Has(EXPIRES_PARAMETER_KEY))
                {
                    string expiresStr = ContactParameters.Get(EXPIRES_PARAMETER_KEY);
                    Int64.TryParse(expiresStr, out expires);
                    if (expires > UInt32.MaxValue)
                    {
                        expires = UInt32.MaxValue;
                    }
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
                if (string.IsNullOrWhiteSpace(contactHeaderStr))
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
                throw new SIPValidationException(SIPValidationFieldsEnum.ContactHeader, $"Contact header invalid, parse failed. {excp.Message}");
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
        public string Value;
        public SIPAuthorisationHeadersEnum AuthorisationType;

        private SIPAuthenticationHeader() : this(new SIPAuthorisationDigest())
        {
        }

        public SIPAuthenticationHeader(SIPAuthorisationDigest sipDigest)
        {
            SIPDigest = sipDigest;
            Value = string.Empty;
            AuthorisationType = sipDigest?.AuthorisationType ?? SIPAuthorisationHeadersEnum.Authorize;
        }

        public SIPAuthenticationHeader(SIPAuthorisationHeadersEnum authorisationType, string realm, string nonce)
        {
            SIPDigest = new SIPAuthorisationDigest(authorisationType)
            {
                Realm = realm,
                Nonce = nonce
            };
            Value = string.Empty;
            AuthorisationType = authorisationType;
        }

        public static SIPAuthenticationHeader ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum authorizationType, string headerValue)
        {
            try
            {
                var authHeader = new SIPAuthenticationHeader
                {
                    Value = headerValue
                };
                if (headerValue.StartsWith(SIPAuthorisationDigest.METHOD, StringComparison.OrdinalIgnoreCase))
                {
                    authHeader.SIPDigest = SIPAuthorisationDigest.ParseAuthorisationDigest(authorizationType, headerValue);
                }
                else
                {
                    authHeader.SIPDigest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.Unknown);
                }
                authHeader.AuthorisationType = authHeader.SIPDigest.AuthorisationType;
                return authHeader;
            }
            catch
            {
                throw new ApplicationException($"Error parsing SIP authentication header request, {headerValue}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string BuildAuthorisationHeaderName(SIPAuthorisationHeadersEnum authorisationHeaderType)
        {
            string authHeader = null;
            if (authorisationHeaderType == SIPAuthorisationHeadersEnum.Authorize)
            {
                authHeader = $"{SIPHeaders.SIP_HEADER_AUTHORIZATION}: ";
            }
            else if (authorisationHeaderType == SIPAuthorisationHeadersEnum.ProxyAuthenticate)
            {
                authHeader = $"{SIPHeaders.SIP_HEADER_PROXYAUTHENTICATION}: ";
            }
            else if (authorisationHeaderType == SIPAuthorisationHeadersEnum.ProxyAuthorization)
            {
                authHeader = $"{SIPHeaders.SIP_HEADER_PROXYAUTHORIZATION}: ";
            }
            else if (authorisationHeaderType == SIPAuthorisationHeadersEnum.WWWAuthenticate)
            {
                authHeader = $"{SIPHeaders.SIP_HEADER_WWWAUTHENTICATE}: ";
            }
            else
            {
                authHeader = $"{SIPHeaders.SIP_HEADER_AUTHORIZATION}: ";
            }

            return authHeader;
        }

        public override string ToString()
        {
            if (SIPDigest != null)
            {
                var authorisationHeaderType = (SIPDigest.AuthorisationResponseType != SIPAuthorisationHeadersEnum.Unknown) ? SIPDigest.AuthorisationResponseType : SIPDigest.AuthorisationType;
                string authHeader = BuildAuthorisationHeaderName(authorisationHeaderType);
                return authHeader + SIPDigest.ToString();
            }
            else if (!string.IsNullOrEmpty(Value))
            {
                string authHeader = BuildAuthorisationHeaderName(AuthorisationType);
                return authHeader + Value;
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
            m_sipRoutes.Insert(0, new SIPRoute($"{scheme}:{socket.ToString()}", true));
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
            }
            ;
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

        public override string ToString()
        {
            if (m_sipRoutes != null && m_sipRoutes.Count > 0)
            {
                var routeStr = new StringBuilder();
                for (int routeIndex = 0; routeIndex < m_sipRoutes.Count; routeIndex++)
                {
                    if (routeIndex > 0)
                    {
                        routeStr.Append(',');
                    }

                    routeStr.Append(m_sipRoutes[routeIndex].ToString());
                }

                return routeStr.ToString();
            }

            return null;
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

        public override string ToString()
        {
            if (m_viaHeaders != null && m_viaHeaders.Count > 0)
            {
                var viaStr = new StringBuilder();
                for (int viaIndex = 0; viaIndex < m_viaHeaders.Count; viaIndex++)
                {
                    viaStr.Append(m_viaHeaders[viaIndex].ToString()).Append(m_CRLF);
                }

                return viaStr.ToString();
            }

            return null;
        }
    }

    /// <summary>
    /// Class used to parse History-Info, Diversion, P-Asserted-Identity Headers.
    /// </summary>
    public class SIPUriHeader
    {
        public static SIPUriHeader GetDefaultHeader(SIPSchemesEnum scheme)
        {
            return new SIPUriHeader(null, new SIPURI(scheme, IPAddress.Any, 0));
        }

        public string Name
        {
            get { return m_userField.Name; }
            set { m_userField.Name = value; }
        }

        public SIPURI URI
        {
            get { return m_userField.URI; }
            set { m_userField.URI = value; }
        }

        public SIPParameters Parameters
        {
            get { return m_userField.Parameters; }
            set { m_userField.Parameters = value; }
        }

        private SIPUserField m_userField = new SIPUserField();
        public SIPUserField UserField
        {
            get { return m_userField; }
            set { m_userField = value; }
        }

        public SIPUriHeader()
        { }

        public static bool SortByUriParameter(ref List<SIPUriHeader> header, string paramterForSort, bool descending = false)
        {
            if (header == null)
            {
                return false;
            }

            try
            {
                if (descending)
                {
                    header.Sort((x, y) => y.Parameters.Get(paramterForSort).CompareTo(x.Parameters.Get(paramterForSort)));
                }
                else
                {
                    header.Sort((x, y) => x.Parameters.Get(paramterForSort).CompareTo(y.Parameters.Get(paramterForSort)));
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public SIPUriHeader(string Name, SIPURI URI, string uriParams = null)
        {
            m_userField = new SIPUserField(Name, URI, uriParams);
        }
        public static List<SIPUriHeader> ParseHeader(string headerStr)
        {
            try
            {
                var returnHeaders = new List<SIPUriHeader>();

                string[] uris = SIPParameters.GetKeyValuePairsFromQuoted(headerStr, ',');

                foreach (string uri in uris)
                {
                    var NewHeader = new SIPUriHeader();
                    NewHeader.m_userField = SIPUserField.ParseSIPUserField(uri);
                    returnHeaders.Add(NewHeader);
                }

                return returnHeaders;
            }
            catch (ArgumentException argExcp)
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.Unknown, argExcp.Message);
            }
            catch
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.Unknown, $"One of the SIP SIPMultiUriHeaders was invalid, header: {headerStr}");
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
        /// <returns>A string representing a friendly description of the MultiUri header.</returns>
        public string FriendlyDescription()
        {
            string caller = URI.ToAOR();
            caller = (!string.IsNullOrEmpty(Name)) ? $"{Name} {caller}" : caller;
            return caller;
        }
    }

    /// <bnf>
    /// header  =  "header-name" HCOLON header-value *(COMMA header-value)
    /// field-name: field-value CRLF
    /// </bnf>
    public class SIPHeader
    {
        public const int DEFAULT_CSEQ = 100;

        private static readonly ILogger logger = LogFactory.CreateLogger<SIPHeader>();
        private static string m_CRLF = SIPConstants.CRLF;

        // RFC SIP headers.
        public string Accept;
        public string AcceptEncoding;
        public string AcceptLanguage;
        public string AlertInfo;
        public string Allow;
        public string AllowEvents;                          // RFC3265 SIP Events.
        public string AuthenticationInfo;
        public List<SIPAuthenticationHeader> AuthenticationHeaders = new List<SIPAuthenticationHeader>();
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
        public long Expires = -1;
        public SIPFromHeader From;
        public string InReplyTo;
        public long MinExpires = -1;
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
        public List<SIPUriHeader> PassertedIdentity = new List<SIPUriHeader>();       // RFC3325.

        /// <summary>
        /// It's quaranteed to be sorted from shortest index to longest index.
        /// </summary>
        public List<SIPUriHeader> HistoryInfo = new List<SIPUriHeader>();             // RFC4244.

        public List<SIPUriHeader> Diversion = new List<SIPUriHeader>();               // RFC5806.

        // Non-core custom SIP headers used to allow a SIP Proxy to communicate network info to internal server agents.
        public string ProxyReceivedOn;          // The Proxy socket that the SIP message was received on.
        public string ProxyReceivedFrom;        // The remote socket that the Proxy received the SIP message on.
        public string ProxySendFrom;            // The Proxy socket that the SIP message should be transmitted from.

        public List<string> UnknownHeaders = new List<string>();    // Holds any unrecognised headers.

        public List<SIPExtensions> RequiredExtensions = new List<SIPExtensions>();
        public string UnknownRequireExtension = null;
        public List<SIPExtensions> SupportedExtensions = new List<SIPExtensions>();

        public bool HasAuthenticationHeader => AuthenticationHeaders.Count > 0;

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

            if (string.IsNullOrWhiteSpace(callId))
            {
                throw new ApplicationException("The CallId header cannot be empty when creating a new SIP header.");
            }

            From = from;
            To = to;
            Contact = contact;
            CallId = callId;

            if (cseq >= 0 && cseq < Int32.MaxValue)
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
            static string NormalizeFoldedHeaderLines(string headerBlock)
            {
                var normalised = default(StringBuilder);
                var segmentStart = 0;
                var position = 0;

                while (position < headerBlock.Length)
                {
                    if (position + 2 < headerBlock.Length &&
                        headerBlock[position] == '\r' &&
                        headerBlock[position + 1] == '\n' &&
                        char.IsWhiteSpace(headerBlock[position + 2]))
                    {
                        normalised ??= new StringBuilder(headerBlock.Length);
                        normalised.Append(headerBlock, segmentStart, position - segmentStart);
                        normalised.Append(' ');

                        position += 2;
                        while (position < headerBlock.Length && char.IsWhiteSpace(headerBlock[position]))
                        {
                            position++;
                        }

                        segmentStart = position;
                        continue;
                    }

                    if (position + 1 < headerBlock.Length &&
                        headerBlock[position] == '\r' &&
                        headerBlock[position + 1] == ' ')
                    {
                        normalised ??= new StringBuilder(headerBlock.Length);
                        normalised.Append(headerBlock, segmentStart, position - segmentStart);
                        normalised.Append(m_CRLF);

                        position += 2;
                        segmentStart = position;
                        continue;
                    }

                    position++;
                }

                if (normalised is null)
                {
                    return headerBlock;
                }

                normalised.Append(headerBlock, segmentStart, headerBlock.Length - segmentStart);
                return normalised.ToString();
            }

            // SIP headers can be extended across lines if the first character of the next line is at least on whitespace character.
            // Some user agents couldn't get the \r\n bit right; normalise those at the same time.
            message = NormalizeFoldedHeaderLines(message);

            var headers = new List<string>();
            var messageSpan = message.AsSpan();

            foreach (var headerRange in messageSpan.Split(m_CRLF.AsSpan()))
            {
                headers.Add(messageSpan[headerRange].ToString());
            }

            return headers.ToArray();
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
                    if (headerLine.StartsWith(" ", StringComparison.Ordinal))
                    {
                        headerName = lastHeader;
                        headerValue = headerLine.Trim();
                    }
                    else
                    {
                        var headerLineSpan = headerLine.AsSpan().Trim();
                        var delimiterIndex = headerLineSpan.IndexOf(SIPConstants.HEADER_DELIMITER_CHAR);

                        if (delimiterIndex == -1)
                        {
                            logger.LogWarning("Invalid SIP header, ignoring {HeaderLine}.", headerLine);
                            continue;
                        }

                        headerLine = headerLineSpan.ToString();
                        headerName = headerLineSpan.Slice(0, delimiterIndex).Trim().ToString();
                        headerValue = headerLineSpan.Slice(delimiterIndex + 1).Trim().ToString();
                    }

                    try
                    {
                        static bool TryGetSpaceSeparatedToken(ReadOnlySpan<char> value, int tokenIndex, out ReadOnlySpan<char> token)
                        {
                            token = value;

                            for (var i = 0; i < tokenIndex; i++)
                            {
                                var separatorIndex = token.IndexOf(' ');
                                if (separatorIndex == -1)
                                {
                                    token = default;
                                    return false;
                                }

                                token = token.Slice(separatorIndex + 1);
                            }

                            var nextSeparatorIndex = token.IndexOf(' ');
                            if (nextSeparatorIndex != -1)
                            {
                                token = token.Slice(0, nextSeparatorIndex);
                            }

                            return true;
                        }

                        bool IsHeaderName(string knownHeaderName) =>
                            string.Equals(headerName, knownHeaderName, StringComparison.OrdinalIgnoreCase);

                        #region Via
                        if (IsHeaderName(SIPHeaders.SIP_COMPACTHEADER_VIA) ||
                            IsHeaderName(SIPHeaders.SIP_HEADER_VIA))
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
                        else if (IsHeaderName(SIPHeaders.SIP_COMPACTHEADER_CALLID) ||
                                IsHeaderName(SIPHeaders.SIP_HEADER_CALLID))
                        {
                            sipHeader.CallId = headerValue;
                        }
                        #endregion
                        #region CSeq
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_CSEQ))
                        {
                            //sipHeader.RawCSeq += headerValue;

                            var cseqFields = headerValue.AsSpan();
                            if (!TryGetSpaceSeparatedToken(cseqFields, 0, out var cseqNumber))
                            {
                                logger.LogWarning("The " + SIPHeaders.SIP_HEADER_CSEQ + " was empty.");
                            }
                            else
                            {
                                if (!int.TryParse(cseqNumber, out sipHeader.CSeq))
                                {
                                    logger.LogWarning(SIPHeaders.SIP_HEADER_CSEQ + " did not contain a valid integer, {HeaderLine}.", headerLine);
                                }

                                if (TryGetSpaceSeparatedToken(cseqFields, 1, out var cseqMethod))
                                {
                                    sipHeader.CSeqMethod = SIPMethods.GetMethod(cseqMethod.ToString());
                                }
                                else
                                {
                                    logger.LogWarning("There was no " + SIPHeaders.SIP_HEADER_CSEQ + " method, {HeaderLine}.", headerLine);
                                }
                            }
                        }
                        #endregion
                        #region Expires
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_EXPIRES))
                        {
                            //sipHeader.RawExpires += headerValue;

                            if (!Int64.TryParse(headerValue, out sipHeader.Expires))
                            {
                                logger.LogWarning("The Expires value was not a valid integer. {HeaderLine}.", headerLine);
                            }
                        }
                        #endregion
                        #region Min-Expires
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_MINEXPIRES))
                        {
                            if (!Int64.TryParse(headerValue, out sipHeader.MinExpires))
                            {
                                logger.LogWarning("The Min-Expires value was not a valid integer. {HeaderLine}.", headerLine);
                            }
                        }
                        #endregion
                        #region Contact
                        else if (IsHeaderName(SIPHeaders.SIP_COMPACTHEADER_CONTACT) ||
                            IsHeaderName(SIPHeaders.SIP_HEADER_CONTACT))
                        {
                            List<SIPContactHeader> contacts = SIPContactHeader.ParseContactHeader(headerValue);
                            if (contacts != null && contacts.Count > 0)
                            {
                                sipHeader.Contact.AddRange(contacts);
                            }
                        }
                        #endregion
                        #region From
                        else if (IsHeaderName(SIPHeaders.SIP_COMPACTHEADER_FROM) ||
                             IsHeaderName(SIPHeaders.SIP_HEADER_FROM))
                        {
                            //sipHeader.RawFrom = headerValue;
                            sipHeader.From = SIPFromHeader.ParseFromHeader(headerValue);
                        }
                        #endregion
                        #region To
                        else if (IsHeaderName(SIPHeaders.SIP_COMPACTHEADER_TO) ||
                            IsHeaderName(SIPHeaders.SIP_HEADER_TO))
                        {
                            //sipHeader.RawTo = headerValue;
                            sipHeader.To = SIPToHeader.ParseToHeader(headerValue);
                        }
                        #endregion
                        #region WWWAuthenticate
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_WWWAUTHENTICATE))
                        {
                            //sipHeader.RawAuthentication = headerValue;
                            sipHeader.AuthenticationHeaders.Add(SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, headerValue));
                        }
                        #endregion
                        #region Authorization
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_AUTHORIZATION))
                        {
                            //sipHeader.RawAuthentication = headerValue;
                            sipHeader.AuthenticationHeaders.Add(SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.Authorize, headerValue));
                        }
                        #endregion
                        #region ProxyAuthentication
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_PROXYAUTHENTICATION))
                        {
                            //sipHeader.RawAuthentication = headerValue;
                            sipHeader.AuthenticationHeaders.Add(SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.ProxyAuthenticate, headerValue));
                        }
                        #endregion
                        #region ProxyAuthorization
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_PROXYAUTHORIZATION))
                        {
                            sipHeader.AuthenticationHeaders.Add(SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.ProxyAuthorization, headerValue));
                        }
                        #endregion
                        #region UserAgent
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_USERAGENT))
                        {
                            sipHeader.UserAgent = headerValue;
                        }
                        #endregion
                        #region MaxForwards
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_MAXFORWARDS))
                        {
                            if (!Int32.TryParse(headerValue, out sipHeader.MaxForwards))
                            {
                                logger.LogWarning("The " + SIPHeaders.SIP_HEADER_MAXFORWARDS + " could not be parsed as a valid integer, {HeaderLine}", headerLine);
                            }
                        }
                        #endregion
                        #region ContentLength
                        else if (IsHeaderName(SIPHeaders.SIP_COMPACTHEADER_CONTENTLENGTH) ||
                            IsHeaderName(SIPHeaders.SIP_HEADER_CONTENTLENGTH))
                        {
                            if (!Int32.TryParse(headerValue, out sipHeader.ContentLength))
                            {
                                logger.LogWarning("The " + SIPHeaders.SIP_HEADER_CONTENTLENGTH + " could not be parsed as a valid integer.");
                            }
                        }
                        #endregion
                        #region ContentType
                        else if (IsHeaderName(SIPHeaders.SIP_COMPACTHEADER_CONTENTTYPE) ||
                            IsHeaderName(SIPHeaders.SIP_HEADER_CONTENTTYPE))
                        {
                            sipHeader.ContentType = headerValue;
                        }
                        #endregion
                        #region Accept
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_ACCEPT))
                        {
                            sipHeader.Accept = headerValue;
                        }
                        #endregion
                        #region Route
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_ROUTE))
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
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_RECORDROUTE))
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
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_ALLOW_EVENTS) || IsHeaderName(SIPHeaders.SIP_COMPACTHEADER_ALLOWEVENTS))
                        {
                            sipHeader.AllowEvents = headerValue;
                        }
                        #endregion
                        #region Event
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_EVENT) || IsHeaderName(SIPHeaders.SIP_COMPACTHEADER_EVENT))
                        {
                            sipHeader.Event = headerValue;
                        }
                        #endregion
                        #region SubscriptionState.
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_SUBSCRIPTION_STATE))
                        {
                            sipHeader.SubscriptionState = headerValue;
                        }
                        #endregion
                        #region Timestamp.
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_TIMESTAMP))
                        {
                            sipHeader.Timestamp = headerValue;
                        }
                        #endregion
                        #region Date.
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_DATE))
                        {
                            sipHeader.Date = headerValue;
                        }
                        #endregion
                        #region Refer-Sub.
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_REFERSUB))
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
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_REFERTO) ||
                            IsHeaderName(SIPHeaders.SIP_COMPACTHEADER_REFERTO))
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
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_REFERREDBY))
                        {
                            sipHeader.ReferredBy = headerValue;
                        }
                        #endregion
                        #region Replaces.
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_REPLACES))
                        {
                            sipHeader.Replaces = headerValue;
                        }
                        #endregion
                        #region Require.
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_REQUIRE))
                        {
                            sipHeader.Require = headerValue;

                            if (!String.IsNullOrEmpty(sipHeader.Require))
                            {
                                sipHeader.RequiredExtensions = SIPExtensionHeaders.ParseSIPExtensions(sipHeader.Require, out sipHeader.UnknownRequireExtension);
                            }
                        }
                        #endregion
                        #region Reason.
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_REASON))
                        {
                            sipHeader.Reason = headerValue;
                        }
                        #endregion
                        #region Proxy-ReceivedFrom.
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_PROXY_RECEIVEDFROM))
                        {
                            sipHeader.ProxyReceivedFrom = headerValue;
                        }
                        #endregion
                        #region Proxy-ReceivedOn.
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_PROXY_RECEIVEDON))
                        {
                            sipHeader.ProxyReceivedOn = headerValue;
                        }
                        #endregion
                        #region Proxy-SendFrom.
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_PROXY_SENDFROM))
                        {
                            sipHeader.ProxySendFrom = headerValue;
                        }
                        #endregion
                        #region Supported
                        else if (IsHeaderName(SIPHeaders.SIP_COMPACTHEADER_SUPPORTED) ||
                            IsHeaderName(SIPHeaders.SIP_HEADER_SUPPORTED))
                        {
                            sipHeader.Supported = headerValue;

                            if (!String.IsNullOrEmpty(sipHeader.Supported))
                            {
                                sipHeader.SupportedExtensions = SIPExtensionHeaders.ParseSIPExtensions(sipHeader.Supported, out _);
                            }
                        }
                        #endregion
                        #region Authentication-Info
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_AUTHENTICATIONINFO))
                        {
                            sipHeader.AuthenticationInfo = headerValue;
                        }
                        #endregion
                        #region Accept-Encoding
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_ACCEPTENCODING))
                        {
                            sipHeader.AcceptEncoding = headerValue;
                        }
                        #endregion
                        #region Accept-Language
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_ACCEPTLANGUAGE))
                        {
                            sipHeader.AcceptLanguage = headerValue;
                        }
                        #endregion
                        #region Alert-Info
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_ALERTINFO))
                        {
                            sipHeader.AlertInfo = headerValue;
                        }
                        #endregion
                        #region Allow
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_ALLOW))
                        {
                            sipHeader.Allow = headerValue;
                        }
                        #endregion
                        #region Call-Info
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_CALLINFO))
                        {
                            sipHeader.CallInfo = headerValue;
                        }
                        #endregion
                        #region Content-Disposition
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_CONTENT_DISPOSITION))
                        {
                            sipHeader.ContentDisposition = headerValue;
                        }
                        #endregion
                        #region Content-Encoding
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_CONTENT_ENCODING))
                        {
                            sipHeader.ContentEncoding = headerValue;
                        }
                        #endregion
                        #region Content-Language
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_CONTENT_LANGUAGE))
                        {
                            sipHeader.ContentLanguage = headerValue;
                        }
                        #endregion
                        #region Error-Info
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_ERROR_INFO))
                        {
                            sipHeader.ErrorInfo = headerValue;
                        }
                        #endregion
                        #region In-Reply-To
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_IN_REPLY_TO))
                        {
                            sipHeader.InReplyTo = headerValue;
                        }
                        #endregion
                        #region MIME-Version
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_MIME_VERSION))
                        {
                            sipHeader.MIMEVersion = headerValue;
                        }
                        #endregion
                        #region Organization
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_ORGANIZATION))
                        {
                            sipHeader.Organization = headerValue;
                        }
                        #endregion
                        #region Priority
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_PRIORITY))
                        {
                            sipHeader.Priority = headerValue;
                        }
                        #endregion
                        #region Proxy-Require
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_PROXY_REQUIRE))
                        {
                            sipHeader.ProxyRequire = headerValue;
                        }
                        #endregion
                        #region Reply-To
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_REPLY_TO))
                        {
                            sipHeader.ReplyTo = headerValue;
                        }
                        #endregion
                        #region Retry-After
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_RETRY_AFTER))
                        {
                            sipHeader.RetryAfter = headerValue;
                        }
                        #endregion
                        #region Subject
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_SUBJECT))
                        {
                            sipHeader.Subject = headerValue;
                        }
                        #endregion
                        #region Unsupported
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_UNSUPPORTED))
                        {
                            sipHeader.Unsupported = headerValue;
                        }
                        #endregion
                        #region Warning
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_WARNING))
                        {
                            sipHeader.Warning = headerValue;
                        }
                        #endregion
                        #region ETag
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_ETAG))
                        {
                            sipHeader.ETag = headerValue;
                        }
                        #endregion
                        #region RAck
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_RELIABLE_ACK))
                        {
                            var rackFields = headerValue.AsSpan();
                            if (!TryGetSpaceSeparatedToken(rackFields, 0, out var rackRSeq))
                            {
                                logger.LogWarning("The " + SIPHeaders.SIP_HEADER_RELIABLE_ACK + " was empty.");
                            }
                            else
                            {
                                if (!int.TryParse(rackRSeq, out sipHeader.RAckRSeq))
                                {
                                    logger.LogWarning(SIPHeaders.SIP_HEADER_RELIABLE_ACK + " did not contain a valid integer for the RSeq being acknowledged, {HeaderLine}", headerLine);
                                }

                                if (TryGetSpaceSeparatedToken(rackFields, 1, out var rackCSeq))
                                {
                                    if (!int.TryParse(rackCSeq, out sipHeader.RAckCSeq))
                                    {
                                        logger.LogWarning(SIPHeaders.SIP_HEADER_RELIABLE_ACK + " did not contain a valid integer for the CSeq being acknowledged, {HeaderLine}", headerLine);
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("There was no " + SIPHeaders.SIP_HEADER_RELIABLE_ACK + " method, {HeaderLine}", headerLine);
                                }

                                if (TryGetSpaceSeparatedToken(rackFields, 2, out var rackCSeqMethod))
                                {
                                    sipHeader.RAckCSeqMethod = SIPMethods.GetMethod(rackCSeqMethod.ToString());
                                }
                                else
                                {
                                    logger.LogWarning("There was no " + SIPHeaders.SIP_HEADER_RELIABLE_ACK + " method, {HeaderLine}", headerLine);
                                }
                            }
                        }
                        #endregion
                        #region RSeq
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_RELIABLE_SEQ))
                        {
                            if (!Int32.TryParse(headerValue, out sipHeader.RSeq))
                            {
                                logger.LogWarning("The Rseq value was not a valid integer. {HeaderLine}.", headerLine);
                            }
                        }
                        #endregion
                        #region Server
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_SERVER))
                        {
                            sipHeader.Server = headerValue;
                        }
                        #endregion
                        #region P-Asserted-Indentity
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_PASSERTED_IDENTITY))
                        {
                            sipHeader.PassertedIdentity.AddRange(SIPUriHeader.ParseHeader(headerValue));
                        }
                        #endregion
                        #region History-Info
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_HISTORY_INFO))
                        {
                            sipHeader.HistoryInfo.AddRange(SIPUriHeader.ParseHeader(headerValue));
                        }
                        #endregion
                        #region Diversion
                        else if (IsHeaderName(SIPHeaders.SIP_HEADER_DIVERSION))
                        {
                            sipHeader.Diversion.AddRange(SIPUriHeader.ParseHeader(headerValue));
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
                        logger.LogError(parseExcp, "Error parsing SIP header {HeaderLine}. {ErrorMessage}.", headerLine, parseExcp.Message);
                        throw new SIPValidationException(SIPValidationFieldsEnum.Headers, "Unknown error parsing Header.");
                    }
                }

                // ensure History-Info Header is sorted in ascending order, if it already is nothing will be changed
                if (sipHeader.HistoryInfo != null && sipHeader.HistoryInfo.Count > 1)
                {
                    if (!SIPUriHeader.SortByUriParameter(ref sipHeader.HistoryInfo, "index"))
                    {
                        logger.LogWarning("could not sort History-Info header");
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
                logger.LogError(excp, "Exception ParseSIPHeaders. {ErrorMessage}", excp.Message);
                throw new SIPValidationException(SIPValidationFieldsEnum.Headers, "Unknown error parsing Headers.");
            }
        }

        /// <summary>
        /// Puts the SIP headers together into a string ready for transmission.
        /// </summary>
        /// <returns>String representing the SIP headers.</returns>
        public override string ToString()
        {
            try
            {
                var headersBuilder = new StringBuilder();

                void AppendHeader<T>(string headerName, T headerValue) =>
                    headersBuilder.Append($"{headerName}: {headerValue}{m_CRLF}");

                headersBuilder.Append(Vias.ToString());

                if (To != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_TO, this.To);
                }

                if (From != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_FROM, this.From);
                }

                if (CallId != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_CALLID, this.CallId);
                }

                if (CSeq >= 0)
                {
                    AppendHeader(
                        SIPHeaders.SIP_HEADER_CSEQ,
                        this.CSeqMethod != SIPMethodsEnum.NONE ? $"{this.CSeq} {this.CSeqMethod}" : this.CSeq.ToString());
                }

                #region Appending Contact header.

                if (Contact != null && Contact.Count == 1)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_CONTACT, Contact[0]);
                }
                else if (Contact != null && Contact.Count > 1)
                {
                    StringBuilder contactsBuilder = new StringBuilder();
                    contactsBuilder.Append($"{SIPHeaders.SIP_HEADER_CONTACT}: ");

                    bool firstContact = true;
                    foreach (SIPContactHeader contactHeader in Contact)
                    {
                        if (firstContact)
                        {
                            contactsBuilder.Append(contactHeader.ToString());
                        }
                        else
                        {
                            contactsBuilder.Append($",{contactHeader}");
                        }

                        firstContact = false;
                    }

                    headersBuilder.Append(contactsBuilder).Append(m_CRLF);
                }

                #endregion

                if (MaxForwards >= 0)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_MAXFORWARDS, this.MaxForwards);
                }

                if (Routes != null && Routes.Length > 0)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_ROUTE, Routes);
                }

                if (RecordRoutes != null && RecordRoutes.Length > 0)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_RECORDROUTE, RecordRoutes);
                }

                if (!string.IsNullOrWhiteSpace(UserAgent))
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_USERAGENT, this.UserAgent);
                }

                if (Expires != -1)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_EXPIRES, this.Expires);
                }

                if (MinExpires != -1)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_MINEXPIRES, this.MinExpires);
                }

                if (Accept != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_ACCEPT, this.Accept);
                }

                if (AcceptEncoding != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_ACCEPTENCODING, this.AcceptEncoding);
                }

                if (AcceptLanguage != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_ACCEPTLANGUAGE, this.AcceptLanguage);
                }

                if (Allow != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_ALLOW, this.Allow);
                }

                if (AlertInfo != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_ALERTINFO, this.AlertInfo);
                }

                if (AuthenticationInfo != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_AUTHENTICATIONINFO, this.AuthenticationInfo);
                }

                if (AuthenticationHeaders.Count > 0)
                {
                    foreach (var authHeader in AuthenticationHeaders)
                    {
                        var value = authHeader.ToString();
                        if (value != null)
                        {
                            headersBuilder.Append(value).Append(m_CRLF);
                        }
                    }
                }
                if (CallInfo != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_CALLINFO, this.CallInfo);
                }

                if (ContentDisposition != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_CONTENT_DISPOSITION, this.ContentDisposition);
                }

                if (ContentEncoding != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_CONTENT_ENCODING, this.ContentEncoding);
                }

                if (ContentLanguage != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_CONTENT_LANGUAGE, this.ContentLanguage);
                }

                if (Date != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_DATE, Date);
                }

                if (ErrorInfo != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_ERROR_INFO, this.ErrorInfo);
                }

                if (InReplyTo != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_IN_REPLY_TO, this.InReplyTo);
                }

                if (Organization != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_ORGANIZATION, this.Organization);
                }

                if (Priority != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_PRIORITY, Priority);
                }

                if (ProxyRequire != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_PROXY_REQUIRE, this.ProxyRequire);
                }

                if (ReplyTo != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_REPLY_TO, this.ReplyTo);
                }

                if (Require != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_REQUIRE, Require);
                }

                if (RetryAfter != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_RETRY_AFTER, this.RetryAfter);
                }

                if (!string.IsNullOrWhiteSpace(Server))
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_SERVER, this.Server);
                }

                if (Subject != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_SUBJECT, Subject);
                }

                if (Supported != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_SUPPORTED, Supported);
                }

                if (Timestamp != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_TIMESTAMP, Timestamp);
                }

                if (Unsupported != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_UNSUPPORTED, Unsupported);
                }

                if (Warning != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_WARNING, Warning);
                }

                if (ETag != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_ETAG, ETag);
                }

                AppendHeader(SIPHeaders.SIP_HEADER_CONTENTLENGTH, this.ContentLength);
                if (!string.IsNullOrWhiteSpace(this.ContentType))
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_CONTENTTYPE, this.ContentType);
                }

                // Non-core SIP headers.
                if (AllowEvents != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_ALLOW_EVENTS, AllowEvents);
                }

                if (Event != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_EVENT, Event);
                }

                if (SubscriptionState != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_SUBSCRIPTION_STATE, SubscriptionState);
                }

                if (ReferSub != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_REFERSUB, ReferSub);
                }

                if (ReferTo != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_REFERTO, ReferTo);
                }

                if (ReferredBy != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_REFERREDBY, ReferredBy);
                }

                if (Replaces != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_REPLACES, Replaces);
                }

                if (Reason != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_REASON, Reason);
                }

                if (RSeq != -1)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_RELIABLE_SEQ, RSeq);
                }

                if (RAckRSeq != -1)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_RELIABLE_ACK, $"{RAckRSeq} {RAckCSeq} {RAckCSeqMethod}");
                }

                foreach (var PAI in PassertedIdentity)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_PASSERTED_IDENTITY, PAI);
                }

                foreach (var HistInfo in HistoryInfo)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_HISTORY_INFO, HistInfo);
                }

                foreach (var DiversionHeader in Diversion)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_DIVERSION, DiversionHeader);
                }

                // Custom SIP headers.
                if (ProxyReceivedFrom != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_PROXY_RECEIVEDFROM, ProxyReceivedFrom);
                }

                if (ProxyReceivedOn != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_PROXY_RECEIVEDON, ProxyReceivedOn);
                }

                if (ProxySendFrom != null)
                {
                    AppendHeader(SIPHeaders.SIP_HEADER_PROXY_SENDFROM, ProxySendFrom);
                }

                // Unknown SIP headers
                foreach (string unknownHeader in UnknownHeaders)
                {
                    headersBuilder.Append(unknownHeader).Append(m_CRLF);
                }

                return headersBuilder.ToString();
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception SIPHeader ToString. Exception: {ErrorMessage}", excp.Message);
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
            Date = $"{DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss ")}GMT";
        }

        public void SetDateHeader(bool useLocalTime, string timeFormat)
        {
            var time = useLocalTime ? DateTime.Now : DateTime.UtcNow;
            Date = time.ToString(timeFormat) + (useLocalTime ? "" : "GMT");
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
                        logger.LogWarning("Invalid SIP header, ignoring {UnknownHeader}.", unknonwHeader);
                        continue;
                    }

                    var trimmedHeaderSpan = trimmedHeader.AsSpan();
                    var headerName = trimmedHeaderSpan.Slice(0, delimiterIndex).Trim().ToString();

                    if (string.Equals(headerName, unknownHeaderName, StringComparison.OrdinalIgnoreCase))
                    {
                        return trimmedHeaderSpan.Slice(delimiterIndex + 1).Trim().ToString();
                    }
                }

                return null;
            }
        }
    }
}
