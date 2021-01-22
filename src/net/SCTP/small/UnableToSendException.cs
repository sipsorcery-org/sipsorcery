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
using System.Runtime.Serialization;

namespace SIPSorcery.Net.Sctp
{
    [Serializable]
    internal class UnableToSendException : Exception
    {
        public Association.State State { get; set; } 
        public UnableToSendException()
        {
        }

        public UnableToSendException(string message) : base(message)
        {
        }

        public UnableToSendException(string message, Association.State state) : base(message)
        {
            this.State = state;
        }

        public UnableToSendException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected UnableToSendException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}