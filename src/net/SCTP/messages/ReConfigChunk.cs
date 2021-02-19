/*
 * Copyright 2017 pi.pe gmbh .
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */
// Modified by Andrés Leone Gámez


using System;
using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.Sys;

/**
 *
 * @author thp
 */
namespace SIPSorcery.Net.Sctp
{
    public class ReConfigChunk : Chunk
    {

        private static ILogger logger = Log.Logger;

        private long sentAt;
        private int retries;

        public ReConfigChunk(ChunkType type, byte flags, int length, ByteBuffer pkt)
            : base(type, flags, length, pkt)
        {
            //logger.LogDebug("ReConfig chunk" + this.ToString());
            if (_body.remaining() >= 4)
            {
                while (_body.hasRemaining())
                {
                    VariableParam v = this.readVariable();
                    _varList.Add(v);
                    //logger.LogDebug("\tParam :" + v.ToString());
                }
            }
        }

        public ReConfigChunk() : base(ChunkType.RE_CONFIG) { }

        protected override void putFixedParams(ByteBuffer ret)
        {
            //throw new UnsupportedOperationException("Not supported yet."); //To change body of generated methods, choose Tools | Templates.
        }

        public bool hasIncomingReset()
        {
            foreach (var v in _varList)
                if (typeof(IncomingSSNResetRequestParameter).IsAssignableFrom(v.GetType()))
                    return true;
            return false;
        }

        public IncomingSSNResetRequestParameter getIncomingReset()
        {
            foreach (var v in _varList)
                if (typeof(IncomingSSNResetRequestParameter).IsAssignableFrom(v.GetType()))
                    return (IncomingSSNResetRequestParameter)v;
            return null;
        }

        public bool hasOutgoingReset()
        {
            foreach (var v in _varList)
                if (typeof(OutgoingSSNResetRequestParameter).IsAssignableFrom(v.GetType()))
                    return true;
            return false;
        }

        private bool hasOutgoingAdd()
        {
            foreach (var v in _varList)
                if (typeof(AddOutgoingStreamsRequestParameter).IsAssignableFrom(v.GetType()))
                    return true;
            return false;
        }

        private bool hasResponse()
        {
            foreach (var v in _varList)
                if (typeof(ReconfigurationResponseParameter).IsAssignableFrom(v.GetType()))
                    return true;
            return false;
        }

        public OutgoingSSNResetRequestParameter getOutgoingReset()
        {
            foreach (var v in _varList)
                if (typeof(OutgoingSSNResetRequestParameter).IsAssignableFrom(v.GetType()))
                    return (OutgoingSSNResetRequestParameter)v;
            return null;
        }

        public bool hasParam()
        {
            return _varList.Count > 0;
        }

        /*
		   1.   Outgoing SSN Reset Request Parameter.

	   2.   Incoming SSN Reset Request Parameter.

	   3.   Outgoing SSN Reset Request Parameter, Incoming SSN Reset Request
			Parameter.

	   4.   SSN/TSN Reset Request Parameter.

	   5.   Add Outgoing Streams Request Parameter.

	   6.   Add Incoming Streams Request Parameter.

	   7.   Add Outgoing Streams Request Parameter, Add Incoming Streams
			Request Parameter.

	   8.   Re-configuration Response Parameter.

	   9.   Re-configuration Response Parameter, Outgoing SSN Reset Request
			Parameter.

	   10.  Re-configuration Response Parameter, Re-configuration Response
			Parameter.
		 */
        public override void validate()
        {
            if (_varList.Count < 1)
            {
                throw new Exception("[IllegalArgumentException] Too few params " + _varList.Count);
            }
            if (_varList.Count > 2)
            {
                throw new Exception("[IllegalArgumentException] Too many params " + _varList.Count);
            }
            // now check for invalid combos
            if ((_varList.Count == 2))
            {
                if (this.hasOutgoingReset())
                {
                    VariableParam remain = null;
                    foreach (var v in _varList)
                    {
                        if (!typeof(OutgoingSSNResetRequestParameter).IsAssignableFrom(v.GetType()))
                        {
                            remain = v;
                            break;
                        }
                    }
                    if (remain == null)
                    {
                        throw new Exception("[IllegalArgumentException] 2 OutgoingSSNResetRequestParameter in one Chunk not allowed ");
                    }
                    if (!typeof(IncomingSSNResetRequestParameter).IsAssignableFrom(remain.GetType()) //3
                        && !typeof(ReconfigurationResponseParameter).IsAssignableFrom(remain.GetType())) //9
                    {
                        throw new Exception("[IllegalArgumentException] OutgoingSSNResetRequestParameter and " + remain.GetType().Name + " in same Chunk not allowed ");
                    }
                }
                else if (this.hasOutgoingAdd())
                {
                    VariableParam remain = null;

                    foreach (var v in _varList)
                    {
                        if (!typeof(AddOutgoingStreamsRequestParameter).IsAssignableFrom(v.GetType()))
                        {
                            remain = v;
                            break;
                        }
                    }
                    if (remain == null)
                    {
                        throw new Exception("[IllegalArgumentException] 2 AddOutgoingStreamsRequestParameter in one Chunk not allowed ");
                    }
                    if (!typeof(AddIncomingStreamsRequestParameter).IsAssignableFrom(remain.GetType())) //7
                    {
                        throw new Exception("[IllegalArgumentException] OutgoingSSNResetRequestParameter and " + remain.GetType().Name + " in same Chunk not allowed ");
                    }
                }
                else if (this.hasResponse())
                {
                    VariableParam remain = null;

                    foreach (var v in _varList)
                    {
                        if (!typeof(ReconfigurationResponseParameter).IsAssignableFrom(v.GetType()))
                        {
                            remain = v;
                            break;
                        }
                    }

                    if (remain != null)
                    {
                        throw new Exception("[IllegalArgumentException] ReconfigurationResponseParameter and " + remain.GetType().Name + " in same Chunk not allowed ");
                    }
                }
            } // implicitly just one - which is ok 1,2,4,5,6,8
        }

        public void addParam(VariableParam rep)
        {
            //logger.LogDebug("adding " + rep + " to " + this);
            _varList.Add(rep);
            validate();
        }

        public bool sameAs(ReConfigChunk other)
        {
            // we ignore other var types for now....
            bool ret = false; // assume the negative.
            if (other != null)
            {
                // if there are 2 params and both match
                if ((this.hasIncomingReset() && other.hasIncomingReset())
                        && (this.hasOutgoingReset() && other.hasOutgoingReset()))
                {
                    ret = this.getIncomingReset().sameAs(other.getIncomingReset())
                            && this.getOutgoingReset().sameAs(other.getOutgoingReset());
                }
                else
                {
                    // there is only one (of these) params
                    // that has to match too
                    if (this.hasIncomingReset() && other.hasIncomingReset())
                    {
                        ret = this.getIncomingReset().sameAs(other.getIncomingReset());
                    }
                    if (this.hasOutgoingReset() && other.hasOutgoingReset())
                    {
                        ret = this.getOutgoingReset().sameAs(other.getOutgoingReset());
                    }
                }
            }
            return ret;
        }

        // stuff to manage outbound retries
        public long getSentTime()
        {
            return sentAt;
        }

        public void setSentTime(long now)
        {
            sentAt = now;
        }

        public int getAndIncrementRetryCount()
        {
            return retries++;
        }
    }
}
