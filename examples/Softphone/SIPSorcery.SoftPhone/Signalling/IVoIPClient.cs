//-----------------------------------------------------------------------------
// Filename: IVoIPClient.cs
//
// Description: An interface that holds the methods required by clients capable of making VoIP calls. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Mar 2012	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.SoftPhone
{
    public interface IVoIPClient
    {
        void Call(MediaManager mediaManager, string destination);
        void Cancel();
        void Answer(MediaManager mediaManager);
        void Reject();
        void Redirect(string destination);
        void Hangup();
        void Shutdown();
    }
}
