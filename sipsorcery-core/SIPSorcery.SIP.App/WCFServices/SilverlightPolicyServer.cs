//-----------------------------------------------------------------------------
// Filename: SilverlightPolicyServer.cs
//
// Description: Listens for requests from Silverlight clients for policy files. Silverlight
// will request the policy file before allowing a socket connection to a host. The code is
// derived from the example provided by Microsoft in the Silverlight 2.2 Beta SDK.
// 
// History:
// 23 Sep 2008	    Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
using System.IO;
using System.Net;
using System.Net.Sockets;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    public class SilverlightPolicyServer
    {
        private ILog logger = AppState.logger;

        private Socket m_listener;
        private byte[] m_policy;

        // pass in the path of an XML file containing the socket policy 
        public SilverlightPolicyServer(string policyFile)
        {
            // Load the policy file 
            FileStream policyStream = new FileStream(policyFile, FileMode.Open);
            m_policy = new byte[policyStream.Length];
            policyStream.Read(m_policy, 0, m_policy.Length);
            policyStream.Close();

            // Create the Listening Socket 
            m_listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Put the socket into dual mode to allow a single socket 
            // to accept both IPv4 and IPv6 connections 
            // Otherwise, server needs to listen on two sockets, 
            // one for IPv4 and one for IPv6 
            // NOTE: dual-mode sockets are supported on Vista and later 
            //m_listener.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, 0); 
            m_listener.Bind(new IPEndPoint(IPAddress.Any, 943));
            m_listener.Listen(10);

            m_listener.BeginAccept(new AsyncCallback(OnConnection), null);
        }

        // Called when we receive a connection from a client 
        public void OnConnection(IAsyncResult res)
        {
            Socket client = null;

            try
            {
                client = m_listener.EndAccept(res);
            }
            catch (SocketException sockExcp)
            {
                logger.Error("Exception SilverlightPolicyServer OnConnection. " + sockExcp + ".");
                return;
            }

            logger.Debug("SilverlightPolicyServer connection from " + client.RemoteEndPoint + ".");

            // handle this policy request with a PolicyConnection 
            PolicyConnection pc = new PolicyConnection(client, m_policy);

            // look for more connections 
            m_listener.BeginAccept(new AsyncCallback(OnConnection), null);
        }

        public void Close()
        {
            m_listener.Close();
        }
    } 

    /// <summary>
    ///  Encapsulate and manage state for a single connection from a client 
    /// </summary>
    class PolicyConnection
    {
        private Socket m_connection;
        private byte[] m_buffer;    // buffer to receive the request from the client 
        private int m_received;
        private byte[] m_policy;    // the policy to return to the client 
        private static string s_policyRequestString = "<policy-file-request/>";  // the request that we're expecting from the client 

        public PolicyConnection(Socket client, byte[] policy)
        {
            m_connection = client;
            m_policy = policy;
            m_buffer = new byte[s_policyRequestString.Length];
            m_received = 0;

            try
            {
                // receive the request from the client 
                m_connection.BeginReceive(m_buffer, 0, s_policyRequestString.Length, SocketFlags.None, new AsyncCallback(OnReceive), null);
            }
            catch (SocketException)
            {
                m_connection.Close();

            }
        }

        // Called when we receive data from the client 
        private void OnReceive(IAsyncResult res)
        {
            try
            {
                m_received += m_connection.EndReceive(res);

                // if we haven't gotten enough for a full request yet, receive again 
                if (m_received < s_policyRequestString.Length)
                {
                    m_connection.BeginReceive(m_buffer, m_received, s_policyRequestString.Length - m_received, SocketFlags.None, new AsyncCallback(OnReceive), null);
                    return;
                }

                // make sure the request is valid 
                string request = System.Text.Encoding.UTF8.GetString(m_buffer, 0, m_received);
                if (StringComparer.InvariantCultureIgnoreCase.Compare(request, s_policyRequestString) != 0)
                {
                    m_connection.Close();
                    return;
                }

                // send the policy 
                m_connection.BeginSend(m_policy, 0, m_policy.Length, SocketFlags.None, new AsyncCallback(OnSend), null);

            }
            catch (SocketException)
            {
                m_connection.Close();
            }
        }

        // called after sending the policy to the client; close the connection. 
        public void OnSend(IAsyncResult res)
        {
            try
            {
                m_connection.EndSend(res);
            }
            finally
            {
                m_connection.Close();
            }
        }
    }
}
