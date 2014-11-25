//-----------------------------------------------------------------------------
// Filename: STUNListener.cs
//
// Description: Creates the duplex sockets to listen for STUN client requests.
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
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Net
{
	/*public class IncomingMessage
	{
		public SIPChannel InSIPChannel;
		public IPEndPoint InEndPoint;
		public byte[] Buffer;

		public IncomingMessage(SIPChannel inSIPChannel, IPEndPoint inEndPoint, byte[] buffer)
		{
			InSIPChannel = inSIPChannel;
			InEndPoint = inEndPoint;
			Buffer = buffer;
		}
	}*/

    public delegate void STUNMessageReceived(IPEndPoint receivedEndPoint, IPEndPoint receivedOnEndPoint, byte[] buffer, int bufferLength);
	
	public class STUNListener
	{
        private const string STUN_LISTENER_THREAD_NAME = "stunlistener-";

		public ILog logger = AppState.logger;

		private IPEndPoint m_localEndPoint = null;
		private UdpClient m_stunConn = null;
		private bool m_closed = false;

        public event STUNMessageReceived MessageReceived;

		public IPEndPoint SIPChannelEndPoint
		{
			get{ return m_localEndPoint; }
		}
		
		public STUNListener(IPEndPoint endPoint)
		{	
			try
			{
				m_localEndPoint = InitialiseSockets(endPoint.Address, endPoint.Port);
				logger.Info("STUNListener created " + endPoint.Address + ":" + endPoint.Port + ".");
			}
			catch(Exception excp)
			{
                logger.Error("Exception STUNListener (ctor). " + excp.Message);
				throw excp;
			}
		}

		public void Dispose(bool disposing)
		{
			try
			{
				this.Close();
			}
			catch(Exception excp)
			{
                logger.Error("Exception Disposing STUNListener. " + excp.Message);
			}
		}

		private IPEndPoint InitialiseSockets(IPAddress localIPAddress, int localPort)
		{
			try
			{
				IPEndPoint localEndPoint = null;
				UdpClient stunConn = null;
				
				localEndPoint = new IPEndPoint(localIPAddress, localPort);
				stunConn = new UdpClient(localEndPoint);

				m_stunConn = stunConn;
				
				Thread listenThread = new Thread(new ThreadStart(Listen));
				listenThread.Start();

				return localEndPoint;
			}
			catch(Exception excp)
			{
				logger.Error("Exception STUNListener InitialiseSockets. " + excp.Message);
				throw excp;
			}
		}
			
		private void Listen()
		{
			try
			{
    			UdpClient stunConn = m_stunConn;

				IPEndPoint inEndPoint = new IPEndPoint(IPAddress.Any, 0);
				byte[] buffer = null;

                Thread.CurrentThread.Name = STUN_LISTENER_THREAD_NAME + inEndPoint.Port.ToString();

				while(!m_closed)
				{
					try
					{
						buffer = stunConn.Receive(ref inEndPoint);
					}
					catch(Exception bufExcp)
					{					
						logger.Error("Exception listening in STUNListener. " + bufExcp.Message + ".");
						inEndPoint = new IPEndPoint(IPAddress.Any, 0);
						continue;
					}

					if(buffer == null || buffer.Length == 0)
					{
						logger.Error("Unable to read from STUNListener local end point " + m_localEndPoint.Address.ToString() + ":" + m_localEndPoint.Port);
					}
					else
					{
						if(MessageReceived != null)
                        {
                            try
                            {
                                MessageReceived( m_localEndPoint, inEndPoint, buffer, buffer.Length);
                            }
                            catch (Exception excp)
                            {
                                logger.Error("Exception processing STUNListener MessageReceived. " + excp.Message);
                            }
                        }
					}
				}
			}
			catch(Exception excp)
			{
				logger.Error("Exception STUNListener Listen. " + excp.Message);
				throw excp;
			}
		}

		public virtual void Send(IPEndPoint destinationEndPoint, byte[] buffer)
		{			
			try
			{		
				if(destinationEndPoint == null)
				{
					logger.Error("An empty destination was specified to Send in STUNListener.");
				}

				m_stunConn.Send(buffer, buffer.Length, destinationEndPoint);
			}
			catch(ObjectDisposedException)
			{
				logger.Warn("The STUNListener was not accessible when attempting to send a message to, " + IPSocket.GetSocketString(destinationEndPoint) + ".");
			}
			catch(Exception excp)
			{
				logger.Error("Exception (" + excp.GetType().ToString() + ") STUNListener Send (sendto=>" + IPSocket.GetSocketString(destinationEndPoint) + "). " + excp.Message);
				throw excp;
			}
		}		

		public void Close()
		{
			try
			{
				logger.Debug("Closing STUNListener.");
				
				m_closed = true;
				m_stunConn.Close();
			}
			catch(Exception excp)
			{
				logger.Warn("Exception STUNListener Close. " +excp.Message);
			}
		}

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class STUNListenerUnitTest
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
