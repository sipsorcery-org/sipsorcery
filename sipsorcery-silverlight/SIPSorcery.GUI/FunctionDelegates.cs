using System;
using System.Collections.Generic;
using SIPSorcery.Entities;

namespace SIPSorcery
{
    public delegate void ActivityMessageDelegate(MessageLevelsEnum level, string message);
    public delegate void ActivityProgressDelegate(double? progress);
    public delegate void LoginDelegate(string username, string password);
    public delegate void CreateCustomerDelegate(Customer customer);
    public delegate void LogoutDelegate(bool sendServerLogout);

    public delegate void SIPAccountUpdateDelegate(SIPAccount sipAccount);
    public delegate void SIPAccountAddedDelegate(SIPAccount sipAccount);
    public delegate void DialPlanUpdateDelegate(SIPDialPlan dialPlan);
    public delegate void DialPlanAddedDelegate(SIPDialPlan dialPlan);
    public delegate void SIPProviderUpdateDelegate(SIPProvider sipProvider);
    public delegate void SIPProviderAddedDelegate(SIPProvider sipProvider);

    public delegate List<string> GetDialPlanNamesDelegate(string owner);
    public delegate List<string> GetSIPDomainsDelegate(string owner);

    public delegate void SIPMonitorMachineEventReceivedDelegate(SIPSorcery.SIP.App.SIPMonitorMachineEvent machineEvent);
    public delegate void ServiceStatusChangeDelegate(ServiceConnectionStatesEnum serviceStatus, string statusMessage);
}
