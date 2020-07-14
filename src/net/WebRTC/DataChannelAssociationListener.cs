using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using pe.pi.sctp4j.sctp;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class DataChannelAssociationListener : pe.pi.sctp4j.sctp.AssociationListener
    {
        private static ILogger logger = Log.Logger;

        public void onAssociated(Association a)
        {
            logger.LogDebug($"Data Channel onAssociated.");
        }

        public void onDCEPStream(SCTPStream s, string label, int type)
        {
            logger.LogDebug($"Data Channel onDCEPStream.");
            string test = "Test message";
            //SCTPMessage m = s.getAssociation().makeMessage(test, (pe.pi.sctp4j.sctp.small.BlockingSCTPStream)s);
            //SCTPMessage m = instanceLeft.makeMessage(test, (BlockingSCTPStream)s);
            s.send(test);

            s.setSCTPStreamListener(new DataChannelStreamListener());
        }

        public void onDisAssociated(Association a)
        {
            logger.LogDebug($"Data Channel onDisAssociated.");
        }

        public void onRawStream(SCTPStream s)
        {
            logger.LogDebug($"Data Channel onRawStream.");
        }
    }

    public class DataChannelStreamListener : pe.pi.sctp4j.sctp.SCTPStreamListener
    {
        private static ILogger logger = Log.Logger;

        public void close(SCTPStream aThis)
        {
            logger.LogDebug("Data channel stream closed.");
        }

        public void onMessage(SCTPStream s, string message)
        {
            logger.LogDebug($"Data channel stream message {message}.");
        }
    }
}
