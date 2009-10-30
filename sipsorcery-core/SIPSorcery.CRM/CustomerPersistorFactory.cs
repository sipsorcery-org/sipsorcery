// ============================================================================
// FileName: CustomerPersistorFactory.cs
//
// Description:
// Creates CustomerPersistor objects depending on the storage type specified. This
// class implements the standard factory design pattern in conjunction with the
// CustomerPersistor template class.
//
// Author(s):
// Aaron Clauson
//
// History:
// 21 Dec 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using SIPSorcery.Persistence;
using log4net;

namespace SIPSorcery.CRM
{
    public class CustomerPersistorFactory
    {
        private const string CUSTOMERS_XML_FILENAME = "customers.xml";
        private const string CUSTOMER_SESSIONS_XML_FILENAME = "customersessions.xml";

        public static SIPAssetPersistor<Customer> CreateCustomerPersistor(StorageTypes storageType, string storageDescription)
        {
            if (storageType == StorageTypes.XML)
            {
                if(!storageDescription.EndsWith(@"\")) {
                    storageDescription += @"\";
                }
                return new SIPAssetXMLPersistor<Customer>(storageDescription + CUSTOMERS_XML_FILENAME);
            }
            else if (storageType == StorageTypes.DBLinqMySQL || storageType == StorageTypes.DBLinqPostgresql) {
                //DataContext dbLinqContext = DBLinqContext.CreateDBLinqDataContext(storageType, storageDescription);
                //dbLinqContext.Log = Console.Out;
                return new DBLinqAssetPersistor<Customer>(storageType, storageDescription);
            }
            else if (storageType == StorageTypes.SimpleDBLinq) {
                return new SimpleDBAssetPersistor<Customer>(storageDescription);
            }
            else {
                throw new ApplicationException(storageType + " is not supported as a CreateCustomerPersistor option.");
            }
        }

        public static SIPAssetPersistor<CustomerSession> CreateCustomerSessionPersistor(StorageTypes storageType, string storageDescription) {
            if (storageType == StorageTypes.XML) {
                return new SIPAssetXMLPersistor<CustomerSession>(storageDescription + CUSTOMER_SESSIONS_XML_FILENAME);
            }
            else if (storageType == StorageTypes.NHibernate) {
                throw new ApplicationException(storageType + " is not supported as a CreateCustomerSessionPersistor option.");
            }
            else if (storageType == StorageTypes.DBLinqMySQL || storageType == StorageTypes.DBLinqPostgresql) {
                //DataContext dbLinqContext = DBLinqContext.CreateDBLinqDataContext(storageType, storageDescription);
                //dbLinqContext.Log = Console.Out;
                return new DBLinqAssetPersistor<CustomerSession>(storageType, storageDescription);
            }
            else if (storageType == StorageTypes.SimpleDBLinq) {
                return new SimpleDBAssetPersistor<CustomerSession>(storageDescription);
            }
            else {
                throw new ApplicationException(storageType + " is not supported as a CreateCustomerSessionPersistor option.");
            }
        }
    }
}
