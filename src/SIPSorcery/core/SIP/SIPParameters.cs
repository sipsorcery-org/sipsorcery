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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.Extensions.Logging;
using Polyfills;
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

        private static readonly ILogger logger = LogFactory.CreateLogger<SIPParameters>();

        [DataMember]
        public char TagDelimiter = DEFAULT_PARAMETER_DELIMITER;

        //[DataMember]
        private readonly ConcurrentDictionary<string, string> m_dictionary;

        [IgnoreDataMember]
        public int Count
        {
            get { return (m_dictionary != null) ? m_dictionary.Count : 0; }
        }

        internal SIPParameters()
        {
            m_dictionary = new ConcurrentDictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
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

            void AddKeyValuePair(ReadOnlySpan<char> keyValuePair, Span<Range> keyValueRange)
            {
                if (keyValuePair.Trim().Length > 0)
                {
                    if (keyValuePair.Split(keyValueRange, TAG_NAME_VALUE_SEPERATOR) == 2)
                    {
                        var keyName = keyValuePair[keyValueRange[0]].Trim().ToString();

                        // If this is not the parameter that is being removed put it back on.
                        if (!m_dictionary.ContainsKey(keyName))
                        {
                            m_dictionary.TryAdd(keyName, keyValuePair[keyValueRange[1]].Trim().ToString());
                        }
                    }
                    else
                    {
                        // Keys with no values are valid in SIP so they get added to the collection with a null value.
                        var keyName = keyValuePair.ToString();
                        if (!m_dictionary.ContainsKey(keyName))
                        {
                            m_dictionary.TryAdd(keyName, null);
                        }
                    }
                }
            }

            static int IndexOfDelimiterOrQuote(ReadOnlySpan<char> value, int startIndex, char delimiter)
            {
                var remaining = value.Slice(startIndex);
                var delimiterIndex = remaining.IndexOf(delimiter);
                var quoteIndex = remaining.IndexOf(QUOTE);

                if (delimiterIndex == -1)
                {
                    return quoteIndex == -1 ? -1 : startIndex + quoteIndex;
                }

                if (quoteIndex == -1)
                {
                    return startIndex + delimiterIndex;
                }

                return startIndex + Math.Min(delimiterIndex, quoteIndex);
            }

            var sipParameters = sipString.AsSpan();
            if (sipParameters.Trim().Length == 0)
            {
                return;
            }

            Span<Range> keyValueRange = stackalloc Range[2];
            if (sipParameters.IndexOf(delimiter) == -1)
            {
                AddKeyValuePair(sipParameters, keyValueRange);
                return;
            }

            var startParameterPosn = 0;
            var inParameterPosn = 0;
            var inQuotedStr = false;

            while (inParameterPosn != -1 && inParameterPosn < sipParameters.Length)
            {
                inParameterPosn = IndexOfDelimiterOrQuote(sipParameters, inParameterPosn, delimiter);

                // Determine if the delimiter position represents the end of the parameter or is in a quoted string.
                if (inParameterPosn != -1)
                {
                    if (inParameterPosn <= startParameterPosn && sipParameters[inParameterPosn] == delimiter)
                    {
                        // Initial or doubled up Parameter delimiter character, ignore and move on.
                        inQuotedStr = false;
                        inParameterPosn++;
                        startParameterPosn = inParameterPosn;
                    }
                    else if (sipParameters[inParameterPosn] == QUOTE)
                    {
                        if (inQuotedStr && inParameterPosn > 0 && sipParameters[inParameterPosn - 1] != BACK_SLASH)
                        {
                            // If in a quoted string and this quote has not been escaped close the quoted string.
                            inQuotedStr = false;
                        }
                        else if (inQuotedStr && inParameterPosn > 0 && sipParameters[inParameterPosn - 1] == BACK_SLASH)
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
                            AddKeyValuePair(sipParameters.Slice(startParameterPosn, inParameterPosn - startParameterPosn), keyValueRange);

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
            if (startParameterPosn < sipParameters.Length)
            {
                // Parameter delimiter found and not in quoted string therefore this is a parameter separator.
                AddKeyValuePair(sipParameters.Slice(startParameterPosn), keyValueRange);
            }
        }

        public static string[] GetKeyValuePairsFromQuoted(string quotedString, char delimiter)
        {
            try
            {
                List<string> keyValuePairList = new List<string>();

                if (string.IsNullOrWhiteSpace(quotedString))
                {
                    return null;
                }
                else if (quotedString.IndexOf(delimiter) == -1)
                {
                    return new string[] { quotedString };
                }
                else
                {
                    static int IndexOfDelimiterOrQuote(ReadOnlySpan<char> value, int startIndex, char delimiter)
                    {
                        var remaining = value.Slice(startIndex);
                        var delimiterIndex = remaining.IndexOf(delimiter);
                        var quoteIndex = remaining.IndexOf(QUOTE);

                        if (delimiterIndex == -1)
                        {
                            return quoteIndex == -1 ? -1 : startIndex + quoteIndex;
                        }

                        if (quoteIndex == -1)
                        {
                            return startIndex + delimiterIndex;
                        }

                        return startIndex + Math.Min(delimiterIndex, quoteIndex);
                    }

                    int startParameterPosn = 0;
                    int inParameterPosn = 0;
                    bool inQuotedStr = false;

                    while (inParameterPosn != -1 && inParameterPosn < quotedString.Length)
                    {
                        inParameterPosn = IndexOfDelimiterOrQuote(quotedString.AsSpan(), inParameterPosn, delimiter);

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
                logger.LogError(excp, "Exception GetKeyValuePairsFromQuoted. {ErrorMessage}", excp.Message);
                throw;
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
                m_dictionary.TryAdd(name, value);
            }
        }

        public string Get(string name)
        {
            if (m_dictionary != null && m_dictionary.Count > 0)
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
                m_dictionary.TryRemove(name, out string ignore);
            }
        }

        public void RemoveAll()
        {
            m_dictionary.Clear();
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
            var paramStr = default(StringBuilder);

            if (m_dictionary != null)
            {
                foreach (KeyValuePair<string, string> param in m_dictionary)
                {
                    paramStr ??= new StringBuilder();
                    paramStr.Append(TagDelimiter).Append(param.Key);

                    if (!string.IsNullOrWhiteSpace(param.Value))
                    {
                        paramStr.Append(TAG_NAME_VALUE_SEPERATOR).Append(SIPEscape.SIPURIParameterEscape(param.Value));
                    }
                }
            }

            return paramStr?.ToString();
        }

        public override int GetHashCode()
        {
            return (m_dictionary == null) ? 0 : m_dictionary.GetHashCode();
        }

        public SIPParameters CopyOf()
        {
            SIPParameters copy = new SIPParameters();
            copy.TagDelimiter = this.TagDelimiter;
            foreach (var kvp in this.m_dictionary)
            {
                copy.m_dictionary.TryAdd(kvp.Key, kvp.Value);
            }
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
            if (object.ReferenceEquals(x, y))
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
