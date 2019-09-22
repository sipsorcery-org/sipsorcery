// ============================================================================
// FileName: Sugarhelper.cs
//
// Description:
// Helper class for web service calls to SugarCRM.
//
// Author(s):
// Aaron Clauson
//
// History:
// 29 Jun 2011  Aaron Clauson   Derived from http://www.sugarcrm.com/kb/system/web/soap-in-csharp/.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.ServiceModel;
using System.Text;

namespace SIPSorcery.CRM.SugarCRM
{
    public class SugarHelper
    {
        sugarsoapPortTypeClient sugarClient;
        string sessionId;
        string error;

        public string Error
        {
            get
            {
                return this.error;
            }
        }

        public SugarHelper()
        {
            //Create a new instance of the client proxy
            this.sugarClient = new sugarsoapPortTypeClient(new BasicHttpBinding(), new EndpointAddress("http://crm.sipsorcery.com/soap.php"));

            //Set the default value
            this.sessionId = String.Empty;
        }

        public bool Authenticate(string Username, string Password)
        {
            //Create an authentication object
            user_auth user = new user_auth();

            //Set the credentials
            user.user_name = Username;
            user.password = this.computeMD5String(Password);

            //Try to authenticate
            set_entry_result authentication_result = this.sugarClient.login(user, "");

            //Check for errors
            if (Convert.ToInt32(authentication_result.error.number) != 0)
            {
                //An error occured
                this.error = String.Concat(authentication_result.error.name, ": ",
                authentication_result.error.description);

                //Clear the existing sessionId
                this.sessionId = String.Empty;
            }
            else
            {
                //Set the sessionId
                this.sessionId = authentication_result.id;

                //Clear the existing error
                this.error = String.Empty;
            }

            //Return the boolean
            return (this.sessionId != String.Empty);
        }

        private string computeMD5String(string PlainText)
        {
            MD5 md5 = MD5.Create();
            byte[] inputBuffer = System.Text.Encoding.ASCII.GetBytes(PlainText);
            byte[] outputBuffer = md5.ComputeHash(inputBuffer);

            //Convert the byte[] to a hex-string
            StringBuilder builder = new StringBuilder(outputBuffer.Length);
            for (int i = 0; i < outputBuffer.Length; i++)
            {
                builder.Append(outputBuffer[i].ToString("X2"));
            }

            return builder.ToString();
        }

        public DataTable GetMeetings(string SessionId, sugarsoapPortTypeClient SugarSoap,
            string Query, string OrderBy, int Offset, int MaxResults, bool GetDeleted)
        {
            //Define the array
            string[] fields = new string[14];

            //Fill the array
            fields[0] = "id";
            fields[1] = "date_entered";
            fields[2] = "date_modified";
            fields[3] = "assigned_user";
            fields[4] = "modified_user";
            fields[5] = "created_by";
            fields[6] = "name";
            fields[7] = "location";
            fields[8] = "duration_hours";
            fields[9] = "duration_minutes";
            fields[10] = "date_start";
            fields[11] = "date_end";
            fields[12] = "status";
            fields[13] = "description";

            //Create a DataTable
            DataTable meetings = new DataTable("MEETINGS");

            //Define the Columns
            foreach (string field in fields)
            {
                meetings.Columns.Add(field);
            }

            //Get a list of entries
            get_entry_list_result entryList = this.sugarClient.get_entry_list(this.sessionId, "Meetings",
            Query, OrderBy, Offset, fields, MaxResults, Convert.ToInt32(GetDeleted));

            //Loop trough the entries
            foreach (entry_value entry in entryList.entry_list)
            {
                //Create a new DataRow
                DataRow meeting = meetings.NewRow();

                //Loop trough the columns
                foreach (name_value value in entry.name_value_list)
                {
                    meeting[value.name] = value.value;
                }

                //Add the DataRow to the DataTable
                meetings.Rows.Add(meeting);
            }

            return meetings;
        }

        public get_entry_list_result GetContacts(string SessionId, sugarsoapPortTypeClient SugarSoap,
            string Query, string OrderBy, int Offset, int MaxResults, bool GetDeleted)
        {
            string[] fields = new string[]{ "phone_work" };

            //Get a list of entries
            get_entry_list_result contactsList = this.sugarClient.get_entry_list(this.sessionId, "Contacts",
                Query, OrderBy, Offset, fields, MaxResults, Convert.ToInt32(GetDeleted));

            return contactsList;
        }
    }
}
