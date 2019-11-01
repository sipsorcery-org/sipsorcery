//-----------------------------------------------------------------------------
// Filename: IVoIPClient.cs
//
// Description: An interface that holds the methods required by clients capable of making VoIP calls. 
// 
// History:
// 28 Mar 2012	Aaron Clauson	Created, (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
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
