//-----------------------------------------------------------------------------
// Filename: IceConnectionStatesEnum.cs
//
// Description: An enumeration of the different ICE connection states a WebRTC peer can have.
//
// Author(s):
// Aaron Clauson
// 
// History:
// 03 Mar 2016	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.Net
{
    public enum IceConnectionStatesEnum
    {
        None = 0,
        Gathering = 1,
        GatheringComplete = 2,
        Connecting = 3,
        Connected = 4,
        Closed = 5
    }
}
