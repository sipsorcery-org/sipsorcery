using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Web.Services
{
    public class PullNotificationHeader : MessageHeader
    {
        public const string PULL_NOTIFICATION_NAMESPACE = "http://www.sipsorcery.com/pullnotification";
        public const string PULL_NOTIFICATION_PREFIX = "ssnotfn";
        public const string NOTIFICATION_HEADER_NAME = "Notification";
        public const string ADDRESS_ELEMENT_NAME = "Address";

        private static ILog logger = AppState.logger;

        public string Address;

        public override bool MustUnderstand { get { return true; } }
        public override string Name { get { return NOTIFICATION_HEADER_NAME; } }
        public override string Namespace { get { return PULL_NOTIFICATION_NAMESPACE; } }

        public PullNotificationHeader(string address)
        {
            Address = address;
        }

        protected override void OnWriteHeaderContents(XmlDictionaryWriter writer, MessageVersion messageVersion)
        {
            writer.WriteStartElement(PULL_NOTIFICATION_PREFIX, ADDRESS_ELEMENT_NAME, PULL_NOTIFICATION_NAMESPACE);
            writer.WriteString(Address);
            writer.WriteEndElement();
        }

        protected override void OnWriteStartHeader(XmlDictionaryWriter writer, MessageVersion messageVersion)
        {
            writer.WriteStartElement(PULL_NOTIFICATION_PREFIX, this.Name, this.Namespace);
        }

        public static PullNotificationHeader ParseHeader(OperationContext context)
        {
            try
            {
                int headerIndex = context.IncomingMessageHeaders.FindHeader(NOTIFICATION_HEADER_NAME, PULL_NOTIFICATION_NAMESPACE);
                if (headerIndex != -1)
                {
                    XmlDictionaryReader reader = context.IncomingMessageHeaders.GetReaderAtHeader(headerIndex);

                    if (reader.IsStartElement(NOTIFICATION_HEADER_NAME, PULL_NOTIFICATION_NAMESPACE))
                    {
                        reader.ReadStartElement();
                        reader.MoveToContent();

                        if (reader.IsStartElement(ADDRESS_ELEMENT_NAME, PULL_NOTIFICATION_NAMESPACE))
                        {
                            string address = reader.ReadElementContentAsString();
                            return new PullNotificationHeader(address);
                        }
                    }
                }
                return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception PullNotificationHeader ParseHeader. " + excp.Message);
                throw;
            }
        }
    }
}
