using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.CRM.ThirtySevenSignals;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public class DialPlanCRMFacade
    {
        private static ILog logger = AppState.logger;

        public DialPlanCRMFacade()
        { }

        public CRMHeaders LookupHighriseContact(string url, string authToken, SIPFromHeader from)
        {
            try
            {
                PersonRequest req = new PersonRequest(url, authToken);
                People people = req.GetByName(from.FromName);

                if (people != null && people.PersonList != null && people.PersonList.Count > 0)
                {
                    Person person = people.PersonList[0];
                    return new CRMHeaders(person.FirstName + " " + person.LastName, null, person.AvatarURL);
                }
                else
                {
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception LookupHighriseContact. " + excp.Message);
                return null;
            }
        }

        public CRMHeaders LookupHighriseContact(string url, string authToken, string name)
        {
            try
            {
                PersonRequest req = new PersonRequest(url, authToken);
                People people = req.GetByName(name);

                if (people != null && people.PersonList != null && people.PersonList.Count > 0)
                {
                    Person person = people.PersonList[0];
                    return new CRMHeaders(person.FirstName + " " + person.LastName, null, person.AvatarURL);
                }
                else
                {
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception LookupHighriseContact. " + excp.Message);
                return null;
            }
        }
    }
}
