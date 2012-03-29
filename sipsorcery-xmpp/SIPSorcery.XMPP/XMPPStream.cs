//-----------------------------------------------------------------------------
// Filename: XMPPStream.cs
//
// Description: Represents the XML stream to read and write XML stanzas.
// 
// History:
// 14 Nov 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), Hobart, Tasmanian, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery. 
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
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.XMPP
{
    public class XMPPStream
    {
        public const string STREAM_ELEMENT_NAME = "stream";
        public const string SASL_AUTH_ELEMENT_NAME = "auth";
        public const string BIND_ELEMENT_NAME = "bind";
        public const string IQ_ELEMENT_NAME = "iq";
        public const string RESOURCE_ELEMENT_NAME = "resource";
        public const string MECHANISMS_ELEMENT_NAME = "mechanisms";
        public const string STREAM_PREFIX = "stream";
        public const string STREAM_NAMESPACE = "http://etherx.jabber.org/streams";
        public const string STREAM_PARAMS_NAMESPACE = "urn:ietf:params:xml:ns:xmpp-streams";
        public const string SASL_NAMESPACE = "urn:ietf:params:xml:ns:xmpp-sasl";
        public const string BIND_NAMESPACE = "urn:ietf:params:xml:ns:xmpp-bind";
        public const string GOOGLE_SESSION_NAMESPACE = "http://www.google.com/session";
        public const string GOOGLE_PHONE_SESSION_NAMESPACE = "http://www.google.com/session/phone";
        public const string TRANSPORT_NAMESPACE = "http://www.google.com/transport/p2p";
        public const int MAJOR_VERSION = 1;
        public const int MINOR_VERSION = 0;

#if !SILVERLIGHT
        private static XmlSchemaSet m_xmppSchemaSet = new XmlSchemaSet();
#endif

        private static ILog logger = AppState.logger;

        protected static XNamespace m_streamNS = STREAM_NAMESPACE;
        protected static XNamespace StreamParamsNS = STREAM_PARAMS_NAMESPACE;
        protected static XNamespace JabberClientNS = XMPPConstants.JABBER_NAMESPACE;

        protected Stream NetworkStream;
        protected XmlWriter XmlWriter;

        //private XmlReader m_xmlReader;
        protected bool IsTLS;
        protected bool IsAuthenticated;
        public string JID;
        private string m_toDomain;
        private string m_fromUsername;

        protected string StreamError;
        public string StreamFailure;
        protected List<XMPPStreamFeature> Features;
        protected bool Exit;
        protected string SASLToken;

        // Stream attributes from server.
        protected string StreamID;
        private decimal m_serverVersion;
        private string m_serverLanguage;
        private string m_serverNamespace;
        private string m_serverFrom;
        private string m_serverTo;

        public event Action<XElement> ElementReceived;

        static XMPPStream()
        {
            //m_xmppSchemaSet.Add(JABBER_NAMESPACE, "jabber.xsd");
        }

        public XMPPStream(Stream stream)
        {
            NetworkStream = stream;
        }

        public void Start(string toDomain, string saslToken, string fromUsername)
        {
            m_toDomain = toDomain;
            SASLToken = saslToken;
            m_fromUsername = fromUsername;

            XmlWriterSettings xws = new XmlWriterSettings();

            if (IsTLS)
            {
                xws.OmitXmlDeclaration = true;
            }

            xws.Indent = true;
            xws.NewLineHandling = NewLineHandling.None;
            xws.Encoding = new UTF8Encoding(false);
            xws.CloseOutput = false;

            XmlWriter = XmlWriter.Create(NetworkStream, xws);

            SendStreamHeader(XmlWriter);

            Listen();
        }

        protected void SendStreamHeader(XmlWriter xmlWriter)
        {
            xmlWriter.WriteStartElement(STREAM_PREFIX, STREAM_ELEMENT_NAME, STREAM_NAMESPACE);

            if (IsAuthenticated)
            {
                xmlWriter.WriteAttributeString("from", m_fromUsername);
            }

            xmlWriter.WriteAttributeString("to", m_toDomain);
            xmlWriter.WriteAttributeString("version", MAJOR_VERSION + "." + MINOR_VERSION);
            xmlWriter.WriteAttributeString("xml", "lang", null, "en");
            xmlWriter.WriteAttributeString("xmlns", XMPPConstants.JABBER_NAMESPACE);
            xmlWriter.WriteWhitespace("\n");
            xmlWriter.Flush();

            //Console.WriteLine("stream initialisation element sent (from=" + m_fromUsername + ".");
        }

        private void Listen()
        {
            //Console.WriteLine("Staring listener...");

            XmlReaderSettings xrs = new XmlReaderSettings();
            //xrs.ConformanceLevel = ConformanceLevel.Fragment;

            xrs.CloseInput = false;
            //xrs.IgnoreWhitespace = true;
            //xrs.IgnoreComments = true;
            //xrs.XmlResolver = null;

#if !SILVERLIGHT
            xrs.ValidationType = ValidationType.None;
            xrs.Schemas = m_xmppSchemaSet;
            xrs.ValidationEventHandler += new ValidationEventHandler(ValidationCallBack);
#endif
            using (XmlReader xmlReader = XmlReader.Create(NetworkStream, xrs))
            {
                //Console.WriteLine("XML reader ready.");

                while (!Exit && xmlReader.Read())
                {
                    //Console.WriteLine("Read " + xmlReader.NodeType + " " + xmlReader.Name);

                    if (xmlReader.NodeType == XmlNodeType.Element)
                    {
                        if (xmlReader.Name == STREAM_PREFIX + ":" + STREAM_ELEMENT_NAME)
                        {
                            //Console.WriteLine("stream initialisation node received.");
                            LoadStreamAttributes(xmlReader);
                        }
                        else
                        {
                            XElement streamDocElement = XElement.Load(xmlReader.ReadSubtree());

                            //Console.WriteLine(streamDocElement.Name.LocalName);
                            //Console.WriteLine(streamDocElement.ToString());

                            switch (streamDocElement.Name.LocalName)
                            {
                                case "error":
                                    StreamError = streamDocElement.Element(StreamParamsNS + "text").Value;
                                    //throw new ApplicationException(m_streamError);
                                    logger.Warn("XMPPStream error: " + StreamError);
                                    break;
                                case "features":
                                    //streamDocElement.Nodes().ToList().ForEach(x => Console.WriteLine(x.ToString()));
                                    Features = (from feature in streamDocElement.Nodes() select new XMPPStreamFeature((XElement)feature)).ToList();
                                    //Features.ForEach(x => Console.WriteLine("feature received " + x.Name + ", is required " + x.IsRequired + "."));
                                    break;
                                case "failure":
                                    StreamFailure = streamDocElement.Elements().First().Name.LocalName;
                                    logger.Warn("XMPPStream failure: " + StreamFailure);
                                    Close();
                                    throw new ApplicationException("Failure attempting to authenticate XMPP stream.");
                                    break;
                                default:
                                    break;
                            }

                            if (!Exit)
                            {
                                ElementReceived(streamDocElement);
                            }
                        }
                    }
                    else if (xmlReader.NodeType == XmlNodeType.EndElement && xmlReader.Name == STREAM_PREFIX + ":" + STREAM_ELEMENT_NAME)
                    {
                        //Console.WriteLine("Server closed the stream.");
                        Close();
                        break;
                    }

                    if (!Exit)
                    {
                        //Console.WriteLine("xml reader next read...");
                    }
                }

                //m_xmlWriter.Close();
            }

            //Console.WriteLine("Listen exiting.");
        }

        public void Close()
        {
            logger.Debug("Closing XMPPStream.");
            XmlWriter.WriteEndDocument();
            XmlWriter.Flush();
        }

        protected void WriteElement(XElement element)
        {
            element.WriteTo(XmlWriter);
            XmlWriter.Flush();
        }

        private void LoadStreamAttributes(XmlReader xmlReader)
        {
            if (xmlReader.HasAttributes)
            {
                //Console.WriteLine("Attributes of <" + xmlReader.Name + ">");
                while (xmlReader.MoveToNextAttribute())
                {
                    string attributeName = xmlReader.Name;
                    string attributeValue = xmlReader.Value;

                    //Console.WriteLine(" {0}={1}", attributeName, attributeValue);

                    switch (attributeName.ToLower())
                    {
                        case "id":
                            StreamID = attributeValue;
                            break;
                        case "from":
                            m_serverFrom = attributeValue;
                            break;
                        case "to":
                            m_serverTo = attributeValue;
                            break;
                        case "version":
                            Decimal.TryParse(attributeValue, out m_serverVersion);
                            break;
                        case "xmlns":
                            m_serverNamespace = attributeValue;
                            break;
                        case "xmlns:stream":
                            break;
                        default:
                            logger.Warn("Stream attribute " + attributeName + " was not recognised.");
                            break;
                    }
                }
            }
        }

#if !SILVERLIGHT
        // Display any validation errors.
        private static void ValidationCallBack(object sender, ValidationEventArgs e)
        {
            logger.Warn(String.Format("Validation Error: {0}", e.Message));
        }
#endif
    }
}
