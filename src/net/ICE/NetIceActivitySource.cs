using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

internal static class NetIceActivitySource
{
    private static readonly ActivitySource _activitySource = new("sipsorcery.net.ice");

    public static Activity? StartStunMessageSentActivity(
        STUNMessage stunMessage,
        IPEndPoint dstEndPoint,
        STUNUri? uri = null,
        ReadOnlyMemory<byte> realm = default,
        ReadOnlyMemory<byte> nonce = default,
        bool isRelayed = false)
    {
        if (!_activitySource.HasListeners())
        {
            return null;
        }

        var transactionId = stunMessage.Header!.TransactionId;
        var messageTypeText = stunMessage.Header!.MessageType.ToStringFast();
        var transactionIdText = transactionId.HexStr();
        if (_activitySource.StartActivity($"Send {(isRelayed ? " relayed" : "")} STUN {messageTypeText} message {transactionIdText}", ActivityKind.Client) is { } activity)
        {
            activity
                .SetTag("sipsorcery.net.ice.stun.message.type", messageTypeText)
                .SetTag("sipsorcery.net.ice.stun.transaction.id", transactionIdText)
                .SetTag("sipsorcery.net.ice.stun.uri", uri)
                .SetTag("sipsorcery.net.ice.dst.endpoint", dstEndPoint)
                .SetTag("sipsorcery.net.ice.relayed", isRelayed.ToString())
                ;

            if (!realm.IsEmpty)
            {
                activity
                    .SetTag("sipsorcery.net.ice.stun.realm", Encoding.UTF8.GetString(realm.Span))
                    ;
            }

            if (!nonce.IsEmpty)
            {
                activity
                    .SetTag("sipsorcery.net.ice.stun.nonce", nonce.Span.HexStr())
                    ;
            }

            SetStunMessageCustomProperty(activity, stunMessage);

            return activity;
        }

        return null;
    }

    public static Activity? StartStunMessageReceivedActivity(STUNMessage stunMessage, IPEndPoint srcEndPoint, int localPort, bool isRelayed)
    {
        if (!_activitySource.HasListeners())
        {
            return null;
        }

        var transactionId = stunMessage.Header!.TransactionId;
        var messageTypeText = stunMessage.Header!.MessageType.ToStringFast();
        var transactionIdText = transactionId.HexStr();
        if (_activitySource.StartActivity($"Received{(isRelayed ? " relayed" : "")} STUN {messageTypeText} message {transactionIdText}", ActivityKind.Client) is { } activity)
        {
            activity
                .SetTag("sipsorcery.net.ice.stun.message.type", messageTypeText)
                .SetTag("sipsorcery.net.ice.stun.transaction.id", transactionIdText)
                .SetTag("sipsorcery.net.ice.src.endpoint", srcEndPoint)
                .SetTag("sipsorcery.net.ice.dst.port", localPort.ToString())
                .SetTag("sipsorcery.net.ice.relayed", isRelayed.ToString())
                ;

            SetStunMessageCustomProperty(activity, stunMessage);

            return activity;
        }

        return null;
    }

    public static Activity SetRelayEndpoint(this Activity activity, IPEndPoint relayEndPoint)
    {
        activity
            .SetTag("sipsorcery.net.ice.relay.endpoint", relayEndPoint)
            ;

        return activity;
    }

    private static void SetStunMessageCustomProperty(Activity activity, STUNMessage stunMessage)
    {
        activity.SetCustomProperty("StunMessage", stunMessage);
    }

    private static bool IsStunMessageActivity(Activity activity)
    {
        return activity.GetCustomProperty("StunMessage") is { };
    }
}
