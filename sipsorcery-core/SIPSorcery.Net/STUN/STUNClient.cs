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
        private static ILog logger = AppState.logger;

        private static readonly int m_defaultSTUNPort = STUNAppState.DEFAULT_STUN_PORT;

        public static IPAddress GetPublicIPAddress(string stunServer) {
            try {

                logger.Debug("STUNClient attempting to determine public IP from " + stunServer + ".");

                UdpClient udpClient = new UdpClient(stunServer, m_defaultSTUNPort);
                STUNMessage initMessage = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
                byte[] stunMessageBytes = initMessage.ToByteBuffer();
                udpClient.Send(stunMessageBytes, stunMessageBytes.Length);

                IPEndPoint stunResponseEndPoint = null;
                byte[] stunResponseBuffer = udpClient.Receive(ref stunResponseEndPoint);

                if (stunResponseBuffer != null && stunResponseBuffer.Length > 0) {
                    logger.Debug("STUNClient Response to initial STUN message received from " + stunResponseEndPoint + ".");
                    STUNMessage stunResponse = STUNMessage.ParseSTUNMessage(stunResponseBuffer, stunResponseBuffer.Length);

                    if (stunResponse.Attributes.Count > 0) {
                        foreach (STUNAttribute stunAttribute in stunResponse.Attributes) {
                            if (stunAttribute.AttributeType == STUNAttributeTypesEnum.MappedAddress) {
                                IPAddress publicAddress = ((STUNAddressAttribute)stunAttribute).Address;
                                logger.Debug("STUNClient Public IP=" + publicAddress.ToString() + ".");
                                return publicAddress;
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception excp) {
                logger.Error("Exception STUNClient GetPublicIPAddress. " + excp.Message);
                throw;
            }
        }
    }
}
