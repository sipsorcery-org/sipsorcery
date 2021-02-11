//-----------------------------------------------------------------------------
// Filename: STUNClient.cs
//
// Description: 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// ??	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Methods to resolve the public IP address and port information of the client.
    /// </summary>
    public class STUNClient
    {
        public const int DEFAULT_STUN_PORT = 3478;
        private const int STUN_SERVER_RESPONSE_TIMEOUT = 3;

        private static readonly ILogger logger = Log.Logger;

        /// <summary>
        /// Used to get the public IP address of the client as seen by the STUN server.
        /// </summary>
        /// <param name="stunServer">A server to send STUN requests to.</param>
        /// <param name="port">The port to use for the request. Defaults to 3478.</param>
        /// <returns>The public IP address of the client.</returns>
        public static IPAddress GetPublicIPAddress(string stunServer, int port = DEFAULT_STUN_PORT) =>
            GetPublicIPEndPoint(stunServer, port).Address;

        /// <summary>
        /// Used to get the public IP address and port as seen by the STUN server.
        /// </summary>
        /// <param name="stunServer">A server to send STUN requests to.</param>
        /// <param name="port">The port to use for the request. Defaults to 3478.</param>
        /// <returns>The public IP address and port of the client.</returns>
        public static IPEndPoint GetPublicIPEndPoint(string stunServer, int port = DEFAULT_STUN_PORT)
        {
            try
            {
                logger.LogDebug("STUNClient attempting to determine public IP from " + stunServer + ".");

                using (UdpClient udpClient = new UdpClient(stunServer, port))
                {
                    STUNMessage initMessage = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
                    byte[] stunMessageBytes = initMessage.ToByteBuffer(null, false);
                    udpClient.Send(stunMessageBytes, stunMessageBytes.Length);

                    IPEndPoint publicEndPoint = null;
                    ManualResetEvent gotResponseMRE = new ManualResetEvent(initialState: false);

                    udpClient.BeginReceive((ar) =>
                    {
                        try
                        {
                            IPEndPoint stunResponseEndPoint = null;
                            byte[] stunResponseBuffer = udpClient.EndReceive(ar, ref stunResponseEndPoint);

                            if (stunResponseBuffer != null && stunResponseBuffer.Length > 0)
                            {
                                logger.LogDebug("STUNClient Response to initial STUN message received from " + stunResponseEndPoint + ".");
                                STUNMessage stunResponse = STUNMessage.ParseSTUNMessage(stunResponseBuffer, stunResponseBuffer.Length);

                                if (stunResponse.Attributes.Count > 0)
                                {
                                    foreach (STUNAttribute stunAttribute in stunResponse.Attributes)
                                    {
                                        if (stunAttribute.AttributeType == STUNAttributeTypesEnum.MappedAddress)
                                        {
                                            STUNAddressAttribute stunAddress = (STUNAddressAttribute)stunAttribute;
                                            publicEndPoint = new IPEndPoint(stunAddress.Address, stunAddress.Port);
                                            logger.LogDebug($"STUNClient Public IP={publicEndPoint.Address} Port={publicEndPoint.Port}.");
                                        }
                                    }
                                }
                            }

                            gotResponseMRE.Set();
                        }
                        catch (Exception recvExcp)
                        {
                            logger.LogWarning(recvExcp, "Exception STUNClient Receive. " + recvExcp.Message);
                        }
                    }, state: null);

                    if (gotResponseMRE.WaitOne(STUN_SERVER_RESPONSE_TIMEOUT * 1000))
                    {
                        return publicEndPoint;
                    }
                    else
                    {
                        logger.LogWarning("STUNClient server response timed out after " + STUN_SERVER_RESPONSE_TIMEOUT + "s.");
                        return null;
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception STUNClient GetPublicIPAddress. " + excp.Message);
                return null;
            }
        }
    }
}
