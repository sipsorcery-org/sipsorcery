using SIPSorcery.SIP.App;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SIPSorcery.SIP;

namespace ack_bug_track.Model
{
	public class CallLeg : IDisposable
	{
		public CallLegType LegType
		{
			get;
			set;
		}
		public ISIPClientUserAgent SipClient
		{
			get;
			set;
		}
		public ISIPServerUserAgent SipServer
		{
			get;
			set;
		}
		public CallState CallState
		{
			get;
			set;
		}
		public CallLeg PartnerLeg
		{
			get;
			set;
		}
		public string CallId
		{
			get;
			set;
		}
		public SIPRequest SIPInvite
		{
			get;
			set;
		}
		public SIPFromHeader SIPFrom
		{
			get;
			set;
		}
		public SIPToHeader SIPTo
		{
			get;
			set;
		}
		public SIPContactHeader LocalContact
		{
			get;
			set;
		}
		public SIPContactHeader RemoteContact
		{
			get;
			set;
		}
		public SIPEndPoint SipEndpointLocal
		{
			get;
			set;
		}

		#region IDisposable Support
		private bool disposedValue = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if(!disposedValue)
			{
				if(disposing)
				{
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				disposedValue = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~CallLeg() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}
		#endregion


	}
}
