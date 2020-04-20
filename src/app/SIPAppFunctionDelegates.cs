// ============================================================================
// FileName: SIPFunctionDelegates.cs
//
// Description:
// A list of function delegates that are used by the SIP Server Agents.
//
// Author(s):
// Aaron Clauson
//
// History:
// 14 Nov 2008	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Net;

namespace SIPSorcery.SIP.App
{
    public delegate bool SIPMonitorAuthenticationDelegate(string username, string password);    // Delegate to authenticate connections to the SIP Monitor Server.
    public delegate void DialogueBridgeCreatedDelegate(SIPDialogue clientDialogue, SIPDialogue forwardedDialogue, string owner);
    public delegate void DialogueBridgeClosedDelegate(string dialogueId, string owner);
    public delegate void IPAddressChangedDelegate(IPAddress newIPAddress);
    public delegate void QueueNewCallDelegate(ISIPServerUserAgent uas);
    public delegate void BlindTransferDelegate(SIPDialogue deadDialogue, SIPDialogue orphanedDialogue, SIPDialogue answeredDialogue);

    // SIP User Agent Delegates.
    public delegate void SIPCallResponseDelegate(ISIPClientUserAgent uac, SIPResponse sipResponse);
    public delegate void SIPCallFailedDelegate(ISIPClientUserAgent uac, string errorMessage, SIPResponse sipResponse);
    public delegate void SIPUASStateChangedDelegate(ISIPServerUserAgent uas, SIPResponseStatusCodesEnum statusCode, string reasonPhrase);

    // Get SIP account(s) from external sources delegate.
    public delegate SIPAccount GetSIPAccountDelegate(string username, string domain);
    public delegate List<SIPAccount> GetSIPAccountsForUserDelegate(string username, string domain, int offset, int limit);
    public delegate List<SIPAccount> GetSIPAccountsForOwnerDelegate(string owner, int offset, int limit);

    // Authorisation delegates.
    public delegate SIPRequestAuthenticationResult SIPAuthenticateRequestDelegate(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest, SIPAccount sipAccount, SIPMonitorLogDelegate log);

    // SIP Presence delegates.
    public delegate int SIPRegistrarBindingsCountDelegate(Guid sipAccountID);
    public delegate object SIPAssetGetPropertyByIdDelegate<T>(Guid id, string propertyName);

    // Diagnostic/logging delegates.
    public delegate void SIPMonitorLogDelegate(SIPMonitorEvent monitorEvent);
    public delegate void SIPMonitorMachineLogDelegate(SIPMonitorMachineEvent machineEvent);
}
