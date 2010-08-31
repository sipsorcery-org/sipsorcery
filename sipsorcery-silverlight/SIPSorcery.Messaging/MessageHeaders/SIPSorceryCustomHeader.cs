using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Xml;

namespace SIPSorcery.Silverlight.Messaging
{
    public class SIPSorceryCustomHeader : IClientCustomHeader
    {
        private List<MessageHeader> m_messageHeaders;
        //private string m_authId;

        public SIPSorceryCustomHeader(List<MessageHeader> messageHeaders)
        {
            m_messageHeaders = messageHeaders;
        }

        //public SecurityHeader(string authId)
        //{
        //    m_authId = authId;
        //}

        public object BeforeSendRequest(ref Message request, IClientChannel channel)
        {
            //request.Headers.Add(MessageHeader.CreateHeader("authid", "", m_authId));
            foreach(MessageHeader messageHeader in m_messageHeaders)
            {
                request.Headers.Add(messageHeader);
            }

            return null;
        }

        public void AfterReceiveReply(ref Message reply, object correlationState)
        { }
    }
}
