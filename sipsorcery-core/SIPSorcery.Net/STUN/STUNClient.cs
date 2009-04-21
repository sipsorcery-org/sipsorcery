using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{
    public class STUNClient
    {
        private ILog logger = AppState.logger;

        private UdpClient m_udpClient;
        private IPEndPoint m_stunServerEndPoint;

        public STUNClient(IPAddress localAddress, IPEndPoint stunServerEndPoint)
        {
            m_udpClient = new UdpClient(new IPEndPoint(localAddress, 0));
            m_stunServerEndPoint = stunServerEndPoint;
        }

        public void GetPublicIPAddress(object state)
        {
            try
            {
                logger.Debug("Sending initial STUN message to " + m_stunServerEndPoint + ".");

                STUNMessage initMessage = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
                byte[] stunMessageBytes = initMessage.ToByteBuffer();
                m_udpClient.Send(stunMessageBytes, stunMessageBytes.Length, m_stunServerEndPoint);

                IPEndPoint stunResponseEndPoint = null;
                byte[] stunResponseBuffer = m_udpClient.Receive(ref stunResponseEndPoint);

                if (stunResponseBuffer != null && stunResponseBuffer.Length > 0)
                {
                    logger.Debug("Response to initial STUN message received from " + stunResponseEndPoint + ".");
                    STUNMessage stunResponse = STUNMessage.ParseSTUNMessage(stunResponseBuffer, stunResponseBuffer.Length);

                    if (stunResponse.Attributes.Count > 0)
                    {
                        foreach (STUNAttribute stunAttribute in stunResponse.Attributes)
                        {
                            if (stunAttribute.AttributeType == STUNAttributeTypesEnum.MappedAddress)
                            {
                                logger.Debug("Public IP=" + ((STUNAddressAttribute)stunAttribute).Address.ToString() + ".");
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetPublicIPAddress. " + excp.Message);
                throw;
            }
        }
    }
}
