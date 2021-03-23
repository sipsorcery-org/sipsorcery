//-----------------------------------------------------------------------------
// Filename: STUNServer.cs
//
// Description: Implements a STUN Server as defined in RFC3489.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Dec 2006	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public delegate void STUNSendMessageDelegate(IPEndPoint dst, byte[] buffer);  // Used so the STUN server can operate in a multiplexed fashion with things like a SIP server.

    public delegate void STUNServerRequestInTraceDelegate(IPEndPoint localEndPoint, IPEndPoint fromEndPoint, STUNMessage stunMessage);
    public delegate void STUNServerResponseOutTraceDelegate(IPEndPoint localEndPoint, IPEndPoint toEndPoint, STUNMessage stunMessage);

    public class STUNServer
    {
        private static ILogger logger = Log.Logger;

        private IPEndPoint m_primaryEndPoint;
        private IPEndPoint m_secondaryEndPoint;
        private IPEndPoint m_primaryDiffPortEndPoint;
        private IPEndPoint m_secondaryDiffPortEndPoint;

        private STUNSendMessageDelegate m_primarySend;
        private STUNSendMessageDelegate m_secondarySend;
        private UdpClient m_primaryDiffPortSocket;
        private UdpClient m_secondaryDiffPortSocket;

        public event STUNServerRequestInTraceDelegate STUNPrimaryRequestInTraceEvent;
        public event STUNServerRequestInTraceDelegate STUNSecondaryRequestInTraceEvent;
        public event STUNServerResponseOutTraceDelegate STUNPrimaryResponseOutTraceEvent;
        public event STUNServerResponseOutTraceDelegate STUNSecondaryResponseOutTraceEvent;

        public STUNServer(IPEndPoint primaryEndPoint, STUNSendMessageDelegate primarySend, IPEndPoint secondaryEndPoint, STUNSendMessageDelegate secondarySend)
        {
            m_primaryEndPoint = primaryEndPoint;
            m_primarySend = primarySend;
            m_secondaryEndPoint = secondaryEndPoint;
            m_secondarySend = secondarySend;

            //m_primaryDiffPortSocket = NetServices.CreateRandomUDPListener(m_primaryEndPoint.Address, out m_primaryDiffPortEndPoint);
            //m_secondaryDiffPortSocket = NetServices.CreateRandomUDPListener(m_secondaryEndPoint.Address, out m_secondaryDiffPortEndPoint);

            m_primaryDiffPortSocket = new UdpClient();
            m_primaryDiffPortSocket.Client = NetServices.CreateBoundUdpSocket(0, m_primaryEndPoint.Address);
            m_primaryDiffPortEndPoint = m_primaryDiffPortSocket.Client.LocalEndPoint as IPEndPoint;

            m_secondaryDiffPortSocket = new UdpClient();
            m_secondaryDiffPortSocket.Client = NetServices.CreateBoundUdpSocket(0, m_primaryEndPoint.Address);
            m_secondaryDiffPortEndPoint = m_secondaryDiffPortSocket.Client.LocalEndPoint as IPEndPoint;

            logger.LogDebug("STUN Server additional sockets, primary=" + IPSocket.GetSocketString(m_primaryDiffPortEndPoint) + ", secondary=" + IPSocket.GetSocketString(m_secondaryDiffPortEndPoint) + ".");
        }

        public void STUNPrimaryReceived(IPEndPoint localEndPoint, IPEndPoint receivedEndPoint, byte[] buffer, int bufferLength)
        {
            try
            {
                //Console.WriteLine("\n=> received from " + IPSocketAddress.GetSocketString(receivedEndPoint) + " on " + IPSocketAddress.GetSocketString(receivedOnEndPoint));
                //Console.WriteLine(Utility.PrintBuffer(buffer));

                STUNMessage stunRequest = STUNMessage.ParseSTUNMessage(buffer, bufferLength);
                //Console.WriteLine(stunRequest.ToString());

                FireSTUNPrimaryRequestInTraceEvent(localEndPoint, receivedEndPoint, stunRequest);

                STUNMessage stunResponse = GetResponse(receivedEndPoint, stunRequest, true);
                byte[] stunResponseBuffer = stunResponse.ToByteBuffer(null, false);

                bool changeAddress = false;
                bool changePort = false;
                foreach (STUNAttribute attr in stunRequest.Attributes)
                {
                    if (attr.AttributeType == STUNAttributeTypesEnum.ChangeRequest)
                    {
                        STUNChangeRequestAttribute changeReqAttr = (STUNChangeRequestAttribute)attr;
                        changeAddress = changeReqAttr.ChangeAddress;
                        changePort = changeReqAttr.ChangePort;
                        break;
                    }
                }

                if (!changeAddress)
                {
                    if (!changePort)
                    {
                        //Console.WriteLine("<= sending to " + IPSocketAddress.GetSocketString(receivedEndPoint) + " from " + IPSocketAddress.GetSocketString(m_primaryEndPoint));
                        m_primarySend(receivedEndPoint, stunResponseBuffer);

                        FireSTUNPrimaryResponseOutTraceEvent(m_primaryEndPoint, receivedEndPoint, stunResponse);
                    }
                    else
                    {
                        //Console.WriteLine("<= sending to " + IPSocketAddress.GetSocketString(receivedEndPoint) + " from " + IPSocketAddress.GetSocketString(m_primaryDiffPortEndPoint));
                        m_primaryDiffPortSocket.Send(stunResponseBuffer, stunResponseBuffer.Length, receivedEndPoint);

                        FireSTUNPrimaryResponseOutTraceEvent(m_primaryDiffPortEndPoint, receivedEndPoint, stunResponse);
                    }
                }
                else
                {
                    if (!changePort)
                    {
                        //Console.WriteLine("<= sending to " + IPSocketAddress.GetSocketString(receivedEndPoint) + " from " + IPSocketAddress.GetSocketString(m_secondaryEndPoint));
                        m_secondarySend(receivedEndPoint, stunResponseBuffer);

                        FireSTUNSecondaryResponseOutTraceEvent(m_secondaryEndPoint, receivedEndPoint, stunResponse);
                    }
                    else
                    {
                        //Console.WriteLine("<= sending to " + IPSocketAddress.GetSocketString(receivedEndPoint) + " from " + IPSocketAddress.GetSocketString(m_secondaryDiffPortEndPoint));
                        m_secondaryDiffPortSocket.Send(stunResponseBuffer, stunResponseBuffer.Length, receivedEndPoint);

                        FireSTUNSecondaryResponseOutTraceEvent(m_secondaryDiffPortEndPoint, receivedEndPoint, stunResponse);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogDebug("Exception STUNPrimaryReceived. " + excp.Message);
            }
        }

        public void STUNSecondaryReceived(IPEndPoint localEndPoint, IPEndPoint receivedEndPoint, byte[] buffer, int bufferLength)
        {
            try
            {
                //Console.WriteLine("\n=> received from " + IPSocketAddress.GetSocketString(receivedEndPoint) + " on " + IPSocketAddress.GetSocketString(receivedOnEndPoint));
                //Console.WriteLine(Utility.PrintBuffer(buffer));

                STUNMessage stunRequest = STUNMessage.ParseSTUNMessage(buffer, bufferLength);
                //Console.WriteLine(stunRequest.ToString());

                FireSTUNSecondaryRequestInTraceEvent(localEndPoint, receivedEndPoint, stunRequest);

                STUNMessage stunResponse = GetResponse(receivedEndPoint, stunRequest, true);
                byte[] stunResponseBuffer = stunResponse.ToByteBuffer(null, false);

                bool changeAddress = false;
                bool changePort = false;
                foreach (STUNAttribute attr in stunRequest.Attributes)
                {
                    if (attr.AttributeType == STUNAttributeTypesEnum.ChangeRequest)
                    {
                        STUNChangeRequestAttribute changeReqAttr = (STUNChangeRequestAttribute)attr;
                        changeAddress = changeReqAttr.ChangeAddress;
                        changePort = changeReqAttr.ChangePort;
                        break;
                    }
                }

                if (!changeAddress)
                {
                    if (!changePort)
                    {
                        //Console.WriteLine("<= sending to " + IPSocketAddress.GetSocketString(receivedEndPoint) + " from " + IPSocketAddress.GetSocketString(m_secondaryEndPoint));
                        m_secondarySend(receivedEndPoint, stunResponseBuffer);

                        FireSTUNSecondaryResponseOutTraceEvent(m_secondaryEndPoint, receivedEndPoint, stunResponse);
                    }
                    else
                    {
                        //Console.WriteLine("<= sending to " + IPSocketAddress.GetSocketString(receivedEndPoint) + " from " + IPSocketAddress.GetSocketString(m_secondaryDiffPortEndPoint));
                        m_secondaryDiffPortSocket.Send(stunResponseBuffer, stunResponseBuffer.Length, receivedEndPoint);

                        FireSTUNSecondaryResponseOutTraceEvent(m_secondaryDiffPortEndPoint, receivedEndPoint, stunResponse);
                    }
                }
                else
                {
                    if (!changePort)
                    {
                        //Console.WriteLine("<= sending to " + IPSocketAddress.GetSocketString(receivedEndPoint) + " from " + IPSocketAddress.GetSocketString(m_primaryEndPoint));
                        m_primarySend(receivedEndPoint, stunResponseBuffer);

                        FireSTUNPrimaryResponseOutTraceEvent(m_primaryEndPoint, receivedEndPoint, stunResponse);
                    }
                    else
                    {
                        //Console.WriteLine("<= sending to " + IPSocketAddress.GetSocketString(receivedEndPoint) + " from " + IPSocketAddress.GetSocketString(m_primaryDiffPortEndPoint));
                        m_primaryDiffPortSocket.Send(stunResponseBuffer, stunResponseBuffer.Length, receivedEndPoint);

                        FireSTUNPrimaryResponseOutTraceEvent(m_primaryDiffPortEndPoint, receivedEndPoint, stunResponse);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogDebug("Exception STUNSecondaryReceived. " + excp.Message);
            }
        }

        private STUNMessage GetResponse(IPEndPoint receivedEndPoint, STUNMessage stunRequest, bool primary)
        {
            if (stunRequest.Header.MessageType == STUNMessageTypesEnum.BindingRequest)
            {
                STUNMessage stunResponse = new STUNMessage();
                stunResponse.Header.MessageType = STUNMessageTypesEnum.BindingSuccessResponse;
                stunResponse.Header.TransactionId = stunRequest.Header.TransactionId;

                // Add MappedAddress attribute to indicate the socket the request was received from.
                STUNAddressAttribute mappedAddressAtt = new STUNAddressAttribute(STUNAttributeTypesEnum.MappedAddress, receivedEndPoint.Port, receivedEndPoint.Address);
                stunResponse.Attributes.Add(mappedAddressAtt);
                stunResponse.AddXORMappedAddressAttribute(receivedEndPoint.Address, receivedEndPoint.Port);//Compatible with the client code

                // Add SourceAddress attribute to indicate the socket used to send the response.
                if (primary)
                {
                    STUNAddressAttribute sourceAddressAtt = new STUNAddressAttribute(STUNAttributeTypesEnum.SourceAddress, m_primaryEndPoint.Port, m_primaryEndPoint.Address);
                    stunResponse.Attributes.Add(sourceAddressAtt);
                }
                else
                {
                    STUNAddressAttribute sourceAddressAtt = new STUNAddressAttribute(STUNAttributeTypesEnum.SourceAddress, m_secondaryEndPoint.Port, m_secondaryEndPoint.Address);
                    stunResponse.Attributes.Add(sourceAddressAtt);
                }

                // Add ChangedAddress attribute to indicate the servers alternative socket.
                if (primary)
                {
                    STUNAddressAttribute changedAddressAtt = new STUNAddressAttribute(STUNAttributeTypesEnum.ChangedAddress, m_secondaryEndPoint.Port, m_secondaryEndPoint.Address);
                    stunResponse.Attributes.Add(changedAddressAtt);
                }
                else
                {
                    STUNAddressAttribute changedAddressAtt = new STUNAddressAttribute(STUNAttributeTypesEnum.ChangedAddress, m_primaryEndPoint.Port, m_primaryEndPoint.Address);
                    stunResponse.Attributes.Add(changedAddressAtt);
                }

                //Console.WriteLine(stunResponse.ToString());

                //byte[] stunResponseBuffer = stunResponse.ToByteBuffer();

                return stunResponse;
            }

            return null;
        }

        public void Stop()
        {
            try
            {
                m_primaryDiffPortSocket.Close();
                m_secondaryDiffPortSocket.Close();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception StunServer Stop. " + excp.Message);
            }
        }

        private void FireSTUNPrimaryRequestInTraceEvent(IPEndPoint localEndPoint, IPEndPoint fromEndPoint, STUNMessage stunMessage)
        {
            try
            {
                if (STUNPrimaryRequestInTraceEvent != null)
                {
                    STUNPrimaryRequestInTraceEvent(localEndPoint, fromEndPoint, stunMessage);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireSTUNPrimaryRequestInTraceEvent. " + excp.Message);
            }
        }

        private void FireSTUNSecondaryRequestInTraceEvent(IPEndPoint localEndPoint, IPEndPoint fromEndPoint, STUNMessage stunMessage)
        {
            try
            {
                if (STUNSecondaryRequestInTraceEvent != null)
                {
                    STUNSecondaryRequestInTraceEvent(localEndPoint, fromEndPoint, stunMessage);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireSTUNecondaryRequestInTraceEvent. " + excp.Message);
            }
        }

        private void FireSTUNPrimaryResponseOutTraceEvent(IPEndPoint localEndPoint, IPEndPoint toEndPoint, STUNMessage stunMessage)
        {
            try
            {
                if (STUNPrimaryResponseOutTraceEvent != null)
                {
                    STUNPrimaryResponseOutTraceEvent(localEndPoint, toEndPoint, stunMessage);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireSTUNPrimaryResponseOutTraceEvent. " + excp.Message);
            }
        }

        private void FireSTUNSecondaryResponseOutTraceEvent(IPEndPoint localEndPoint, IPEndPoint toEndPoint, STUNMessage stunMessage)
        {
            try
            {
                if (STUNSecondaryResponseOutTraceEvent != null)
                {
                    STUNSecondaryResponseOutTraceEvent(localEndPoint, toEndPoint, stunMessage);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireSTUNSecondaryResponseOutTraceEvent. " + excp.Message);
            }
        }
    }
}
