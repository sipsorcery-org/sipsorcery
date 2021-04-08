//-----------------------------------------------------------------------------
// Filename: SDPMessageMediaFormat.cs
//
// Description: Contains enums and helper classes for common definitions
// and attributes used in SDP payloads.
//
// Author(s):
// Jacek Dzija
// Mateusz Greczek
//
// History:
// 30 Mar 2021 Jacek Dzija,Mateusz Greczek Added MSRP
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using SIPSorcery.app.Media;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Net
{
    //An example of MSRP Session class which handle MSRP stack 
    public class MSRPSession : IBaseMediaSession, IDisposable
    {
        public bool IsClosed { get; }
        public SDP RemoteDescription { get; }

        public SDP CreateOffer(IPAddress connectionAddress)
        {
            throw new NotImplementedException();
        }

        public SetDescriptionResultEnum SetRemoteDescription(SdpType sdpType, SDP sessionDescription)
        {
            throw new NotImplementedException();
        }

        public SDP CreateAnswer(IPAddress connectionAddress)
        {
            throw new NotImplementedException();
        }

        public Task Start()
        {
            throw new NotImplementedException();
        }

        public void Close(string reason)
        {
            throw new NotImplementedException();
        }

        public virtual void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            throw new NotImplementedException();
        }
    }
}