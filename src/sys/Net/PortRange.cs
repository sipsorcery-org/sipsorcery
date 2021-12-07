// ============================================================================
// FileName: PortRange.cs
//
// Description:
// Contains a helper class to manage sequential or random allocation of
// UDP-Port pairs for RTP-Streams.
//
// Author(s):
// Tobias Stähli
//
// History:
// 6 Dec 2021	Tobias Stähli	Created, Frauenfeld, Switzerland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Net;

namespace SIPSorcery.Sys
{
    /// <summary>
    /// Class to manage port allocation for Rtp Ports. Ports are always even, because
    /// due Rtp data and control ports are always with data on even port and 
    /// control (if any) on data_port + 1
    /// 
    /// There are two operation modes:
    ///  - The sequential mode which hands out all ports within the assigned range and 
    /// wraps around if the last port was assigned
    ///  - The shuffled mode: Ports are handed out evenly distributed within the assigned
    ///  port range.
    /// </summary>
    public class PortRange
    {
        private readonly Random m_random;
        private readonly bool m_shuffle;
        private readonly int m_startPort;
        private readonly int m_endPort;
        private int m_nextPort;

        /// <summary>
        /// Initializes a new PortRange.
        /// </summary>
        /// <param name="startPort">Inclusive, lowest port within this portrange. must be an even number</param>
        /// <param name="endPort">Inclusive, highest port within this portrange.</param>
        /// <param name="shuffle">optional, if set, the ports are assigned in a pseudorandom order.</param>
        /// <param name="randomSeed">optional, the seed for the pseudorandom order.</param>
        /// <exception cref="ArgumentException"></exception>
        public PortRange(int startPort, int endPort, bool shuffle = false, int? randomSeed = null)
        {
            if (startPort <= 0 || startPort > IPEndPoint.MaxPort)
            {
                throw new ArgumentException($"startPort must be greater than 0 and less than or euqal {IPEndPoint.MaxPort}");
            }
            if (endPort <= 0 || endPort > IPEndPoint.MaxPort)
            {
                throw new ArgumentException($"endPort must be greater than 0 and less than or euqal {IPEndPoint.MaxPort}");
            }
            if (endPort - startPort < 2)
            {
                throw new ArgumentException($"endPort({endPort}) - startPort({startPort}) must be at least 2, but is {endPort - startPort}");
            }
            if (startPort % 2 == 1)
            {
                throw new ArgumentException("startPort must be even");
            }
            if (endPort % 2 == 0)
            {
                endPort -= 1;// correct end-port to odd if even -> RtpPort are always handed out in pairs
            }
            m_startPort = startPort;
            m_endPort = endPort;
            m_shuffle = shuffle;
            if (shuffle)
            {
                m_random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
                m_nextPort = m_random.Next(m_startPort, m_endPort + 1) // The + 1 is needed to get an even distribution because Random.Next(start, end) is inclusive start but exclusive the end
                    & 0x0000_FFFE; // AND with IPEndPoint.MaxPort but last bit is set to zero to always have an even port
            }
            else
            {
                m_nextPort = startPort;
            }
        }

        /// <summary>
        /// Calculates the next port which should be tried.
        /// No guarantee is made, that the returned port can also be bound to; actual check is still needed.
        /// Caller of this method should try to bind to the socket and if not successful, try again for x times
        /// before giving up.
        /// 
        /// This method is thread-safe
        /// </summary>
        /// <returns>port from the portrange</returns>
        public virtual int GetNextPort()
        {
            lock (this)
            {
                var res = m_nextPort;
                if (m_shuffle)
                {
                    m_nextPort = m_random.Next(m_startPort, m_endPort + 1) // The + 1 is needed to get an even distribution because Random.Next(start, end) is inclusive start but exclusive the end
                        & 0x0000_FFFE; // AND with IPEndPoint.MaxPort but last bit is set to zero to always have an even port
                }
                else
                {
                    m_nextPort = m_nextPort + 2;
                    if (m_nextPort > m_endPort)
                    {
                        m_nextPort = m_startPort;
                    }
                }
                return res;
            }
        }
    }
}
