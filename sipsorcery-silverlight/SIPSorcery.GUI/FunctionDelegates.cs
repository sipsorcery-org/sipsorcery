using System;
using System.Collections.Generic;
using SIPSorcery.CRM;
using SIPSorcery.SIP.App;

namespace SIPSorcery
{
    public delegate void ActivityMessageDelegate(MessageLevelsEnum level, string message);
    public delegate void ActivityProgressDelegate(double? progress);
    public delegate void LoginDelegate(string username, string password);
    public delegate void CreateCustomerDelegate(Customer customer);
    public delegate void LogoutDelegate(bool sendServerLogout);
    public delegate void KeyDownDelegate();
    public delegate void ClickedDelegate();

    public delegate void SIPAccountUpdateDelegate(SIPAccount sipAccount);
    public delegate void SIPAccountAddedDelegate(SIPAccount sipAccount);
    public delegate void DialPlanUpdateDelegate(SIPDialPlan dialPlan);
    public delegate void DialPlanAddedDelegate(SIPDialPlan dialPlan);
    public delegate void SIPProviderUpdateDelegate(SIPProvider sipProvider);
    public delegate void SIPProviderAddedDelegate(SIPProvider sipProvider);

    public delegate List<string> GetDialPlanNamesDelegate(string owner);
    public delegate List<string> GetSIPDomainsDelegate(string owner);
}
