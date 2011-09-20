// ============================================================================
// FileName: HighriseRequest.cs
//
// Description:
// Base class to send requests to 37 Signals contact management system Highrise.
//
// Author(s):
// Aaron Clauson
//
// History:
// 13 Feb 2011  Aaron Clauson   Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2011 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Ltd. 
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using System.Web;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.CRM.ThirtySevenSignals
{
    /// <summary>
    /// This class submits HTTP requests to the 37 Signals Highrise API.
    /// </summary>
    /// <typeparam name="S">The singular object type for retrieving single items, e.g. Person or Company.</typeparam>
    /// <typeparam name="T">The plural object type for retrieving lists of items, e.g. People or Companies.</typeparam>
    public class HighriseRequest<S, T>
    {
        private const int MAX_HTTP_REQUEST_TIMEOUT = 10;
        private const string NO_RESULTS_RESPONSE = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<nil-classes type=\"array\"/>\n";   // There's got to be a better way. The XML deserialiser throws an exception when it gets this.

        private static ILog logger = AppState.logger;

        private string m_urlNoun;
        private string m_authToken;

        protected string BaseUrl { get; private set; }

        public HighriseRequest(string baseUrl, string urlNoun, string authToken)
        {
            BaseUrl = baseUrl;
            m_urlNoun = urlNoun;
            m_authToken = authToken;
        }

        public S GetByID(int id)
        {
            string requestURL = BaseUrl + "/" + m_urlNoun + "/" + id + ".xml";
            return GetItem(requestURL);
        }

        public T GetByPhoneNumber(string phoneNumber)
        {
            string requestURL = BaseUrl + "/" + m_urlNoun + "/search.xml?" + Uri.EscapeDataString("criteria[phone]=" + phoneNumber);
            return GetList(requestURL);
        }

        public T GetByCustomField(string customField, string searchString)
        {
            string requestURL = BaseUrl + "/" + m_urlNoun + "/search.xml?" + Uri.EscapeDataString("criteria[" + customField + "]=" + searchString);
            return GetList(requestURL);
        }

        public T GetByCustomSearch(string customSearch)
        {
            string requestURL = BaseUrl + "/" + m_urlNoun + "/search.xml?" + Uri.EscapeDataString(customSearch);
            return GetList(requestURL);
        }

        public T GetByName(string name)
        {
            string requestURL = BaseUrl + "/" + m_urlNoun + "/search.xml?" + Uri.EscapeDataString("term=" + name);
            return GetList(requestURL);
        }

        protected virtual S GetItem(string url)
        {
            string response = GetResponse(url);

            if (!response.IsNullOrBlank())
            {
                S item = default(S);

                using (TextReader xmlReader = new StringReader(response))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(S));
                    item = (S)serializer.Deserialize(xmlReader);
                }

                return item;
            }
            else
            {
                return default(S);
            }
        }

        protected virtual T GetList(string url)
        {
            logger.Debug("HighRiseRequest url " + url + ".");

            string response = GetResponse(url);

            if (response == NO_RESULTS_RESPONSE)
            {
                return default(T);
            }
            else if (!response.IsNullOrBlank())
            {
                T list = default(T);

                using (TextReader xmlReader = new StringReader(response))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(T));
                    list = (T)serializer.Deserialize(xmlReader);
                }

                return list;
            }
            else
            {
                return default(T);
            }
        }

        private string GetResponse(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.AllowAutoRedirect = true;
            request.Timeout = MAX_HTTP_REQUEST_TIMEOUT * 1000;
            request.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(m_authToken)));

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            if (response.StatusCode != HttpStatusCode.OK)
            {
                response.Close();
                throw new ApplicationException("37 Signals Highrise request to " + url + " failed with response " + response.StatusCode + ".");
            }

            StreamReader reader = new StreamReader(response.GetResponseStream());
            string responseStr = reader.ReadToEnd();
            response.Close();

            if (responseStr != null)
            {
                return Regex.Replace(responseStr, "nil=\"true\"", "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\"");
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Attempts to create a new Highrise item.
        /// </summary>
        /// <param name="createUrl">The URL to send the creation request to. The URL can determine where the object gets created.
        /// For example to create a new note on a person the URL would be /people/#{person-id}/notes.xml.</param>
        /// <param name="itemXML">The XML that contains the information to create the new Highrise item.</param>
        /// <returns>If sucessful the reponse HTTP Location header which indicates the URL for the newly created item.</returns>
        protected string CreateItem(string createUrl, string itemXML)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(createUrl);
            request.Method = "POST";
            request.AllowAutoRedirect = true;
            request.Timeout = MAX_HTTP_REQUEST_TIMEOUT * 1000;
            request.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(m_authToken)));
            request.ContentType = "application/xml";
            Stream requestStream = request.GetRequestStream();
            byte[] requestBytes = Encoding.UTF8.GetBytes(itemXML);
            requestStream.Write(requestBytes, 0, requestBytes.Length);

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            if (response.StatusCode != HttpStatusCode.Created)
            {
                response.Close();
                throw new ApplicationException("37 Signals Highrise create request to " + createUrl + " failed with response " + response.StatusCode + ".");
            }

            return response.Headers["Location"];
        }
    }
}
