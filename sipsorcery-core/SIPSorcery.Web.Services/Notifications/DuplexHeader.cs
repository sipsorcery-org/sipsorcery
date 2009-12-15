using System;
using System.Collections.Generic;
using System.Text;
using System.ServiceModel.Channels;
using System.ServiceModel;
using System.Xml;

namespace SIPSorcery.Web.Services
{
    public class DuplexHeader : MessageHeader
    {
        string address;
        string sessionId;

        public override bool MustUnderstand { get { return true; } }
        public override string Name { get { return "Duplex"; } }
        public override string Namespace { get { return "http://schemas.microsoft.com/2008/04/netduplex"; } }
        
        DuplexHeader(string address, string sessionId)
        {
            this.sessionId = sessionId;
            this.address = address;
        }

        public static void AddToMessage(Message message, string address, string sessionId)
        {
            message.Headers.Add(Create(address, sessionId));
        }

        public static DuplexHeader Create(string address, string sessionId)
        {
            if (sessionId == null)
            {
                throw new ArgumentNullException("sessionId");
            }
            return new DuplexHeader(address, sessionId);
        }

        internal static PollingDuplexSession FindHeader(MessageHeaders headers)
        {
            PollingDuplexSession info = null;
            try
            {
                int headerIndex = headers.FindHeader("Duplex", "http://schemas.microsoft.com/2008/04/netduplex");
                if (headerIndex != -1)
                {
                    info = ReadHeaderValue(headers.GetReaderAtHeader(headerIndex));
                }
            }
            catch (XmlException)
            {
            }
            return info;
        }

        protected override void OnWriteHeaderContents(XmlDictionaryWriter writer, MessageVersion messageVersion)
        {
            if (this.address != null)
            {
                writer.WriteStartElement("netdx", "Address", "http://schemas.microsoft.com/2008/04/netduplex");
                writer.WriteString(this.address);
                writer.WriteEndElement();
            }
            writer.WriteStartElement("netdx", "SessionId", "http://schemas.microsoft.com/2008/04/netduplex");
            writer.WriteString(this.sessionId);
            writer.WriteEndElement();
        }

        protected override void OnWriteStartHeader(XmlDictionaryWriter writer, MessageVersion messageVersion)
        {
            writer.WriteStartElement("netdx", this.Name, this.Namespace);
        }

        static PollingDuplexSession ReadHeaderValue(XmlDictionaryReader reader)
        {
            string str = null;
            string str2 = null;
            bool closeHeader = false;
            if (reader.IsStartElement("Duplex", "http://schemas.microsoft.com/2008/04/netduplex"))
            {
                reader.ReadStartElement();
                reader.MoveToContent();
                while (reader.IsStartElement())
                {
                    if (reader.IsStartElement("SessionId", "http://schemas.microsoft.com/2008/04/netduplex"))
                    {
                        if (!string.IsNullOrEmpty(str2))
                        {
                            throw new InvalidOperationException("Multiple sessionId elements in a duplex header.");
                        }
                        str2 = reader.ReadElementContentAsString();
                        if (string.IsNullOrEmpty(str2))
                        {
                            throw new InvalidOperationException("Invalid sessionId element content in a duplex header.");
                        }
                    }
                    else
                    {
                        if (reader.IsStartElement("Address", "http://schemas.microsoft.com/2008/04/netduplex"))
                        {
                            if (!string.IsNullOrEmpty(str))
                            {
                                throw new InvalidOperationException("Multiple address elements in a duplex header.");
                            }
                            str = reader.ReadElementContentAsString();
                            if (string.IsNullOrEmpty(str))
                            {
                                throw new InvalidOperationException("Invalid address element in a duplex header.");
                            }
                            continue;
                        }
                        if (reader.IsStartElement("CloseSession", "http://schemas.microsoft.com/2008/04/netduplex"))
                        {
                            reader.Skip();
                            continue;
                        }
                        reader.Skip();
                    }
                }
                reader.ReadEndElement();
            }
            if (str == null)
            {
                throw new InvalidOperationException("Missing address in a duplex header.");
            }
            if (str2 == null)
            {
                throw new InvalidOperationException("Missing sessionId in a duplex header.");
            }
            return new PollingDuplexSession(str, str2);
        }
    }
}
