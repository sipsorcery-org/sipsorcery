// SharpSRTP
// Copyright (C) 2025 Lukas Volf
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
// SOFTWARE.

using Org.BouncyCastle.Tls;
using System;
#if !NET6_0_OR_GREATER
using System.Globalization;
#endif
using System.Net;
using System.Net.Sockets;

namespace SIPSorcery.Net.SharpSRTP.UDP
{
    public class UdpDatagramTransport : DatagramTransport
    {
        public const int MTU = 1472; // 1500 - 20 (IP) - 8 (UDP)
        private readonly int _mtu = MTU;

        private UdpClient _udpClient = null;
        private IPEndPoint _remote;
        public IPEndPoint RemoteEndPoint => _remote;

        public UdpDatagramTransport(string localEndpoint, string remoteEndpoint, int mtu = MTU)
        {
            this._mtu = mtu;
            if (string.IsNullOrEmpty(localEndpoint))
            {
                this._udpClient = new UdpClient();
            }
            else
            {
#if NET6_0_OR_GREATER
                var endpoint = IPEndPoint.Parse(localEndpoint);
#else
                var endpoint = IPEndPointExtensions.Parse(localEndpoint);
#endif
                this._udpClient = new UdpClient(endpoint);
            }

            if (!string.IsNullOrEmpty(remoteEndpoint))
            {
#if NET6_0_OR_GREATER
                _remote = IPEndPoint.Parse(remoteEndpoint);
#else
                _remote = IPEndPointExtensions.Parse(remoteEndpoint);
#endif
            }
        }

        public virtual int GetReceiveLimit()
        {
            return _mtu;
        }

        public virtual int GetSendLimit()
        {
            return _mtu;
        }

        public virtual int Receive(byte[] buf, int off, int len, int waitMillis)
        {
            return Receive(buf.AsSpan(off, len), waitMillis);
        }

        public virtual int Receive(Span<byte> buffer, int waitMillis)
        {
            _remote = new IPEndPoint(IPAddress.Any, 0);
            byte[] receivedBytes = _udpClient.Receive(ref _remote);
            receivedBytes.AsSpan().CopyTo(buffer);
            return receivedBytes.Length;
        }

        public virtual void Send(byte[] buf, int off, int len)
        {
            Send(buf.AsSpan(off, len));
        }

        public virtual void Send(ReadOnlySpan<byte> buffer)
        {
#if NET6_0_OR_GREATER
            _udpClient.Send(buffer, _remote);
#else
            _udpClient.Send(buffer.ToArray(), buffer.Length, _remote);
#endif
        }

        public virtual void Close()
        {
            _udpClient.Close();
        }
    }

#if !NET6_0_OR_GREATER

    public static class IPEndPointExtensions
    {
        public static bool TryParse(string s, out IPEndPoint result)
        {
            int addressLength = s.Length;  // If there's no port then send the entire string to the address parser
            int lastColonPos = s.LastIndexOf(':');

            // Look to see if this is an IPv6 address with a port.
            if (lastColonPos > 0)
            {
                if (s[lastColonPos - 1] == ']')
                {
                    addressLength = lastColonPos;
                }
                // Look to see if this is IPv4 with a port (IPv6 will have another colon)
                else if (s.Substring(0, lastColonPos).LastIndexOf(':') == -1)
                {
                    addressLength = lastColonPos;
                }
            }

            if (IPAddress.TryParse(s.Substring(0, addressLength), out IPAddress address))
            {
                uint port = 0;

                if (addressLength == s.Length ||
                    (uint.TryParse(s.Substring(addressLength + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) && port <= IPEndPoint.MaxPort))

                {
                    result = new IPEndPoint(address, (int)port);

                    return true;
                }
            }

            result = null;

            return false;
        }

        public static IPEndPoint Parse(string s)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            if (TryParse(s, out IPEndPoint result))
            {
                return result;
            }

            throw new FormatException(@"An invalid IPEndPoint was specified.");
        }
    }
#endif
}
