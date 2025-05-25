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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Bcpg;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

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
        GetPublicIPEndPoint(stunServer, port)?.Address;

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
            logger.LogDebug("STUNClient attempting to determine public IP from {stunServer}.", stunServer);

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
                            logger.LogDebug("STUNClient Response to initial STUN message received from {stunResponseEndPoint}.", stunResponseEndPoint);
                            STUNMessage stunResponse = STUNMessage.ParseSTUNMessage(stunResponseBuffer, stunResponseBuffer.Length);

                            if (stunResponse.Attributes.Count > 0)
                            {
                                foreach (STUNAttribute stunAttribute in stunResponse.Attributes)
                                {
                                    if (stunAttribute.AttributeType == STUNAttributeTypesEnum.MappedAddress)
                                    {
                                        STUNAddressAttribute stunAddress = (STUNAddressAttribute)stunAttribute;
                                        publicEndPoint = new IPEndPoint(stunAddress.Address, stunAddress.Port);
                                        break;
                                    }
                                    else if (stunAttribute.AttributeType == STUNAttributeTypesEnum.XORMappedAddress)
                                    {
                                        STUNXORAddressAttribute stunAddress = (STUNXORAddressAttribute)stunAttribute;
                                        publicEndPoint = new IPEndPoint(stunAddress.Address, stunAddress.Port);
                                        break;
                                    }
                                }

                                if (publicEndPoint != null)
                                {
                                    logger.LogDebug("STUNClient Public IP={publicEndPointAddress} Port={publicEndPointPort}.", publicEndPoint.Address, publicEndPoint.Port);
                                }
                            }
                        }

                        gotResponseMRE.Set();
                    }
                    catch (Exception recvExcp)
                    {
                        logger.LogWarning(recvExcp, "Exception STUNClient Receive. {ErrorMessage}", recvExcp.Message);
                    }
                }, state: null);

                if (gotResponseMRE.WaitOne(STUN_SERVER_RESPONSE_TIMEOUT * 1000))
                {
                    return publicEndPoint;
                }
                else
                {
                    logger.LogWarning("STUNClient server response timed out after {Timeout}s.", STUN_SERVER_RESPONSE_TIMEOUT);
                    return null;
                }
            }
        }
        catch (Exception excp)
        {
            logger.LogError(excp, "Exception STUNClient GetPublicIPAddress. {ErrorMessage}", excp.Message);
            return null;
        }
    }

    /// <summary>
    /// Used to get the public IP address and port as seen by the STUN server for an existing RTP Channel. For
    /// example this can be used to find the public IP address and port of an RTP channel that has already been created.
    /// Note this method can cause an RTP socket to become unreachable unless used behind a full cone NAT. The reason
    /// being that a non-full cone NAT will only allow ingress packets from the STUN server.
    /// </summary>
    /// <param name="stunServer">The STUN server endpoint to use.</param>
    /// <param name="rtpChannel">An existing RTP channel.</param>
    /// <returns>The public IP address and port of the RTP channel.</returns>
    public static async Task<IPEndPoint> GetPublicIPEndPointForSocketAsync(
        IPEndPoint stunServer,
        RTPChannel rtpChannel)
    {
        var tcs = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnRtpDataReceived(int localPort, IPEndPoint remoteEndPoint, byte[] packet)
        {
            try
            {
                if (packet?.Length > 0)
                {
                    logger.LogDebug("STUNClient response received from {StunResponseEndPoint}.", remoteEndPoint);

                    var stunResponse = STUNMessage.ParseSTUNMessage(packet, packet.Length);
                    IPEndPoint result = null;

                    foreach (var attr in stunResponse.Attributes)
                    {
                        if (attr.AttributeType == STUNAttributeTypesEnum.MappedAddress &&
                            attr is STUNAddressAttribute mapped)
                        {
                            result = new IPEndPoint(mapped.Address, mapped.Port);
                            break;
                        }
                        else if (attr.AttributeType == STUNAttributeTypesEnum.XORMappedAddress &&
                            attr is STUNXORAddressAttribute xorMapped)
                        {
                            result = new IPEndPoint(xorMapped.Address, xorMapped.Port);
                            break;
                        }
                    }

                    if (result != null)
                    {
                        logger.LogDebug("STUNClient public IP={PublicAddress} Port={PublicPort}.", result.Address, result.Port);
                    }

                    tcs.TrySetResult(result);
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        rtpChannel.OnRTPDataReceived += OnRtpDataReceived;

        try
        {
            logger.LogDebug("STUNClient sending BindingRequest for RTP channel {LocalEndPoint} to {StunServer}.", rtpChannel.RTPLocalEndPoint, stunServer);

            var initMessage = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            var bytes = initMessage.ToByteBuffer(null, false);
            rtpChannel.Send(RTPChannelSocketsEnum.RTP, stunServer, bytes);

            // Race the response against a timeout.
            var delayTask = Task.Delay(TimeSpan.FromSeconds(STUN_SERVER_RESPONSE_TIMEOUT));
            var winner = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);

            if (winner == delayTask)
            {
                logger.LogWarning("STUNClient server response timed out after {Timeout}s.", STUN_SERVER_RESPONSE_TIMEOUT);
                return null;
            }

            // Either a result (possibly null) or an exception
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception in STUNClient.GetPublicIPEndPointForSocketAsync: {ErrorMessage}", ex.Message);
            return null;
        }
        finally
        {
            rtpChannel.OnRTPDataReceived -= OnRtpDataReceived;
        }
    }
}
