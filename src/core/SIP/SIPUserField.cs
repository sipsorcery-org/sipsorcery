//-----------------------------------------------------------------------------
// Filename: SIPUserField.cs
//
// Description: 
// Encapsulates the format for the SIP Contact, From and To headers.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 21 Apr 2006	Aaron Clauson	Created, Hobart, Australia.
// 04 Sep 2008  Aaron Clauson   Changed display name to always use quotes. Some SIP stacks were
//                              found to have problems with a comma in a non-quoted display name.
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
    /// <summary>
    /// Encapsulates the format for the SIP Contact, From and To headers.
    /// </summary>
    /// <remarks>
    /// If no "&lt;" and "&gt;" are present, all parameters after the URI are header
    /// parameters, not URI parameters.
    /// </remarks>
    /// <code>
    /// <![CDATA[
    /// name-addr      =  [ display-name ] LAQUOT addr-spec RAQUOT
    /// addr-spec      =  SIP-URI / SIPS-URI / absoluteURI
    /// SIP-URI          =  "sip:" [ userinfo ] hostport
    /// uri-parameters [ headers ]
    /// SIPS-URI         =  "sips:" [ userinfo ] hostport
    /// uri-parameters [ headers ]
    /// userinfo         =  ( user / telephone-subscriber ) [ ":" password ] "@"
    /// ]]>
    /// </code>
    [DataContract]
    public class SIPUserField
    {
        private const char PARAM_TAG_DELIMITER = ';';

        private static ILogger logger = Log.Logger;

        [DataMember]
        public string Name;

        [DataMember]
        public SIPURI URI;

        [DataMember]
        public SIPParameters Parameters = new SIPParameters(null, PARAM_TAG_DELIMITER);

        public SIPUserField()
        { }

        public SIPUserField(string name, SIPURI uri, string paramsAndHeaders)
        {
            Name = name;
            URI = uri;

            Parameters = new SIPParameters(paramsAndHeaders, PARAM_TAG_DELIMITER);
        }

        public static SIPUserField ParseSIPUserField(string userFieldStr)
        {
            if (string.IsNullOrWhiteSpace(userFieldStr))
            {
                throw new ArgumentException("A SIPUserField cannot be parsed from an empty string.");
            }

            SIPUserField userField = new SIPUserField();
            string trimUserField = userFieldStr.Trim();

            int position = trimUserField.IndexOf('<');

            if (position == -1)
            {
                // Treat the field as a URI only, except that all parameters are Header parameters and not URI parameters 
                // (RFC3261 section 20.39 which refers to 20.10 for parsing rules).
                string uriStr = trimUserField;
                int paramDelimPosn = trimUserField.IndexOf(PARAM_TAG_DELIMITER);

                if (paramDelimPosn != -1)
                {
                    string paramStr = trimUserField.Substring(paramDelimPosn + 1).Trim();
                    userField.Parameters = new SIPParameters(paramStr, PARAM_TAG_DELIMITER);
                    uriStr = trimUserField.Substring(0, paramDelimPosn);
                }

                userField.URI = SIPURI.ParseSIPURI(uriStr);
            }
            else
            {
                if (position > 0)
                {
                    userField.Name = trimUserField.Substring(0, position).Trim().Trim('"');
                    trimUserField = trimUserField.Substring(position, trimUserField.Length - position);
                }

                int addrSpecLen = trimUserField.Length;
                position = trimUserField.IndexOf('>');
                if (position != -1)
                {
                    addrSpecLen = position - 1;

                    string paramStr = trimUserField.Substring(position + 1).Trim();
                    userField.Parameters = new SIPParameters(paramStr, PARAM_TAG_DELIMITER);

                    string addrSpec = trimUserField.Substring(1, addrSpecLen);

                    userField.URI = SIPURI.ParseSIPURI(addrSpec);
                }
                else
                {
                    throw new SIPValidationException(SIPValidationFieldsEnum.ContactHeader, "A SIPUserField was missing the right quote, " + userFieldStr + ".");
                }
            }

            return userField;
        }

        public override string ToString()
        {
            try
            {
                string userFieldStr = null;

                if (Name != null)
                {
                    /*if(Regex.Match(Name, @"\s").Success)
                    {
                        userFieldStr = "\"" + Name + "\" ";
                    }
                    else
                    {
                        userFieldStr = Name + " ";
                    }*/

                    userFieldStr = "\"" + Name + "\" ";
                }

                userFieldStr += "<" + URI.ToString() + ">" + Parameters.ToString();

                return userFieldStr;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPUserField ToString. " + excp.Message);
                throw;
            }
        }

        public string ToParameterlessString()
        {
            try
            {
                string userFieldStr = null;

                if (Name != null)
                {
                    userFieldStr = "\"" + Name + "\" ";
                }

                userFieldStr += "<" + URI.ToParameterlessString() + ">";

                return userFieldStr;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPUserField ToParameterlessString. " + excp.Message);
                throw;
            }
        }

        public SIPUserField CopyOf()
        {
            SIPUserField copy = new SIPUserField();
            copy.Name = Name;
            copy.URI = URI.CopyOf();
            copy.Parameters = Parameters.CopyOf();

            return copy;
        }
    }
}
