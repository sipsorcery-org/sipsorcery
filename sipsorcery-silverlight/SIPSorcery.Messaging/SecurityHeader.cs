using System;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Xml;

namespace SIPSorcery.Silverlight.Messaging
{
    public class SecurityHeader : IClientCustomHeader
    {
        private string m_authId;

        public SecurityHeader(string authId)
        {
            m_authId = authId;
        }

        public object BeforeSendRequest(ref Message request, IClientChannel channel)
        {
            request.Headers.Add(MessageHeader.CreateHeader("authid", "", m_authId));
            return null;
        }

        public void AfterReceiveReply(ref System.ServiceModel.Channels.Message reply, object correlationState)
        {
            if (reply != null && reply.Version == MessageVersion.Soap11)
            {
                if (reply.IsFault)
                {
                    throw new RawFaultException(reply.GetReaderAtBodyContents());
                }
            }
        }
    }

    public class RawFaultException : Exception
    {
        private string m_faultMessage;
        private string m_stackTrace;
        private Type m_type;

        public override string Message
        {
            get
            {
                return m_faultMessage;
            }
        }

        public string FaultMessage
        {
            get { return m_faultMessage; }
        }

        public string FaultStackTrace
        {
            get { return m_stackTrace; }
        }

        public Type FaultType
        {
            get { return m_type; }
        }

        public RawFaultException(XmlDictionaryReader reader) :
            base("The service returned a fault - see FaultMessage, FaultStackTrace, and FaultType.")
        {
            reader.ReadToFollowing("Message");
            m_faultMessage = reader.ReadElementContentAsString();
            m_stackTrace = reader.ReadElementContentAsString("StackTrace", reader.NamespaceURI);
            m_type = Type.GetType(reader.ReadElementContentAsString("Type", reader.NamespaceURI));
        }
    }
}
