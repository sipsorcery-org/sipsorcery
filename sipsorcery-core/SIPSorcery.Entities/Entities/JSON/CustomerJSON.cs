//-----------------------------------------------------------------------------
// Filename: CustomerJSON.cs
//
// Description: A translation class to allow a Customer object to be serialised to and from JSON. The 
// Entity Framework derived Customer class cannot be used to a shortcoming in the serialisation
// mechanism to do with IsReference classes. This will probably be fixed in the future making this
// class redundant.
// 
// History:
// 12 Nov 2011	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2011 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery Pty. Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Entities
{
    [DataContractAttribute]
    public class CustomerJSON
    {
        [DataMember] public string City { get; set; }
        [DataMember] public string Country { get; set; }
        [DataMember] public string CustomerPassword { get; set; }
        [DataMember] public string EmailAddress { get; set; }
        [DataMember] public bool EmailAddressConfirmed { get; set; }
        [DataMember] public string Firstname { get; set; }
        [DataMember] public string ID { get; set; }
        [DataMember] public string Lastname { get; set; }
        [DataMember] public string Username { get; set; }
        [DataMember] public string SecurityAnswer { get; set; }
        [DataMember] public string SecurityQuestion { get; set; }
        [DataMember] public string ServiceLevel { get; set; }
        [DataMember] public string ServiceRenewalDate { get; set; }
        [DataMember] public string WebSite { get; set; }

        public CustomerJSON()
        { }

        public CustomerJSON(Customer customer)
        { 
            City = customer.City;
                Country = customer.Country;
                //CustomerPassword = customer.CustomerPassword;
                EmailAddress = customer.EmailAddress;
                EmailAddressConfirmed = customer.EmailAddressConfirmed;
                Firstname = customer.Firstname;
                ID = customer.ID;
                Lastname = customer.Lastname;
                Username = customer.Name;
                SecurityAnswer = customer.SecurityAnswer;
                SecurityQuestion = customer.SecurityQuestion;
                ServiceLevel = customer.ServiceLevel;
                ServiceRenewalDate = customer.ServiceRenewalDate;
                WebSite = customer.WebSite;
        }

        public Customer ToCustomer()
        {
            var entityCustomer = new Customer()
            {
                City = this.City,
                Country = this.Country,
                CustomerPassword = this.CustomerPassword,
                EmailAddress = this.EmailAddress,
                EmailAddressConfirmed = this.EmailAddressConfirmed,
                Firstname = this.Firstname,
                ID = ID,
                Lastname = this.Lastname,
                Name = this.Username,
                SecurityAnswer = this.SecurityAnswer,
                SecurityQuestion = this.SecurityQuestion,
                ServiceLevel = this.ServiceLevel,
                ServiceRenewalDate = this.ServiceRenewalDate,
                WebSite = this.WebSite
            };

            return entityCustomer;
        }
    }
}
