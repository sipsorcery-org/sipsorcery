using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.CRM;

namespace SIPSorcery.SIP.App
{
    public delegate CustomerSession AuthenticateWebServiceDelegate(string username, string password);
    public delegate CustomerSession AuthenticateTokenDelegate(string token);
    public delegate void ExpireTokenDelegate(string token);
}
