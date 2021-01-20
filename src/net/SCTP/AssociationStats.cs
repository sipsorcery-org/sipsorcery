using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace SIPSorcery.Net.Sctp
{
    public class AssociationStats
    {
        private object myLock = new object();
        private UInt64 nDATAs;
        private UInt64 nSACKs;
        private UInt64 nT3Timeouts;    
        private UInt64 nAckTimeouts;
        private UInt64 nFastRetrans;
        public void incSACKs()
        {
            lock (myLock)
            {
                nSACKs++;
            }
        }

        internal void incAckTimeouts()
        {
            lock (myLock)
            {
                nAckTimeouts++;
            }
        }

        internal void incT3Timeouts()
        {
            lock (myLock)
            {
                nT3Timeouts++;
            }
        }

        internal void incFastRetrans()
        {
            lock (myLock)
            {
                nFastRetrans++;
            }
        }
    }
}
