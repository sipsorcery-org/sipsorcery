using System;
using System.Data;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.CRM.SugarCRM;

namespace SIPSorcery.CRM.UnitTests
{
    [TestClass]
    [Ignore] // Need password.
    public class SugarCRMUnitTest
    {
        private string m_username = "admin";
        private string m_password = "";

        [TestMethod]
        public void GetMeetingsUnitTest()
        {
            //Create a new instance of the helper class
            SugarHelper helper = new SugarHelper();

            //Authenticate
            if (helper.Authenticate(m_username, m_password))
            {
                //Get the meetings
                DataTable meetings = helper.GetMeetings("", null, "", "", 0, 100, false);
                Console.WriteLine("Meetings count=" + meetings.Rows.Count + ".");
            }
        }

        [TestMethod]
        public void GetContactsUnitTest()
        {
            //Create a new instance of the helper class
            SugarHelper helper = new SugarHelper();

            //Authenticate
            if (helper.Authenticate(m_username, m_password))
            {
                //Get the meetings
                var contacts = helper.GetContacts("", null, "", "", 0, 100, false);
                Console.WriteLine("Contacts count=" + contacts.result_count + ".");
            }
        }

        [TestMethod]
        public void GetContactsForPhoneNumberUnitTest()
        {
            //Create a new instance of the helper class
            SugarHelper helper = new SugarHelper();

            //Authenticate
            if (helper.Authenticate(m_username, m_password))
            {
                //Get the meetings
                string query = "contacts.phone_work like '%99663311%'";
                //string query = String.Format("SELECT CONCAT(first_name,' ',last_name) AS name FROM contacts WHERE (phone_home LIKE '{0}' OR phone_mobile LIKE '{0}' OR phone_work LIKE '{0}' OR phone_other LIKE '{0}')", "99663311");
                //string query = String.Format("SELECT id FROM contacts WHERE (phone_home = '{0}' OR phone_mobile = '{0}' OR phone_work LIKE '{0}' OR phone_other = '{0}')", "99663311");
                Console.WriteLine(query);
                var contacts = helper.GetContacts("", null, query, "", 0, 100, false);
                Console.WriteLine("Contacts count=" + contacts.result_count + ".");

                foreach (var contact in contacts.entry_list)
                {
                    foreach (var field in contact.name_value_list)
                    {
                        //Console.WriteLine(contact.name_value_list[0] + " " + contact.name_value_list[1]);
                        Console.WriteLine(field.name + ": " + field.value);
                    }
                }
            }
        }
    }
}
