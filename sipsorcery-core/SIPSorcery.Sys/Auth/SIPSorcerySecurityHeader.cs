using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Sys.Auth
{
    public class SIPSorcerySecurityHeader : MessageHeader
    {
        private const string SECURITY_NAMESPACE = "http://www.sipsorcery.com/security";
        private const string SECURITY_HEADER_NAME = "Security";
        private const string SECURITY_PREFIX = "sssec";
        private const string AUTHID_ELEMENT_NAME = "AuthID";

        private static ILog logger = AppState.logger;

        public string AuthID;

        public override bool MustUnderstand { get { return true; } }
        public override string Name { get { return SECURITY_HEADER_NAME; } }
        public override string Namespace { get { return SECURITY_NAMESPACE; } }

        public SIPSorcerySecurityHeader(string authID)
        {
            AuthID = authID;
        }

        protected override void OnWriteHeaderContents(XmlDictionaryWriter writer, MessageVersion messageVersion)
        {
            writer.WriteStartElement(SECURITY_PREFIX, AUTHID_ELEMENT_NAME, SECURITY_NAMESPACE);
            writer.WriteString(AuthID);
            writer.WriteEndElement();
        }

        protected override void OnWriteStartHeader(XmlDictionaryWriter writer, MessageVersion messageVersion)
        {
            writer.WriteStartElement(SECURITY_PREFIX, this.Name, this.Namespace);
        }

        public static SIPSorcerySecurityHeader ParseHeader(OperationContext context)
        {
            try
            {
                int headerIndex = context.IncomingMessageHeaders.FindHeader(SECURITY_HEADER_NAME, SECURITY_NAMESPACE);
                if (headerIndex != -1)
                {
                    XmlDictionaryReader reader = context.IncomingMessageHeaders.GetReaderAtHeader(headerIndex);

                    if (reader.IsStartElement(SECURITY_HEADER_NAME, SECURITY_NAMESPACE))
                    {
                        reader.ReadStartElement();
                        reader.MoveToContent();

                        if (reader.IsStartElement(AUTHID_ELEMENT_NAME, SECURITY_NAMESPACE))
                        {
                            string authID = reader.ReadElementContentAsString();
                            return new SIPSorcerySecurityHeader(authID);
                        }
                    }
                }
                 return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPSorcerySecurityHeader ParseHeader. " + excp.Message);
                throw;
            }
        }
    }
}
