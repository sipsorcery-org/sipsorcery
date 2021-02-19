//-----------------------------------------------------------------------------
// Filename: JanusModel.cs
//
// Description: Classes etc. to implement a minimal object model to interact
// with the Janus WebRTC Server's REST interface.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 04 Feb 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace demo
{
    public enum JanusOperationsEnum
    {
        unknown,
        ack,
        attach,
        create,
        destroy,
        error,
        @event,
        keepalive,
        message,
        success,
        trickle
    }

    public static class JanusPlugins
    {
        public const string ECHO_TEST = "janus.plugin.echotest";
    }

    public struct JanusData
    {
        public ulong id { get; set; }
    }

    /// <summary>
    /// Request to create a new session.
    /// </summary>
    public class JanusRequest
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public JanusOperationsEnum janus;
        public string transaction => DateTime.Now.Ticks.ToString();
    }

    public class AttachPluginRequest : JanusRequest
    {
        public string plugin { get; set; }

        public AttachPluginRequest(string pluginID)
        {
            janus = JanusOperationsEnum.attach;
            plugin = pluginID;
        }
    }

    public class Jsep
    {
        public string type { get; set; }
        public string sdp { get; set; }
        public bool trickle { get; set; }
    }

    /// <remarks>
    /// See https://janus.conf.meetecho.com/docs/echotest.html.
    /// </remarks>
    public class EchoTestBody
    {
        public bool audio { get; set; } = true;
        public bool video { get; set; } = true;
    }

    public class EchoTestRequest : JanusRequest
    {
        public Jsep jsep;
        public EchoTestBody body;

        public EchoTestRequest(string sdpType, string sdp)
        {
            janus = JanusOperationsEnum.message;
            jsep = new Jsep
            {
                type = sdpType,
                sdp = sdp
            };
            body = new EchoTestBody();
        }
    }

    public class JanusError
    {
        public int code { get; set; }
        public string reason { get; set; }
    }

    public class PluginResponse
    {
        public string plugin { get; set; }
    }

    public class JanusResponse
    {
        public string janus { get; set; }
        public ulong session_id { get; set; }
        public string transaction { get; set; }
        public ulong sender { get; set; }
        public string hint { get; set; }
        public JanusData data { get; set; }
        public JanusError error { get; set; }
        public PluginResponse plugindata { get; set; }
        public Jsep jsep { get; set; }

        [JsonIgnore]
        public JanusOperationsEnum JanusOp
        {
            get
            {
                if (Enum.TryParse<JanusOperationsEnum>(janus, out var op))
                {
                    return op;
                }
                else
                {
                    return JanusOperationsEnum.unknown;
                }
            }
            set
            {
                janus = value.ToString();
            }
        }
    }

    /// <summary>
    /// Response for /info.
    /// TODO: Numerous additional properties are available from the Janus info response.
    /// </summary>
    public class ServerInfo
    {
        public string name { get; set; }
        public int version { get; set; }
    }
}
