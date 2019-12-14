//-----------------------------------------------------------------------------
// Filename: IVoIPClient.cs
//
// Description: An interface that holds the methods required by clients capable 
// of making VoIP calls. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Mar 2012	Aaron Clauson	Created, Hobart, Australia.
// 04 Dec 2019  Aaron Clauson   Added Transfer and Hold methods.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Threading.Tasks;

namespace SIPSorcery.SoftPhone
{
    public interface IVoIPClient
    {
        void Call(string destination);
        void Cancel();
        void Answer();
        void Reject();
        void Redirect(string destination);
        void PutOnHold();
        void TakeOffHold();
        Task<bool> Transfer(string destination);
        void Hangup();
        void Shutdown();
    }
}
