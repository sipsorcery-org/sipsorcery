//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example of a simple SIP Proxy Server. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History: 
// 15 Nov 2016	Aaron Clauson	Created, Hobart, Australia.
// 13 Oct 2019  Aaron Clauson   Updated to use the SIPSorcery nuget package.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Notes:
//
// A convenient tool to test SIP applications is [SIPp] (https://github.com/SIPp/sipp). The OPTIONS request handling can 
// be tested from Ubuntu or [WSL] (https://docs.microsoft.com/en-us/windows/wsl/install-win10) using the steps below.
//
// $ sudo apt install sip-tester
// $ wget https://raw.githubusercontent.com/saghul/sipp-scenarios/master/sipp_uac_options.xml
// $ sipp -sf sipp_uac_options.xml -max_socket 1 -r 1 -p 5062 -rp 1000 -trace_err 127.0.0.1
//
// To test registrations (note SIPp returns an error due to no 401 response, if this demo app registers the contact then
// it worked correctly):
//
// $ wget http://tomeko.net/other/sipp/scenarios/REGISTER_client.xml
// $ wget http://tomeko.net/other/sipp/scenarios/REGISTER_SUBSCRIBE_client.csv
// $ sipp 127.0.0.1 -sf REGISTER_client.xml -inf REGISTER_client.csv -m 1 -trace_msg -trace_err 
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Xml;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.SIPProxy
{
    struct SIPAccountBinding
    {
        public SIPAccount SIPAccount;
        public SIPURI RegisteredContact;
        public SIPEndPoint RemoteEndPoint;
        public SIPEndPoint LocalEndPoint;
        public int Expiry;

        public SIPAccountBinding(SIPAccount sipAccount, SIPURI contact, SIPEndPoint remote, SIPEndPoint local, int expiry)
        {
            SIPAccount = sipAccount;
            RegisteredContact = contact;
            RemoteEndPoint = remote;
            LocalEndPoint = local;
            Expiry = expiry;
        }
    }

    class Program
    {
        private static ILogger logger = null;

        private static XmlNode _sipSocketsNode = ProxyState.SIPSocketsNode;                // Optional XML node that can be used to configure the SIP channels used with the SIP transport layer.
        private static IPAddress _defaultLocalAddress = ProxyState.DefaultLocalAddress;   // The default IPv4 address for the machine running the application.
        private static int _defaultSIPUdpPort = SIPConstants.DEFAULT_SIP_PORT;             // The default UDP SIP port.

        private static SIPTransport _sipTransport;
        private static Dictionary<string, SIPAccountBinding> _sipRegistrations = new Dictionary<string, SIPAccountBinding>(); // [SIP Username, Binding], tracks SIP clients that have registered with the server.

        static void Main()
        {
            try
            {
                Console.WriteLine("SIPSorcery SIP Proxy Demo");

                logger = SIPSorcery.Sys.Log.Logger;

                // Configure the SIP transport layer.
                _sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine());

                if (_sipSocketsNode != null)
                {
                    // Set up the SIP channels based on the app.config file.
                    List<SIPChannel> sipChannels = SIPTransportConfig.ParseSIPChannelsNode(_sipSocketsNode);
                    _sipTransport.AddSIPChannel(sipChannels);
                }
                else
                {
                    // Use default options to set up a SIP channel.
                    int port = FreePort.FindNextAvailableUDPPort(_defaultSIPUdpPort);
                    var sipChannel = new SIPUDPChannel(new IPEndPoint(_defaultLocalAddress, port));
                    _sipTransport.AddSIPChannel(sipChannel);
                }

                // Wire up the transport layer so SIP requests and responses have somewhere to go.
                _sipTransport.SIPTransportRequestReceived += SIPTransportRequestReceived;
                _sipTransport.SIPTransportResponseReceived += SIPTransportResponseReceived;

                // If you want to see ALL the nitty gritty SIP traffic wire up the events below.
                //_sipTransport.SIPBadRequestInTraceEvent += SIPBadRequestInTraceEvent;
                //_sipTransport.SIPBadResponseInTraceEvent += SIPBadResponseInTraceEvent;
                _sipTransport.SIPRequestInTraceEvent += SIPRequestInTraceEvent;
                //_sipTransport.SIPRequestOutTraceEvent += SIPRequestOutTraceEvent;
                //_sipTransport.SIPResponseInTraceEvent += SIPResponseInTraceEvent;
                _sipTransport.SIPResponseOutTraceEvent += SIPResponseOutTraceEvent;

                ManualResetEvent mre = new ManualResetEvent(false);
                mre.WaitOne();
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception Main. " + excp);
            }
        }

        /// <summary>
        /// Handler for processing incoming SIP requests.
        /// </summary>
        /// <param name="localSIPEndPoint">The end point the request was received on.</param>
        /// <param name="remoteEndPoint">The end point the request came from.</param>
        /// <param name="sipRequest">The SIP request received.</param>
        private static void SIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    throw new NotImplementedException();
                }
                else if (sipRequest.Method == SIPMethodsEnum.CANCEL)
                {
                    throw new NotImplementedException();
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    throw new NotImplementedException();
                }
                else if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
                {
                    SIPNonInviteTransaction optionsTransaction = _sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                    SIPResponse optionsResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    optionsTransaction.SendFinalResponse(optionsResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.REGISTER)
                {
                    SIPResponse tryingResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Trying, null);
                    _sipTransport.SendResponse(tryingResponse);

                    SIPResponseStatusCodesEnum registerResponse = SIPResponseStatusCodesEnum.Ok;

                    if (sipRequest.Header.Contact != null && sipRequest.Header.Contact.Count > 0)
                    {
                        int expiry = sipRequest.Header.Contact[0].Expires > 0 ? sipRequest.Header.Contact[0].Expires : sipRequest.Header.Expires;
                        var sipAccount = new SIPAccount(null, sipRequest.Header.From.FromURI.Host, sipRequest.Header.From.FromURI.User, null, null);
                        SIPAccountBinding binding = new SIPAccountBinding(sipAccount, sipRequest.Header.Contact[0].ContactURI, remoteEndPoint, localSIPEndPoint, expiry);

                        if (_sipRegistrations.ContainsKey(sipAccount.SIPUsername))
                        {
                            _sipRegistrations.Remove(sipAccount.SIPUsername);
                        }

                        _sipRegistrations.Add(sipAccount.SIPUsername, binding);

                        logger.LogDebug("Registered contact for " + sipAccount.SIPUsername + " as " + binding.RegisteredContact.ToString() + ".");
                    }
                    else
                    {
                        registerResponse = SIPResponseStatusCodesEnum.BadRequest;
                    }

                    SIPNonInviteTransaction registerTransaction = _sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                    SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, registerResponse, null);
                    registerTransaction.SendFinalResponse(okResponse);
                }
                else
                {
                    logger.LogDebug("SIP " + sipRequest.Method + " request received but no processing has been set up for it, rejecting.");
                }
            }
            catch (NotImplementedException)
            {
                logger.LogDebug(sipRequest.Method + " request processing not implemented for " + sipRequest.URI.ToParameterlessString() + " from " + remoteEndPoint + ".");

                SIPNonInviteTransaction notImplTransaction = _sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                SIPResponse notImplResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotImplemented, null);
                notImplTransaction.SendFinalResponse(notImplResponse);
            }
        }

        /// <summary>
        /// Handler for processing incoming SIP responses.
        /// </summary>
        /// <param name="localSIPEndPoint">The end point the response was received on.</param>
        /// <param name="remoteEndPoint">The end point the response came from.</param>
        /// <param name="sipResponse">The SIP response received.</param>
        private static void SIPTransportResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            logger.LogDebug("Response received from " + remoteEndPoint + " method " + sipResponse.Header.CSeqMethod + " status " + sipResponse.Status + ".");
        }

        #region Non-functional trace/logging handlers. Main use is troubleshooting.

        private static void SIPRequestInTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            logger.LogDebug("REQUEST IN {0}->{1}", remoteEndPoint.ToString(), localSIPEndPoint.ToString());
            logger.LogDebug(sipRequest.ToString());
        }

        private static void SIPRequestOutTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            logger.LogDebug("REQUEST OUT {0}->{1}", localSIPEndPoint.ToString(), remoteEndPoint.ToString());
            logger.LogDebug(sipRequest.ToString());
        }

        private static void SIPResponseInTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            logger.LogDebug("RESPONSE IN {0}->{1}", remoteEndPoint.ToString(), localSIPEndPoint.ToString());
            logger.LogDebug(sipResponse.ToString());
        }

        private static void SIPResponseOutTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            logger.LogDebug("RESPONSE OUT {0}->{1}", localSIPEndPoint.ToString(), remoteEndPoint.ToString());
            logger.LogDebug(sipResponse.ToString());
        }

        private static void SIPBadRequestInTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, string message, SIPValidationFieldsEnum sipErrorField, string rawMessage)
        {
            logger.LogWarning("Bad SIPRequest. Field=" + sipErrorField + ", Message=" + message + ", Remote=" + remoteEndPoint.ToString() + ".");
        }

        private static void SIPBadResponseInTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, string message, SIPValidationFieldsEnum sipErrorField, string rawMessage)
        {
            logger.LogWarning("Bad SIPResponse. Field=" + sipErrorField + ", Message=" + message + ", Remote=" + remoteEndPoint.ToString() + ".");
        }

        #endregion
    }
}
