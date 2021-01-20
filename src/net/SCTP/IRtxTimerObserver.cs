using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.Net.Sctp
{
    public interface IRtxTimerObserver
    {
        void onRetransmissionTimeout(int timerID, uint n);
        void onRetransmissionFailure(int timerID);
    }
}
