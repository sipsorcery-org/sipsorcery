//-----------------------------------------------------------------------------
// Filename: CustomerDataLayer.cs
//
// Description: Data layer class for Customer entities.
// 
// History:
// 27 Aug 2013	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2013 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd. 
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
using System.Text;
using System.Transactions;
using SIPSorcery.Entities;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Entities
{
    public class CustomerDataLayer
    {
        private static ILog logger = AppState.logger;

        /// <summary>
        /// Retrieves the customer record that matches the specified ID.
        /// </summary>
        /// <param name="id">The unique ID of the cutomer to try and retrieve.</param>
        /// <returns>If found the customer record otherwise null.</returns>
        public Customer Get(string id)
        {
            if (id.IsNullOrBlank())
            {
                return null;
            }

            using (var db = new SIPSorceryEntities())
            {
                return (from cu in db.Customers
                        where cu.ID == id
                        select cu).SingleOrDefault();
            }
        }

        /// <summary>
        /// Retrieves the customer record that matches the specified name (name in this the username).
        /// </summary>
        /// <param name="name">The name of the customer to try and retrieve.</param>
        /// <returns>If found the customer record otherwise null.</returns>
        public Customer GetForName(string name)
        {
            if(name.IsNullOrBlank())
            {
                return null;
            }

            using (var db = new SIPSorceryEntities())
            {
                string lowerName = name.Trim().ToLower();

                return (from cu in db.Customers
                        where cu.Name.ToLower() == lowerName
                        select cu).SingleOrDefault();
            }
        }

        /// <summary>
        /// Retrieves the customer record that matches the specified email address.
        /// </summary>
        /// <param name="emailAddress">The email address of the customer to try and retrieve.</param>
        /// <returns>If found the customer record otherwise null.</returns>
        public Customer GetForEmail(string emailAddress)
        {
            if (emailAddress.IsNullOrBlank())
            {
                return null;
            }

            using (var db = new SIPSorceryEntities())
            {
                string lowerName = emailAddress.Trim().ToLower();

                return (from cu in db.Customers
                        where cu.EmailAddress.ToLower() == emailAddress
                        select cu).SingleOrDefault();
            }
        }

        /// <summary>
        /// Attempts to retrieve a customer record based on the FTP prefix.
        /// </summary>
        /// <param name="ftpPrefix">The FTP prefix to retrieve the customer for.</param>
        /// <returns>If found the matching Customer record otherwise null.</returns>
        public Customer GetForFTPPrefix(string ftpPrefix)
        {
            if (ftpPrefix.IsNullOrBlank())
            {
                return null;
            }

            using (var db = new SIPSorceryEntities())
            {
                string ftpPrefixLower = ftpPrefix.ToLower();

                return (from cu in db.Customers
                        where
                            cu.FTPPrefix.ToLower() == ftpPrefixLower
                        select cu).SingleOrDefault();
            }
        }
    }
}
