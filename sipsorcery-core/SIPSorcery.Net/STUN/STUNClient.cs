using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{
    public class STUNClient
    {
        private const int STUN_SERVER_RESPONSE_TIMEOUT = 3;

        private static ILog logger = AppState.logger;

        private static readonly int m_defaultSTUNPort = STUNAppState.DEFAULT_STUN_PORT;

        public static IPAddress GetPublicIPAddress(string stunServer)
        {
            try
            {
                logger.Debug("STUNClient attempting to determine public IP from " + stunServer + ".");

                using (UdpClient udpClient = new UdpClient(stunServer, m_defaultSTUNPort))
                {
                        STUNMessage initMessage = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
                        byte[] stunMessageBytes = initMessage.ToByteBuffer();
                        udpClient.Send(stunMessageBytes, stunMessageBytes.Length);

                        IPAddress publicIPAddress = null;
                        ManualResetEvent gotResponseMRE = new ManualResetEvent(false);

                        udpClient.BeginReceive((ar) =>
                        {
                            try
                            {
                                IPEndPoint stunResponseEndPoint = null;
                                byte[] stunResponseBuffer = udpClient.EndReceive(ar, ref stunResponseEndPoint);

                                if (stunResponseBuffer != null && stunResponseBuffer.Length > 0)
                                {
                                    logger.Debug("STUNClient Response to initial STUN message received from " + stunResponseEndPoint + ".");
                                    STUNMessage stunResponse = STUNMessage.ParseSTUNMessage(stunResponseBuffer, stunResponseBuffer.Length);

                                    if (stunResponse.Attributes.Count > 0)
                                    {
                                        foreach (STUNAttribute stunAttribute in stunResponse.Attributes)
                                        {
                                            if (stunAttribute.AttributeType == STUNAttributeTypesEnum.MappedAddress)
                                            {
                                                publicIPAddress = ((STUNAddressAttribute)stunAttribute).Address;
                                                logger.Debug("STUNClient Public IP=" + publicIPAddress.ToString() + ".");
                                            }
                                        }
                                    }
                                }

                                gotResponseMRE.Set();
                            }
                            catch (Exception recvExcp)
                            {
                                logger.Warn("Exception STUNClient Receive. " + recvExcp.Message);
                            }
                        }, null);

                        if (gotResponseMRE.WaitOne(STUN_SERVER_RESPONSE_TIMEOUT * 1000))
                        {
                            return publicIPAddress;
                        }
                        else
                        {
                            logger.Warn("STUNClient server response timedout after " + STUN_SERVER_RESPONSE_TIMEOUT + "s.");
                            return null;
                        }
                    }
            }
            catch (Exception excp)
            {
                logger.Error("Exception STUNClient GetPublicIPAddress. " + excp.Message);
                return null;
                //throw;
            }
        }
    }
}
