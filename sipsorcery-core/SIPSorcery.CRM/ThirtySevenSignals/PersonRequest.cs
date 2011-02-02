using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using SIPSorcery.Sys;

namespace SIPSorcery.CRM.ThirtySevenSignals
{
    public class PersonRequest
    {
        private const int MAX_HTTP_REQUEST_TIMEOUT = 10;

        private string m_url;
        private string m_authToken;

        public PersonRequest(string url, string authToken)
        {
            m_url = url;
            m_authToken = authToken;
        }
        
        public Person GetPerson(int id)
        {
            string personURL = m_url + "/people/" + id + ".xml";
            return GetPerson(personURL);
        }

        public People GetByPhoneNumber(string phoneNumber)
        {
            string personURL = m_url + "/people/search.xml?" + Uri.EscapeDataString("criteria[phone]=" + phoneNumber);
            return GetPeople(personURL);
        }

        public People GetByName(string name)
        {
            string personURL = m_url + "/people/search.xml?" + Uri.EscapeDataString("term=" + name);
            return GetPeople(personURL);
        }

        private Person GetPerson(string url)
        {
            string response = GetResponse(url);

            if (!response.IsNullOrBlank())
            {
                Person person = null;

                using (TextReader xmlReader = new StringReader(response))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(Person));
                    person = (Person)serializer.Deserialize(xmlReader);
                }

                person.SetAvatarURL(m_url);
                return person;
            }
            else
            {
                return null;
            }
        }

        private People GetPeople(string url)
        {
            string response = GetResponse(url);

            if (!response.IsNullOrBlank())
            {
                People people = null;

                using (TextReader xmlReader = new StringReader(response))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(People));
                    people = (People)serializer.Deserialize(xmlReader);
                }

                if (people != null && people.PersonList.Count > 0)
                {
                    foreach (Person person in people.PersonList)
                    {
                        person.SetAvatarURL(m_url);
                    }
                }

                return people;
            }
            else
            {
                return null;
            }
        }

        private string GetResponse(string url)
        {
            HttpWebRequest personRequest = (HttpWebRequest)WebRequest.Create(url);
            personRequest.AllowAutoRedirect = true;
            personRequest.Timeout = MAX_HTTP_REQUEST_TIMEOUT * 1000;
            personRequest.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(m_authToken)));

            HttpWebResponse response = (HttpWebResponse)personRequest.GetResponse();
            if (response.StatusCode != HttpStatusCode.OK)
            {
                response.Close();
                throw new ApplicationException("Person request to " + m_url + " failed with response " + response.StatusCode + ".");
            }

            StreamReader reader = new StreamReader(response.GetResponseStream());
            string responseStr = reader.ReadToEnd();
            response.Close();

            if (responseStr != null)
            {
                return Regex.Replace(responseStr, "nil=\"true\"", "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\"");
            }
            else
            {
                return null;
            }
        }
    }
}
