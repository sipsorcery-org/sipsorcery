//-----------------------------------------------------------------------------
// Filename: SIPParameters.cs
//
// Description: SIP parameters as used in Contact, To, From and Via SIP headers.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 06 May 2006	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// Represents a series of name value pairs that are optionally included in SIP URIs and also as an additional
    /// optional setting on some SIP Headers (Contact, To, From, Via).
    /// This class also treats the header value of a SIP URI as a special case of a SIP parameter. The difference between
    /// a parameter and a SIP URI header is the start and delimiter characters used.
    /// </summary>
    /// <code>
    /// <![CDATA[
    /// SIP URI with parameters:
    /// sip:1234@sip.com;key1=value1;key2=value2
    /// 
    /// SIP URI with headers:
    /// sip:1234@sip.com?key1=value1&key2=value2
    /// 
    /// SIP URI with parameters and headers (parameters always come first):
    /// sip:1234@sip.com;key1=value1;key2=value2?key1=value1&key2=value2
    /// ]]>
    /// </code>
    [DataContract]
    public class SIPParameters
    {
        private const char TAG_NAME_VALUE_SEPERATOR = '=';
        private const char QUOTE = '"';
        private const char BACK_SLASH = '\\';
        private const char DEFAULT_PARAMETER_DELIMITER = ';';

        private static ILogger logger = Log.Logger;

        [DataMember]
        public char TagDelimiter = DEFAULT_PARAMETER_DELIMITER;

        [DataMember]
        public Dictionary<string, string> m_dictionary;

        [IgnoreDataMember]
        public int Count
        {
            get { return (m_dictionary != null) ? m_dictionary.Count : 0; }
        }

        internal SIPParameters()
        {
            m_dictionary = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
        }

        /// <summary>
        /// Parses the name value pairs from a SIP parameter or header string.
        /// </summary>
        public SIPParameters(string sipString, char delimiter) :
            this()
        {
            if (!String.IsNullOrEmpty(sipString))
            {
                Initialise(sipString, delimiter);
            }
        }

        private void Initialise(string sipString, char delimiter)
        {
            TagDelimiter = delimiter;

            string[] keyValuePairs = GetKeyValuePairsFromQuoted(sipString, delimiter);

            if (keyValuePairs != null && keyValuePairs.Length > 0)
            {
                foreach (string keyValuePair in keyValuePairs)
                {
                    AddKeyValuePair(keyValuePair, m_dictionary);
                }
            }
        }

        public static string[] GetKeyValuePairsFromQuoted(string quotedString, char delimiter)
        {
            try
            {
                List<string> keyValuePairList = new List<string>();

                if (quotedString == null || quotedString.Trim().Length == 0)
                {
                    return null;
                }
                else if (quotedString.IndexOf(delimiter) == -1)
                {
                    return new string[] { quotedString };
                }
                else
                {
                    int startParameterPosn = 0;
                    int inParameterPosn = 0;
                    bool inQuotedStr = false;

                    while (inParameterPosn != -1 && inParameterPosn < quotedString.Length)
                    {
                        inParameterPosn = quotedString.IndexOfAny(new char[] { delimiter, QUOTE }, inParameterPosn);

                        // Determine if the delimiter position represents the end of the parameter or is in a quoted string.
                        if (inParameterPosn != -1)
                        {
                            if (inParameterPosn <= startParameterPosn && quotedString[inParameterPosn] == delimiter)
                            {
                                // Initial or doubled up Parameter delimiter character, ignore and move on.
                                inQuotedStr = false;
                                inParameterPosn++;
                                startParameterPosn = inParameterPosn;
                            }
                            else if (quotedString[inParameterPosn] == QUOTE)
                            {
                                if (inQuotedStr && inParameterPosn > 0 && quotedString[inParameterPosn - 1] != BACK_SLASH)
                                {
                                    // If in a quoted string and this quote has not been escaped close the quoted string.
                                    inQuotedStr = false;
                                }
                                else if (inQuotedStr && inParameterPosn > 0 && quotedString[inParameterPosn - 1] == BACK_SLASH)
                                {
                                    // Do nothing, quote has been escaped in a quoted string.
                                }
                                else if (!inQuotedStr)
                                {
                                    // Start quoted string.
                                    inQuotedStr = true;
                                }

                                inParameterPosn++;
                            }
                            else
                            {
                                if (!inQuotedStr)
                                {
                                    // Parameter delimiter found and not in quoted string therefore this is a parameter separator.
                                    string keyValuePair = quotedString.Substring(startParameterPosn, inParameterPosn - startParameterPosn);

                                    keyValuePairList.Add(keyValuePair);

                                    inParameterPosn++;
                                    startParameterPosn = inParameterPosn;
                                }
                                else
                                {
                                    // Do nothing, separator character is within a quoted string.
                                    inParameterPosn++;
                                }
                            }
                        }
                    }

                    // Add the last parameter.
                    if (startParameterPosn < quotedString.Length)
                    {
                        // Parameter delimiter found and not in quoted string therefore this is a parameter separator.
                        keyValuePairList.Add(quotedString.Substring(startParameterPosn));
                    }
                }

                return keyValuePairList.ToArray();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception GetKeyValuePairsFromQuoted. " + excp.Message);
                throw;
            }
        }

        private void AddKeyValuePair(string keyValuePair, Dictionary<string, string> dictionary)
        {
            if (keyValuePair != null && keyValuePair.Trim().Length > 0)
            {
                int seperatorPosn = keyValuePair.IndexOf(TAG_NAME_VALUE_SEPERATOR);
                if (seperatorPosn != -1)
                {
                    string keyName = keyValuePair.Substring(0, seperatorPosn).Trim();

                    // If this is not the parameter that is being removed put it back on.
                    if (!dictionary.ContainsKey(keyName))
                    {
                        dictionary.Add(keyName, keyValuePair.Substring(seperatorPosn + 1).Trim());
                    }
                }
                else
                {
                    // Keys with no values are valid in SIP so they get added to the collection with a null value.
                    if (!dictionary.ContainsKey(keyValuePair))
                    {
                        dictionary.Add(keyValuePair, null);
                    }
                }
            }
        }

        public void Set(string name, string value)
        {
            if (m_dictionary.ContainsKey(name))
            {
                m_dictionary[name] = value;
            }
            else
            {
                m_dictionary.Add(name, value);
            }
        }

        public string Get(string name)
        {
            if (m_dictionary != null || m_dictionary.Count == 0)
            {
                if (m_dictionary.ContainsKey(name))
                {
                    return SIPEscape.SIPURIParameterUnescape(m_dictionary[name]);
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        public bool Has(string name)
        {
            if (m_dictionary != null)
            {
                return m_dictionary.ContainsKey(name);
            }
            else
            {
                return false;
            }
        }

        public void Remove(string name)
        {
            if (name != null)
            {
                m_dictionary.Remove(name);
            }
        }

        public void RemoveAll()
        {
            m_dictionary = new Dictionary<string, string>();
        }

        public string[] GetKeys()
        {
            if (m_dictionary == null || m_dictionary.Count == 0)
            {
                return null;
            }
            else
            {
                string[] keys = new string[m_dictionary.Count];
                int index = 0;
                foreach (KeyValuePair<string, string> entry in m_dictionary)
                {
                    keys[index++] = entry.Key as string;
                }

                return keys;
            }
        }

        public override string ToString()
        {
            string paramStr = null;

            if (m_dictionary != null)
            {
                foreach (KeyValuePair<string, string> param in m_dictionary)
                {
                    if (param.Value != null && param.Value.Trim().Length > 0)
                    {
                        paramStr += TagDelimiter + param.Key + TAG_NAME_VALUE_SEPERATOR + SIPEscape.SIPURIParameterEscape(param.Value);
                    }
                    else
                    {
                        paramStr += TagDelimiter + param.Key;
                    }
                }
            }

            return paramStr;
        }

        public override int GetHashCode()
        {
            return (m_dictionary == null) ? 0 : m_dictionary.GetHashCode();
        }

        public SIPParameters CopyOf()
        {
            SIPParameters copy = new SIPParameters();
            copy.TagDelimiter = this.TagDelimiter;
            copy.m_dictionary = (this.m_dictionary != null) ? new Dictionary<string, string>(this.m_dictionary) : new Dictionary<string, string>();
            return copy;
        }

        public static bool AreEqual(SIPParameters params1, SIPParameters params2)
        {
            return params1 == params2;
        }

        public override bool Equals(object obj)
        {
            return AreEqual(this, (SIPParameters)obj);
        }

        /// <summary>
        /// Two SIPParameters objects are considered equal if they have the same keys and values. The
        /// order of the keys does not affect the equality comparison.
        /// </summary>
        public static bool operator ==(SIPParameters x, SIPParameters y)
        {
            if (x is null && y is null)
            {
                return true;
            }
            else if (x is null || y is null)
            {
                return false;
            }
            else if (x.m_dictionary == null && y.m_dictionary == null)
            {
                return true;
            }
            else if (x.m_dictionary == null || y.m_dictionary == null)
            {
                return false;
            }

            return x.m_dictionary.Count == y.m_dictionary.Count &&
               x.m_dictionary.Keys.All(k => y.m_dictionary.ContainsKey(k)
               && String.Equals(x.m_dictionary[k], y.m_dictionary[k], StringComparison.InvariantCultureIgnoreCase));

            //return x.m_dictionary.Count == y.m_dictionary.Count && !x.m_dictionary.Except(y.m_dictionary).Any();
        }

        public static bool operator !=(SIPParameters x, SIPParameters y)
        {
            return !(x == y);
        }
    }
}
