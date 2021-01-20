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


using SCTP4CS.Utils;

/**
*
* @author tim
*/
namespace SIPSorcery.Net.Sctp
{
    public class Unknown : VariableParam
    {
        protected byte[] _data;
        protected int _type;
        protected string _name;

        public Unknown(int t, string n)
        {
            _type = t;
            _name = n;
        }

        public virtual void readBody(ByteBuffer b, int len)
        {
            _data = new byte[len];
            b.GetBytes(_data, len);
        }

        public virtual void writeBody(ByteBuffer b)
        {
            b.Put(_data);
        }

        public int getType()
        {
            return _type;
        }

        public string getName()
        {
            return _name;
        }
    }
}
