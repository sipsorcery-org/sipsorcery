//-----------------------------------------------------------------------------
// Filename: IceConnectionStatesEnum.cs
//
// Description: An enumeration of the different ICE connection states a WebRTC peer can have.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 03 Mar 2016	Aaron Clauson	Created, Hobart, Australia.
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
