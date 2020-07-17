
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Tls;
using SIPSorcery.Net.Sctp;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTCPeerSctpAssociation
    {
        private static readonly ILogger logger = Log.Logger;

        public int SourcePort { get; private set; }

        public int DestinationPort { get; private set; }

        public bool IsClient { get; private set; }

        private ThreadedAssociation _sctpAssociation;

        public RTCPeerSctpAssociation(DatagramTransport dtlsTransport, bool isClient, int srcPort, int dstPort)
        {
            logger.LogDebug($"SCTP creating association is client {isClient} {srcPort}:{dstPort}.");

            IsClient = isClient;
            SourcePort = srcPort;
            DestinationPort = dstPort;

            PeerAssociationListener listener = new PeerAssociationListener(isClient);
            _sctpAssociation = new ThreadedAssociation(dtlsTransport, listener, isClient, srcPort, dstPort);

            //if (!isClient)
            //{
            //    _sctpAssociation.associate();
            //}
        }

        public void Close()
        {
            logger.LogDebug($"SCTP closing association is client {IsClient} {SourcePort}:{DestinationPort}.");

            foreach (int streamID in _sctpAssociation.allStreams())
            {
                _sctpAssociation.getStream(streamID)?.close();
                _sctpAssociation.delStream(streamID);
            }
        }
    }

    public class PeerAssociationListener : AssociationListener
    {
        private static ILogger logger = Log.Logger;

        private bool _isClient;

        public PeerAssociationListener(bool isClient)
        {
            _isClient = isClient;
        }

        public void onAssociated(Association a)
        {
            logger.LogDebug($"Data Channel onAssociated.");

            if (!_isClient)
            {
                var s = a.mkStream("dc123");
                s.setSCTPStreamListener(new DataChannelStreamListener());

                //s.OnOpen = () =>
                //{
                //    logger.LogDebug($"Data Channel stream opened streamID {s.getNum()}.");
                //    _ = Task.Run(async () =>
                //    {
                //        int counter = 1;
                //        while (s.OutboundIsOpen())
                //        {
                //            await Task.Delay(3000);
                //            s.send(counter.ToString());
                //            counter++;
                //        }
                //    });
                //};
            }
        }

        public void onDCEPStream(SCTPStream s, string label, int type)
        {
            logger.LogDebug($"Data Channel onDCEPStream.");
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

    public class DataChannelStreamListener : SCTPStreamListener
    {
        private static ILogger logger = Log.Logger;

        public void close(SCTPStream aThis)
        {
            logger.LogDebug("Data channel stream closed.");
        }

        public void onMessage(SCTPStream s, string message)
        {
            logger.LogDebug($"Data channel stream message label={s.getLabel()} ,streamID={s.getNum()}: {message}.");

            s.send($"got it {message}.");
        }
    }
}
