// ============================================================================
// FileName: SIPEvent.cs
//
// Description: 
//
// Author(s):
// Aaron Clauson
//
// History:
// ??	Aaron Clauson	Created (aaron@sipsorcery.com), SIPSorcery Ltd, London, UK (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;

namespace SIPSorcery.SIP
{
    public class SIPEvent
    {
        public SIPEvent()
        { }

        public virtual void Load(string eventStr)
        {
            throw new NotImplementedException();
        }

        public virtual string ToXMLText()
        {
            throw new NotImplementedException();
        }
    }
}
