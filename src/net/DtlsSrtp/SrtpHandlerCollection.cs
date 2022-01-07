//-----------------------------------------------------------------------------
// Filename: SrtpHandler.cs
//
// Description: This class represents a collection of SRTP handlers for SIP calls
//
// Author(s):
// Jean-Philippe Fournier
//
// History:
// 5 January 2022 : Jean-Philippe Fournier, created Montréal, QC, Canada
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace SIPSorcery.Net
{
    public class SrtpHandlerCollection
    {
        private readonly ConcurrentDictionary<SDPMediaTypesEnum, SrtpHandler> m_handlerCollection;

        public SrtpHandlerCollection()
        {
            m_handlerCollection = new ConcurrentDictionary<SDPMediaTypesEnum, SrtpHandler>();
        }

        public SrtpHandler GetOrCreateSrtpHandler(SDPMediaTypesEnum mediaType)
        {
            var found = m_handlerCollection.TryGetValue(mediaType, out var current);
            if (!found)
            {
                current = new SrtpHandler();
                m_handlerCollection[mediaType] = current;
            }
            return current;
        }
    }
}