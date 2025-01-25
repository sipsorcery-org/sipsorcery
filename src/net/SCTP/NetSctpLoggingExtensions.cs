using System;
using System.Net;
using Microsoft.Extensions.Logging;
using TinyJson;

namespace SIPSorcery.Net
{
    internal static partial class NetSdpLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0, 
            EventName = "SctpPacketReceivedAborted", 
            Level = LogLevel.Warning, 
            Message = "SCTP packet received but association has been aborted, ignoring.")]
        public static partial void LogSctpPacketReceivedAborted(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpPacketDroppedWrongVerificationTag",
            Level = LogLevel.Warning,
            Message = "SCTP packet dropped due to wrong verification tag, expected {ExpectedVerificationTag} got {ReceivedVerificationTag}.")]
        public static partial void LogSctpPacketDroppedWrongVerificationTag(
            this ILogger logger,
            uint expectedVerificationTag,
            uint receivedVerificationTag);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpPacketDroppedWrongDestinationPort",
            Level = LogLevel.Warning,
            Message = "SCTP packet dropped due to wrong SCTP destination port, expected {ExpectedDestinationPort} got {ReceivedDestinationPort}.")]
        public static partial void LogSctpPacketDroppedWrongDestinationPort(
            this ILogger logger,
            ushort expectedDestinationPort,
            ushort receivedDestinationPort);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpPacketDroppedWrongSourcePort",
            Level = LogLevel.Warning,
            Message = "SCTP packet dropped due to wrong SCTP source port, expected {ExpectedSourcePort} got {ReceivedSourcePort}.")]
        public static partial void LogSctpPacketDroppedWrongSourcePort(
            this ILogger logger,
            ushort expectedSourcePort,
            ushort receivedSourcePort);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpDataChunkReceived",
            Level = LogLevel.Trace,
            Message = "SCTP data chunk received on ID {ID} with TSN {TSN}, payload length {PayloadLength}, flags {Flags}.")]
        public static partial void LogSctpDataChunkReceived(
            this ILogger logger,
            string id,
            uint tsn,
            int payloadLength,
            byte flags);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpPacketAbortChunkReceived",
            Level = LogLevel.Warning,
            Message = "SCTP packet ABORT chunk received from remote party, reason {Reason}.")]
        public static partial void LogSctpPacketAbortChunkReceived(
            this ILogger logger,
            string reason);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpErrorReceived",
            Level = LogLevel.Warning,
            Message = "SCTP error {CauseCode}.")]
        public static partial void LogSctpErrorReceived(
            this ILogger logger,
            SctpErrorCauseCode causeCode);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSendingShutdown",
            Level = LogLevel.Trace,
            Message = "SCTP sending shutdown for association {ID}, ACK TSN {ackTSN}.")]
        public static partial void LogSctpSendingShutdown(
            this ILogger logger,
            string id,
            uint? ackTSN);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpStateChanged",
            Level = LogLevel.Trace,
            Message = "SCTP state for association {ID} changed to {State}.")]
        public static partial void LogSctpStateChanged(
            this ILogger logger,
            string id,
            SctpAssociationState state);

        [LoggerMessage(
            EventId = 0,
            EventName = "ReceivedChunk",
            Level = LogLevel.Trace,
            Message = "SCTP receiver got data chunk with TSN {TSN}, last in order TSN {LastInOrderTSN}, in order receive count {InOrderReceiveCount}.")]
        public static partial void LogReceivedChunk(
            this ILogger logger,
            uint tsn,
            uint lastInOrderTSN,
            uint inOrderReceiveCount);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpDuplicateTsnReceived",
            Level = LogLevel.Trace,
            Message = "SCTP duplicate TSN received for {TSN}.")]
        public static partial void LogSctpDuplicateTsnReceived(
            this ILogger logger,
            uint tsn);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSenderExitingRetransmitMode",
            Level = LogLevel.Trace,
            Message = "SCTP sender exiting retransmit mode.")]
        public static partial void LogSctpSenderExitingRetransmitMode(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpFirstSackReceived",
            Level = LogLevel.Trace,
            Message = "SCTP first SACK remote peer TSN ACK {CumulativeTsnAck} next sender TSN {TSN}, arwnd {ARwnd} (gap reports {GapAckBlocksCount}).")]
        public static partial void LogSctpFirstSackReceived(
            this ILogger logger,
            uint cumulativeTsnAck,
            uint tsn,
            uint arwnd,
            int gapAckBlocksCount);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSackTsnTooDistant",
            Level = LogLevel.Warning,
            Message = "SCTP SACK TSN from remote peer of {CumulativeTsnAck} was too distant from the expected {CumulativeAckTSN}, ignoring.")]
        public static partial void LogSctpSackTsnTooDistant(
            this ILogger logger,
            uint cumulativeTsnAck,
            uint cumulativeAckTSN);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSackTsnBehindExpected",
            Level = LogLevel.Warning,
            Message = "SCTP SACK TSN from remote peer of {CumulativeTsnAck} was behind expected {CumulativeAckTSN}, ignoring.")]
        public static partial void LogSctpSackTsnBehindExpected(
            this ILogger logger,
            uint cumulativeTsnAck,
            uint cumulativeAckTSN);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSackReceived",
            Level = LogLevel.Trace,
            Message = "SCTP SACK remote peer TSN ACK {CumulativeTsnAck}, next sender TSN {TSN}, arwnd {ARwnd} (gap reports {GapAckBlocksCount}).")]
        public static partial void LogSctpSackReceived(
            this ILogger logger,
            uint cumulativeTsnAck,
            uint tsn,
            uint arwnd,
            int gapAckBlocksCount);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSackReceivedNoChange",
            Level = LogLevel.Trace,
            Message = "SCTP SACK remote peer TSN ACK no change {CumulativeAckTSN}, next sender TSN {TSN}, arwnd {ARwnd} (gap reports {GapAckBlocksCount}).")]
        public static partial void LogSctpSackReceivedNoChange(
            this ILogger logger,
            uint cumulativeAckTSN,
            uint tsn,
            uint arwnd,
            int gapAckBlocksCount);

        [LoggerMessage(
            EventId = 0,
            EventName = "ExitingFastRecovery",
            Level = LogLevel.Trace,
            Message = "SCTP sender exiting fast recovery at TSN {FastRecoveryExitPoint}")]
        public static partial void LogExitingFastRecovery(
            this ILogger logger,
            uint fastRecoveryExitPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpAcknowledgedDataChunkReceipt",
            Level = LogLevel.Trace,
            Message = "SCTP acknowledged data chunk receipt in gap report for TSN {TSN}")]
        public static partial void LogSctpAcknowledgedDataChunkReceipt(
            this ILogger logger,
            uint tsn);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSackGapReport",
            Level = LogLevel.Trace,
            Message = "SCTP SACK gap report start TSN {goodTSNStart} gap report end TSN {gapBlockEnd} first missing TSN {missingTSN}.")]
        public static partial void LogSctpSackGapReport(
            this ILogger logger,
            uint goodTSNStart,
            uint gapBlockEnd,
            uint missingTSN);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSackGapAddingRetransmitEntry",
            Level = LogLevel.Trace,
            Message = "SCTP SACK gap adding retransmit entry for TSN {TSN}.")]
        public static partial void LogSctpSackGapAddingRetransmitEntry(
            this ILogger logger,
            uint tsn);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSenderEnteringFastRecoveryMode",
            Level = LogLevel.Trace,
            Message = "SCTP sender entering fast recovery mode due to missing TSN {MissingTsn}. Fast recovery exit point {FastRecoveryExitPoint}.")]
        public static partial void LogSctpSenderEnteringFastRecoveryMode(
            this ILogger logger,
            uint missingTsn,
            uint fastRecoveryExitPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSenderRemovingUnconfirmedChunks",
            Level = LogLevel.Trace,
            Message = "SCTP data sender removing unconfirmed chunks cumulative ACK TSN {CumulativeAckTsn}, SACK TSN {SackTsn}.")]
        public static partial void LogSctpSenderRemovingUnconfirmedChunks(
            this ILogger logger,
            uint cumulativeAckTSN,
            uint sackTSN);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpResendingMissingDataChunk",
            Level = LogLevel.Trace,
            Message = "SCTP resending missing data chunk for TSN {TSN}, data length {UserDataLength}, flags {ChunkFlags:X2}, send count {SendCount}.")]
        public static partial void LogSctpResendingMissingDataChunk(
            this ILogger logger,
            uint tSN,
            int userDataLength,
            byte chunkFlags,
            int sendCount);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpRetransmittingDataChunk",
            Level = LogLevel.Trace,
            Message = "SCTP retransmitting data chunk for TSN {Tsn}, data length {DataLength}, flags {ChunkFlags}, send count {SendCount}.")]
        public static partial void LogSctpRetransmittingDataChunk(
            this ILogger logger,
            uint tSN,
            int dataLength,
            byte chunkFlags,
            int sendCount);

        [LoggerMessage(
            EventId = 0,
            EventName = "EnteringRetransmitMode",
            Level = LogLevel.Trace,
            Message = "SCTP sender entering retransmit mode.")]
        public static partial void LogEnteringRetransmitMode(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpResendingMissingDataChunk2",
            Level = LogLevel.Trace,
            Message = "SCTP resending missing data chunk for TSN {TSN}, data length {UserDataLength}, flags {ChunkFlags:X2}, send count {SendCount}.")]
        public static partial void LogSctpResendingMissingDataChunk2(
            this ILogger logger,
            uint tSN,
            int userDataLength,
            byte chunkFlags,
            int sendCount);

        [LoggerMessage(
            EventId = 0,
            EventName = "SlowStartIncreased",
            Level = LogLevel.Trace,
            Message = "SCTP sender congestion window in slow-start increased from {OldCongestionWindow} to {NewCongestionWindow}.")]
        public static partial void LogSlowStartIncreased(
            this ILogger logger,
            uint oldCongestionWindow,
            uint newCongestionWindow);

        [LoggerMessage(
            EventId = 0,
            EventName = "CongestionAvoidanceIncreased",
            Level = LogLevel.Trace,
            Message = "SCTP sender congestion window in congestion avoidance increased from {OldCongestionWindow} to {NewCongestionWindow}.")]
        public static partial void LogCongestionAvoidanceIncreased(
            this ILogger logger,
            uint oldCongestionWindow,
            uint newCongestionWindow);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpCreatingNewAssociation",
            Level = LogLevel.Debug,
            Message = "SCTP creating new association for {RemoteEndPoint}.")]
        public static partial void LogSctpCreatingNewAssociation(
            this ILogger logger,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpFailedToAddNewAssociation",
            Level = LogLevel.Error,
            Message = "SCTP failed to add new association to dictionary.")]
        public static partial void LogSctpFailedToAddNewAssociation(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpTransportFailedToAddAssociation",
            Level = LogLevel.Warning,
            Message = "SCTP transport failed to add association.")]
        public static partial void LogSctpTransportFailedToAddAssociation(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpCookieEchoInvalidHMAC",
            Level = LogLevel.Warning,
            Message = "SCTP COOKIE ECHO chunk had an invalid HMAC, calculated {CalculatedHMAC}, cookie {CookieHMAC}.")]
        public static partial void LogSctpCookieEchoInvalidHMAC(
            this ILogger logger,
            string calculatedHMAC,
            string cookieHMAC);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpCookieEchoStale",
            Level = LogLevel.Warning,
            Message = "SCTP COOKIE ECHO chunk was stale, created at {CreatedAt}, now {Now}, lifetime {Lifetime}s.")]
        public static partial void LogSctpCookieEchoStale(
            this ILogger logger,
            string createdAt,
            string now,
            int lifetime);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpDataReceiverOldChunk",
            Level = LogLevel.Warning,
            Message = "SCTP data receiver received an old data chunk with TSN {TSN} when the expected TSN was {ExpectedTSN}, ignoring.")]
        public static partial void LogSctpDataReceiverOldChunk(
            this ILogger logger,
            uint tsn,
            uint expectedTSN);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpDataReceiverChunkTooDistant",
            Level = LogLevel.Warning,
            Message = "SCTP data receiver received a data chunk with a TSN {TSN} when the expected TSN was {ExpectedTSN} and a window size of {WindowSize}, ignoring.")]
        public static partial void LogSctpDataReceiverChunkTooDistant(
            this ILogger logger,
            uint tsn,
            uint expectedTSN,
            ushort windowSize);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpDataReceiverInitialChunkTooDistant",
            Level = LogLevel.Warning,
            Message = "SCTP data receiver received a data chunk with a TSN {TSN} when the initial TSN was {InitialTSN} and a window size of {WindowSize}, ignoring.")]
        public static partial void LogSctpDataReceiverInitialChunkTooDistant(
            this ILogger logger,
            uint tsn,
            uint initialTSN,
            ushort windowSize);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpStreamBufferCapacity",
            Level = LogLevel.Warning,
            Message = "Stream {StreamID} is at buffer capacity. Rejected out-of-order data chunk TSN {TSN}.")]
        public static partial void LogSctpStreamBufferCapacity(
            this ILogger logger,
            ushort streamID,
            uint tsn);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpChunkBufferTooShort",
            Level = LogLevel.Warning,
            Message = "The SCTP chunk buffer was too short. Required {BytesRequired} bytes but only {BytesAvailable} available.")]
        public static partial void LogSctpChunkBufferTooShort(
            this ILogger logger,
            int bytesRequired,
            int bytesAvailable);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpImplementParsingLogic",
            Level = LogLevel.Debug,
            Message = "TODO: Implement parsing logic for well known chunk type {ChunkType}.")]
        public static partial void LogSctpImplementParsingLogic(
            this ILogger logger,
            SctpChunkType chunkType);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpPacketDroppedInvalidChecksum",
            Level = LogLevel.Warning,
            Message = "SCTP packet from UDP {RemoteEndPoint} dropped due to invalid checksum.")]
        public static partial void LogSctpPacketDroppedInvalidChecksum(
            this ILogger logger,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpErrorAcquiringHandshakeCookie",
            Level = LogLevel.Warning,
            Message = "SCTP error acquiring handshake cookie from COOKIE ECHO chunk.")]
        public static partial void LogSctpErrorAcquiringHandshakeCookie(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpTransportEncapsulationReceiverClosed",
            Level = LogLevel.Information,
            Message = "SCTP transport encapsulation receiver closed with reason: {Reason}.")]
        public static partial void LogSctpTransportEncapsulationReceiverClosed(
            this ILogger logger,
            string reason);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpTransportOnEncapsulationSocketPacketReceivedException",
            Level = LogLevel.Error,
            Message = "Exception SctpTransport.OnEncapsulationSocketPacketReceived. {ErrorMessage}")]
        public static partial void LogSctpTransportOnEncapsulationSocketPacketReceivedException(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpCookie",
            Level = LogLevel.Debug,
            Message = "Cookie: {Cookie}",
            SkipEnabledCheck = true)]
        private static partial void LogSctpCookieUnchecked(
            this ILogger logger,
            string cookie);

        public static void LogSctpCookie(

            this ILogger logger,
            SctpTransportCookie cookie)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                LogSctpCookieUnchecked(logger, cookie.ToJson());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSenderWaitPeriod",
            Level = LogLevel.Trace,
            Message = "SCTP sender wait period {Wait}ms, arwnd {ReceiverWindow}, cwnd {CongestionWindow} " +
                      "outstanding bytes {OutstandingBytes}, send queue {SendQueueCount}, missing {MissingChunksCount} " +
                      "unconfirmed {UnconfirmedChunksCount}.")]
        public static partial void LogSctpSenderWaitPeriod(
            this ILogger logger,
            int wait,
            uint receiverWindow,
            uint congestionWindow,
            uint outstandingBytes,
            int sendQueueCount,
            int missingChunksCount,
            int unconfirmedChunksCount);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSenderBurstSize",
            Level = LogLevel.Trace,
            Message = "SCTP sender burst size {BurstSize}, in retransmit mode {InRetransmitMode}, cwnd {CongestionWindow}, arwnd {ReceiverWindow}.")]
        public static partial void LogSctpSenderBurstSize(
            this ILogger logger,
            int burstSize,
            bool inRetransmitMode,
            uint congestionWindow,
            uint receiverWindow);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSenderRetransmitMode",
            Level = LogLevel.Debug,
            Message = "SCTP sender wait period {Wait}ms, arwnd {ReceiverWindow}, cwnd {CongestionWindow} " +
                      "outstanding bytes {OutstandingBytes}, send queue {SendQueueCount}, missing {MissingChunksCount} " +
                      "unconfirmed {UnconfirmedChunksCount}.")]
        public static partial void LogSctpSenderRetransmitMode(
            this ILogger logger,
            int wait,
            uint receiverWindow,
            uint congestionWindow,
            uint outstandingBytes,
            int sendQueueCount,
            int missingChunksCount,
            int unconfirmedChunksCount);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpAssociationDataSendThreadStarted",
            Level = LogLevel.Debug,
            Message = "SCTP association data send thread started for association {AssociationID}.")]
        public static partial void LogSctpDataSendThreadStarted(
            this ILogger logger,
            string associationID);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpAssociationDataSendThreadStopped",
            Level = LogLevel.Debug,
            Message = "SCTP association data send thread stopped for association {AssociationID}.")]
        public static partial void LogSctpDataSendThreadStopped(
            this ILogger logger,
            string associationID);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSackGapReportStartTooDistant",
            Level = LogLevel.Warning,
            Message = "SCTP SACK gap report had a start TSN of {GoodTsnStart} too distant from last good TSN {LastAckTsn}, ignoring rest of SACK.")]
        public static partial void LogSctpSackGapReportStartTooDistant(
            this ILogger logger,
            uint goodTsnStart,
            uint lastAckTsn);
            
        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSackGapReportStartBehind",
            Level = LogLevel.Warning,
            Message = "SCTP SACK gap report had a start TSN of {GoodTsnStart} behind last good TSN {LastAckTsn}, ignoring rest of SACK.")]
        public static partial void LogSctpSackGapReportStartBehind(
            this ILogger logger,
            uint goodTsnStart,
            uint lastAckTsn);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpNoMatchingUnconfirmedChunk",
            Level = LogLevel.Warning,
            Message = "SCTP SACK gap report reported missing TSN of {MissingTSN} but no matching unconfirmed chunk available.")]
        public static partial void LogSctpNoMatchingUnconfirmedChunk(
            this ILogger logger,
            uint missingTSN);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpWindowSizeSet",
            Level = LogLevel.Debug,
            Message = "SCTP windows size for data receiver set at {WindowSize}.")]
        public static partial void LogSctpWindowSizeSet(
            this ILogger logger,
            int windowSize);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpTransportClosed",
            Level = LogLevel.Information,
            Message = "SCTP transport encapsulation receiver closed with reason: {reason}.")]
        public static partial void LogSctpTransportClosed(
            this ILogger logger,
            string reason);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpAssociationCannotInitialise",
            Level = LogLevel.Warning,
            Message = "SCTP association cannot be initialised in state {state}.")]
        public static partial void LogSctpAssociationCannotInitialise(
            this ILogger logger,
            SctpAssociationState state);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpAssociationCannotInitialiseAfterAbortOrShutdown",
            Level = LogLevel.Warning,
            Message = "SCTP association cannot be initialised after an abort or shutdown.")]
        public static partial void LogSctpAssociationCannotInitialiseAfterAbortOrShutdown(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpAssociationCannotSendDataInState",
            Level = LogLevel.Warning,
            Message = "SCTP send data is not allowed for an association in state {state}.")]
        public static partial void LogSctpAssociationCannotSendDataInState(
            this ILogger logger,
            SctpAssociationState state);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSendDataNotAllowedAfterAbort",
            Level = LogLevel.Warning,
            Message = "SCTP send data is not allowed on an aborted association.")]
        public static partial void LogSctpSendDataNotAllowedAfterAbort(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpSourcePortCannotBeUpdated",
            Level = LogLevel.Warning,
            Message = "SCTP source port cannot be updated when the association is in state {state}.")]
        public static partial void LogSctpSourcePortCannotBeUpdated(
            this ILogger logger,
            SctpAssociationState state);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpDestinationPortCannotBeUpdated",
            Level = LogLevel.Warning,
            Message = "SCTP destination port cannot be updated when the association is in state {state}.")]
        public static partial void LogSctpDestinationPortCannotBeUpdated(
            this ILogger logger,
            SctpAssociationState state);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpAssociationCookieInitialisationNotAllowed",
            Level = LogLevel.Warning,
            Message = "SCTP association cannot initialise with a cookie after an abort or shutdown.")]
        public static partial void LogSctpAssociationCookieInitialisationNotAllowed(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpAssociationTimedOutInitAck",
            Level = LogLevel.Warning,
            Message = "SCTP timed out waiting for INIT ACK chunk from remote peer.")]
        public static partial void LogSctpAssociationTimedOutInitAck(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpAssociationTimedOutCookieAck",
            Level = LogLevel.Warning,
            Message = "SCTP timed out waiting for COOKIE ACK chunk from remote peer.")]
        public static partial void LogSctpAssociationTimedOutCookieAck(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpAssociationNoRuleForChunk",
            Level = LogLevel.Warning,
            Message = "SCTP association no rule for {chunkType} in state of {state}.")]
        public static partial void LogSctpAssociationNoRuleForChunk(
            this ILogger logger,
            SctpChunkType chunkType,
            SctpAssociationState state);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpInitAckInWrongState",
            Level = LogLevel.Warning,
            Message = "SCTP association received INIT_ACK chunk in wrong state of {state}, ignoring.")]
        public static partial void LogSctpInitAckInWrongState(
            this ILogger logger,
            SctpAssociationState state);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpUnrecognisedChunkType",
            Level = LogLevel.Warning,
            Message = "SCTP unrecognised chunk type {chunkType} indicated no further chunks should be processed.")]
        public static partial void LogSctpUnrecognisedChunkType(
            this ILogger logger,
            byte chunkType);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpUnrecognisedParameter",
            Level = LogLevel.Warning,
            Message = "SCTP unrecognised parameter {ParameterType} for chunk type {ChunkType} indicated no further chunks should be processed.")]
        public static partial void LogSctpUnrecognisedParameter(
            this ILogger logger,
            ushort parameterType,
            SctpChunkType? chunkType);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpPacketReceivedException",
            Level = LogLevel.Error,
            Message = "Exception SctpTransport.OnEncapsulationSocketPacketReceived. {ErrorMessage}")]
        public static partial void LogSctpPacketReceivedException(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpDataReceiverDistantInitialTsn",
            Level = LogLevel.Warning,
            Message = "SCTP data receiver received a data chunk with a TSN {TSN} when the initial TSN was {InitialTSN} and a window size of {WindowSize}, ignoring.")]
        public static partial void LogSctpDataReceiverDistantInitialTsn(
            this ILogger logger,
            uint tsn,
            uint initialTsn,
            ushort windowSize);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpDataReceiverDistantLastInOrderTsn",
            Level = LogLevel.Warning,
            Message = "SCTP data receiver received a data chunk with a TSN {TSN} when the expected TSN was {ExpectedTSN} and a window size of {WindowSize}, ignoring.")]
        public static partial void LogSctpDataReceiverDistantLastInOrderTsn(
            this ILogger logger,
            uint tsn,
            uint expectedTsn,
            ushort windowSize);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpDataReceiverOldChunkTsn",
            Level = LogLevel.Warning,
            Message = "SCTP data receiver received an old data chunk with TSN {TSN} when the expected TSN was {ExpectedTSN}, ignoring.")]
        public static partial void LogSctpDataReceiverOldChunkTsn(
            this ILogger logger,
            uint tsn,
            uint expectedTsn);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpTransportAssociationFailed",
            Level = LogLevel.Warning,
            Message = "SCTP transport failed to add association.")]
        public static partial void LogSctpTransportAssociationFailed(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpStreamBufferAtCapacity",
            Level = LogLevel.Warning,
            Message = "Stream {streamId} is at buffer capacity. Rejected out-of-order data chunk TSN {tsn}.")]
        public static partial void LogSctpStreamBufferAtCapacity(
            this ILogger logger,
            ushort streamId,
            uint tsn);
    }
}
