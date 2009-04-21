// ============================================================================
// FileName: SIPAssetPersistorFactory.cs
//
// Description:
// Creates SIPAssetPersistor objects depending on the storage type specified. This
// class implements the standard factory design pattern in conjunction with the
// SIPAssetPersistor template class.
//
// Author(s):
// Aaron Clauson
//
// History:
// 01 Oct 2008	Aaron Clauson	Created.
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
using DbLinq.Data.Linq;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    public class SIPAssetPersistorFactory
    {
        public static SIPAssetPersistor<SIPAccount> CreateSIPAccountPersistor(StorageTypes storageType, string storageConnectionStr) {
            if (storageType == StorageTypes.XML) {
                return new SIPAssetXMLPersistor<SIPAccount>(storageConnectionStr);
            }
            else if (storageType == StorageTypes.NHibernate) {
                //return new SIPAssetNHibernatePersistor<T>(NHibernateHelper.OpenSession());
                throw new ApplicationException(storageType + " is not supported as a CreateSIPAssetPersistor option.");
            }
            else if (storageType == StorageTypes.DBLinqMySQL || storageType == StorageTypes.DBLinqPostgresql) {
                DataContext dbLinqContext = DBLinqContext.CreateDBLinqDataContext(storageType, storageConnectionStr);
                //dbLinqContext.Log = Console.Out;
                return new DBLinqAssetPersistor<SIPAccount>(dbLinqContext);
            }
            else {
                throw new ApplicationException(storageType + " is not supported as a CreateSIPAssetPersistor option.");
            }
        }

        public static SIPAssetPersistor<SIPDialPlan> CreateDialPlanPersistor(StorageTypes storageType, string storageConnectionStr) {
            if (storageType == StorageTypes.XML) {
                return new SIPAssetXMLPersistor<SIPDialPlan>(storageConnectionStr);
            }
            else if (storageType == StorageTypes.NHibernate) {
               // return new SIPAssetNHibernatePersistor<SIPDialPlan>(NHibernateHelper.OpenSession());
                throw new ApplicationException(storageType + " is not supported as a CreateDialPlanPersistor option.");
            }
            else if (storageType == StorageTypes.DBLinqMySQL || storageType == StorageTypes.DBLinqPostgresql) {
                DataContext dbLinqContext = DBLinqContext.CreateDBLinqDataContext(storageType, storageConnectionStr);
                //dbLinqContext.Log = Console.Out;
                return new DBLinqAssetPersistor<SIPDialPlan>(dbLinqContext);
            }
            else {
                throw new ApplicationException(storageType + " is not supported as a CreateDialPlanPersistor option.");
            }
        }

        public static SIPAssetPersistor<SIPProvider> CreateSIPProviderPersistor(StorageTypes storageType, string storageConnectionStr) {
            if (storageType == StorageTypes.XML) {
                return new SIPAssetXMLPersistor<SIPProvider>(storageConnectionStr);
            }
            else if (storageType == StorageTypes.NHibernate) {
                //return new SIPAssetNHibernatePersistor<SIPProvider>(NHibernateHelper.OpenSession());
                throw new ApplicationException(storageType + " is not supported as a CreateSIPProviderPersistor option.");
            }
            else if (storageType == StorageTypes.DBLinqMySQL || storageType == StorageTypes.DBLinqPostgresql) {
                DataContext dbLinqContext = DBLinqContext.CreateDBLinqDataContext(storageType, storageConnectionStr);
                //dbLinqContext.Log = Console.Out;
                return new DBLinqAssetPersistor<SIPProvider>(dbLinqContext);
            }
            else {
                throw new ApplicationException(storageType + " is not supported as a CreateSIPProviderPersistor option.");
            }
        }

        public static SIPAssetPersistor<SIPProviderBinding> CreateSIPProviderBindingPersistor(StorageTypes storageType, string storageConnectionStr) {
            if (storageType == StorageTypes.XML) {
                return new SIPAssetXMLPersistor<SIPProviderBinding>(storageConnectionStr);
            }
            else if (storageType == StorageTypes.NHibernate) {
                //return new SIPAssetNHibernatePersistor<SIPProviderBinding>(NHibernateHelper.OpenSession());
                throw new ApplicationException(storageType + " is not supported as a CreateSIPProviderBindingPersistor option.");
            }
            else if (storageType == StorageTypes.DBLinqMySQL || storageType == StorageTypes.DBLinqPostgresql) {
                DataContext dbLinqContext = DBLinqContext.CreateDBLinqDataContext(storageType, storageConnectionStr);
                //dbLinqContext.Log = Console.Out;
                return new DBLinqAssetPersistor<SIPProviderBinding>(dbLinqContext);
            }
            else {
                throw new ApplicationException(storageType + " is not supported as a CreateSIPProviderBindingPersistor option.");
            }
        }

        public static SIPAssetPersistor<SIPDomain> CreateSIPDomainPersistor(StorageTypes storageType, string storageConnectionStr) {
            if (storageType == StorageTypes.XML) {
                return new SIPAssetXMLPersistor<SIPDomain>(storageConnectionStr);
            }
            else if (storageType == StorageTypes.NHibernate) {
                //return new SIPAssetNHibernatePersistor<SIPDomain>(NHibernateHelper.OpenSession());
                throw new ApplicationException(storageType + " is not supported as a CreateSIPDomainPersistor option.");
            }
            else if (storageType == StorageTypes.DBLinqMySQL || storageType == StorageTypes.DBLinqPostgresql) {
                DataContext dbLinqContext = DBLinqContext.CreateDBLinqDataContext(storageType, storageConnectionStr);
                //dbLinqContext.Log = Console.Out;
                return new DBLinqAssetPersistor<SIPDomain>(dbLinqContext);
            }
            else {
                throw new ApplicationException(storageType + " is not supported as a CreateSIPDomainPersistor option.");
            }
        }

        public static SIPAssetPersistor<SIPRegistrarBinding> CreateSIPRegistrarBindingPersistor(StorageTypes storageType, string storageConnectionStr) {
            if (storageType == StorageTypes.XML) {
                return new SIPAssetXMLPersistor<SIPRegistrarBinding>(storageConnectionStr);
            }
            else if (storageType == StorageTypes.NHibernate) {
                //return new SIPAssetNHibernatePersistor<SIPRegistrarBinding>(NHibernateHelper.OpenSession());
                throw new ApplicationException(storageType + " is not supported as a CreateSIPRegistrarBindingPersistor option.");
            }
            else if (storageType == StorageTypes.DBLinqMySQL || storageType == StorageTypes.DBLinqPostgresql) {
                DataContext dbLinqContext = DBLinqContext.CreateDBLinqDataContext(storageType, storageConnectionStr);
                //dbLinqContext.Log = Console.Out;
                return new DBLinqAssetPersistor<SIPRegistrarBinding>(dbLinqContext);
            }
            else {
                throw new ApplicationException(storageType + " is not supported as a CreateSIPRegistrarBindingPersistor option.");
            }
        }

        public static SIPAssetPersistor<SIPDialogueAsset> CreateSIPDialoguePersistor(StorageTypes storageType, string storageConnectionStr) {
            if (storageType == StorageTypes.XML) {
                return new SIPAssetXMLPersistor<SIPDialogueAsset>(storageConnectionStr);
            }
            else if (storageType == StorageTypes.NHibernate) {
                //return new SIPAssetNHibernatePersistor<SIPDialogueAsset>(NHibernateHelper.OpenSession());
                throw new ApplicationException(storageType + " is not supported as a CreateSIPDialoguePersistor option.");
            }
            else if (storageType == StorageTypes.DBLinqMySQL || storageType == StorageTypes.DBLinqPostgresql) {
                DataContext dbLinqContext = DBLinqContext.CreateDBLinqDataContext(storageType, storageConnectionStr);
                //dbLinqContext.Log = Console.Out;
                return new DBLinqAssetPersistor<SIPDialogueAsset>(dbLinqContext);
            }
            else {
                throw new ApplicationException(storageType + " is not supported as a CreateSIPDialoguePersistor option.");
            }
        }

        public static SIPAssetPersistor<SIPCDRAsset> CreateSIPCDRPersistor(StorageTypes storageType, string storageConnectionStr) {
            if (storageType == StorageTypes.XML) {
                return new SIPAssetXMLPersistor<SIPCDRAsset>(storageConnectionStr);
            }
            else if (storageType == StorageTypes.NHibernate) {
                //return new SIPAssetNHibernatePersistor<SIPCDRAsset>(NHibernateHelper.OpenSession());
                throw new ApplicationException(storageType + " is not supported as a CreateSIPCDRPersistor option.");
            }
            else if (storageType == StorageTypes.DBLinqMySQL || storageType == StorageTypes.DBLinqPostgresql) {
                DataContext dbLinqContext = DBLinqContext.CreateDBLinqDataContext(storageType, storageConnectionStr);
                //dbLinqContext.Log = Console.Out;
                return new DBLinqAssetPersistor<SIPCDRAsset>(dbLinqContext);
            }
            else {
                throw new ApplicationException(storageType + " is not supported as a CreateSIPCDRPersistor option.");
            }
        }
    }
}
