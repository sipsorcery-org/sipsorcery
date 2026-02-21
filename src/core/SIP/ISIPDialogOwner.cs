//-----------------------------------------------------------------------------
// Filename: ISIPDialogOwner.cs
//
// Description: Interface for SIP dialog owners that register with SIPTransport
// for direct dialog-based request dispatch. This enables RFC 3891 compliant
// 481 responses for Replaces INVITEs targeting non-existent dialogs.
//
// Author(s):
// Contributors
//
// History:
// 16 Feb 2026  Contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Threading.Tasks;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// Interface for components that own a SIP dialog and want to receive
    /// in-dialog requests dispatched directly by the transport layer.
    /// The transport uses <see cref="DialogCallID"/> for registry lookup and
    /// <see cref="DialogLocalTag"/>/<see cref="DialogRemoteTag"/> for RFC 3891
    /// Replaces matching (Call-ID + to-tag + from-tag).
    /// </summary>
    public interface ISIPDialogOwner
    {
        /// <summary>
        /// The Call-ID of the current dialog. Used as the registry key.
        /// </summary>
        string DialogCallID { get; }

        /// <summary>
        /// The local tag of the current dialog. Used for Replaces tag matching.
        /// </summary>
        string DialogLocalTag { get; }

        /// <summary>
        /// The remote tag of the current dialog. Used for Replaces tag matching.
        /// </summary>
        string DialogRemoteTag { get; }

        /// <summary>
        /// Called by the transport layer when an in-dialog request or a Replaces
        /// INVITE targeting this dialog's Call-ID is received.
        /// </summary>
        /// <param name="localSIPEndPoint">The local SIP end point the request was received on.</param>
        /// <param name="remoteEndPoint">The remote end point the request came from.</param>
        /// <param name="sipRequest">The SIP request.</param>
        Task OnDialogRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest);
    }
}
