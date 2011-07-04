//-----------------------------------------------------------------------------
// Filename: WrappedStream.cs
//
// Description: A class that wraps a stream so that an XmlWriter and XmlReader can be used on top
// of an XMPP network stream. The wrapper is needed because the .Net XmlWriter does not like the root
// element being left open when it gets closed. XMPP requires that the root element is not closed when
// transitioning the stream to TLS.
// 
// History:
// 13 Dec 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), Hobart, Tasmania, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace SIPSorcery.XMPP
{
    public class WrappedStream : Stream
    {
        private Stream m_wrappedStream;
        private StreamWriter m_traceStream;
        private Boolean m_blockIO;

        public WrappedStream(Stream stream, StreamWriter traceStream)
        {
            m_wrappedStream = stream;
            m_traceStream = traceStream;
        }

        public override bool CanRead
        {
            get { return m_wrappedStream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return m_wrappedStream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return m_wrappedStream.CanWrite; }
        }

        public override void Flush()
        {
            m_wrappedStream.Flush();
        }

        public override long Length
        {
            get { return m_wrappedStream.Length; }
        }

        public override long Position
        {
            get
            {
                return m_wrappedStream.Position;
            }
            set
            {
                m_wrappedStream.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!m_blockIO)
            {
                int bytesRead = m_wrappedStream.Read(buffer, offset, count);

                if (m_traceStream != null)
                {
                    m_traceStream.WriteLine();
                    m_traceStream.WriteLine("Receive=>");
                    m_traceStream.Flush();
                    m_traceStream.BaseStream.Write(buffer, offset, bytesRead);
                    m_traceStream.Flush();
                }

                Console.WriteLine("Receive=> " + Encoding.UTF8.GetString(buffer, offset, bytesRead));

                return bytesRead;
            }
            else
            {
                return 0;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return m_wrappedStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            m_wrappedStream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!m_blockIO)
            {
                //Console.WriteLine("Send => " + Encoding.UTF8.GetString(buffer, offset, count));

                if (m_traceStream != null)
                {
                    m_traceStream.WriteLine();
                    m_traceStream.WriteLine("Send=>");
                    m_traceStream.Flush();
                    m_traceStream.BaseStream.Write(buffer, offset, count);
                    m_traceStream.Flush();
                }

                m_wrappedStream.Write(buffer, offset, count);
            }
        }

        public void BlockIO()
        {
            m_blockIO = true;
        }

        public override void Close()
        {
            
        }
    }
}
