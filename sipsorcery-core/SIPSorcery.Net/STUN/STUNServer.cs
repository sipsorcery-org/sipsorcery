//-----------------------------------------------------------------------------
// Filename: STUNServer.cs
//
// Description: Implements a STUN Server as defined in RFC3489.
//
// History:
// 27 Dec 2006	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
using System.Net;
using System.Net.Sockets;
using System.Text;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Net
{
    public delegate void STUNSendMessageDelegate(IPEndPoint dst, byte[] buffer);  // Used so the STUN server can operate in a muli-plexed fashion with things like a SIP server.

    public delegate void STUNServerRequestInTraceDelegate(IPEndPoint localEndPoint, IPEndPoint fromEndPoint, STUNMessage stunMessage);
    public delegate void STUNServerResponseOutTraceDelegate(IPEndPoint localEndPoint, IPEndPoint toEndPoint, STUNMessage stunMessage);

    public class STUNServer
	{
        private static ILog logger = STUNAppState.logger;

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

            m_primaryDiffPortSocket = NetServices.CreateRandomUDPListener(m_primaryEndPoint.Address, out m_primaryDiffPortEndPoint);
            m_secondaryDiffPortSocket = NetServices.CreateRandomUDPListener(m_secondaryEndPoint.Address, out m_secondaryDiffPortEndPoint);

            logger.Debug("STUN Server additional sockets, primary=" + IPSocket.GetSocketString(m_primaryDiffPortEndPoint) + ", secondary=" + IPSocket.GetSocketString(m_secondaryDiffPortEndPoint) + ".");
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
                byte[] stunResponseBuffer = stunResponse.ToByteBuffer();

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
                logger.Debug("Exception STUNPrimaryReceived. " + excp.Message);
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
                byte[] stunResponseBuffer = stunResponse.ToByteBuffer();

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
                logger.Debug("Exception STUNSecondaryReceived. " + excp.Message);
            }
        }

        private STUNMessage GetResponse(IPEndPoint receivedEndPoint, STUNMessage stunRequest, bool primary)
        {
            if (stunRequest.Header.MessageType == STUNMessageTypesEnum.BindingRequest)
            {
                STUNMessage stunResponse = new STUNMessage();
                stunResponse.Header.MessageType = STUNMessageTypesEnum.BindingResponse;
                stunResponse.Header.TransactionId = stunRequest.Header.TransactionId;

                // Add MappedAddress attribute to indicate the socket the request was received from.
                STUNAddressAttribute mappedAddressAtt = new STUNAddressAttribute(STUNAttributeTypesEnum.MappedAddress, receivedEndPoint.Port, receivedEndPoint.Address);
                stunResponse.Attributes.Add(mappedAddressAtt);

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

                // Add ChangedAddress attribute to inidcate the servers alternative socket.
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
                logger.Error("Exception StunServer Stop. " + excp.Message);
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
                logger.Error("Exception FireSTUNPrimaryRequestInTraceEvent. " + excp.Message);
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
                logger.Error("Exception FireSTUNecondaryRequestInTraceEvent. " + excp.Message);
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
                logger.Error("Exception FireSTUNPrimaryResponseOutTraceEvent. " + excp.Message);
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
                logger.Error("Exception FireSTUNSecondaryResponseOutTraceEvent. " + excp.Message);
            }
        }
        
		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class STUNServerUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

			
			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			
			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");
			}
		}

		#endif

		#endregion
	}
}
