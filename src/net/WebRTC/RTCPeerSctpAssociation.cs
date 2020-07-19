using System.Collections.Generic;
using System.Linq;
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

        public Dictionary<int, SCTPStream> Streams = new Dictionary<int, SCTPStream>();

        public RTCPeerSctpAssociation(DatagramTransport dtlsTransport, bool isClient, int srcPort, int dstPort)
        {
            logger.LogDebug($"SCTP creating association is client {isClient} {srcPort}:{dstPort}.");

            IsClient = isClient;
            SourcePort = srcPort;
            DestinationPort = dstPort;

            PeerAssociationListener listener = new PeerAssociationListener(isClient);
            _sctpAssociation = new ThreadedAssociation(dtlsTransport, listener, isClient, srcPort, dstPort);
            
            // Network send.
            _sctpAssociation.associate();
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

        public void CreateStream(string label)
        {
            logger.LogDebug($"SCTP creating stream for label {label}.");
            var stm = _sctpAssociation.mkStream(label);
            Streams.Add(stm.getNum(), stm);
        }

        public void Send(string label, string message)
        {
            var stm = Streams.Where(x => x.Value.getLabel() == label).Select(x => x.Value).FirstOrDefault();

            if(stm == null)
            {
                logger.LogWarning($"SCTP no stream was found for label {label}.");
            }
            else
            {
                stm.send(message);
            }
        }
    }

    public class PeerAssociationListener : AssociationListener
    {
        private static ILogger logger = Log.Logger;

        private bool _isClient;
        private Association _association;

        public PeerAssociationListener(bool isClient)
        {
            _isClient = isClient;
        }

        public void onAssociated(Association a)
        {
            logger.LogDebug($"Data Channel onAssociated.");
            _association = a;
        }

        public void onDCEPStream(SCTPStream s, string label, int type)
        {
            logger.LogDebug($"Data channel stream created for label {label}, type {type}, id {s.getNum()}.");
            s.setSCTPStreamListener(new DataChannelStreamListener());
        }

        public void onDisAssociated(Association a)
        {
            logger.LogDebug($"Data Channel onDisAssociated.");
        }

        public void onRawStream(SCTPStream s)
        {
            logger.LogDebug($"Data Channel onRawStream label {s.getLabel()}, id {s.getNum()}.");
            s.setSCTPStreamListener(new DataChannelStreamListener());
        }
    }

    public class DataChannelStreamListener : SCTPStreamListener
    {
        private static ILogger logger = Log.Logger;

        public void close(SCTPStream s)
        {
            logger.LogDebug($"Data channel stream closed id {s.getNum()}.");
        }

        public void onMessage(SCTPStream s, string message)
        {
            logger.LogDebug($"Data channel received message (label={s.getLabel()}, streamID={s.getNum()}): {message}.");

            s.send($"got it {message}.");
        }
    }
}
