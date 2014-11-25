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
// Copyright (c) 2008 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
using SIPSorcery.Persistence;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Persistence
{
    public class SIPAssetPersistorFactory<T> where T : class, ISIPAsset, new()
    {
        private static ILog logger = AppState.logger;

        public static SIPAssetPersistor<T> CreateSIPAssetPersistor(StorageTypes storageType, string storageConnectionStr, string filename)
        {
            try
            {
                if (storageType == StorageTypes.XML)
                {
                    if (!storageConnectionStr.EndsWith(@"\"))
                    {
                        storageConnectionStr += @"\";
                    }
                    return new SIPAssetXMLPersistor<T>(storageConnectionStr + filename);
                }
                else if (storageType == StorageTypes.SQLLinqMySQL)
                {
                    return new SQLAssetPersistor<T>(MySql.Data.MySqlClient.MySqlClientFactory.Instance, storageConnectionStr);
                }
                else if (storageType == StorageTypes.SQLLinqPostgresql)
                {
                    return new SQLAssetPersistor<T>(Npgsql.NpgsqlFactory.Instance, storageConnectionStr);
                }
                //else if (storageType == StorageTypes.SimpleDBLinq)
                //{
                //    return new SimpleDBAssetPersistor<T>(storageConnectionStr);
                //}
                else if (storageType == StorageTypes.SQLLinqMSSQL)
                {
                    return new MSSQLAssetPersistor<T>(System.Data.SqlClient.SqlClientFactory.Instance, storageConnectionStr);
                }
                //else if (storageType == StorageTypes.SQLLinqOracle)
                //{
                //    return new SQLAssetPersistor<T>(Oracle.DataAccess.Client.OracleClientFactory.Instance, storageConnectionStr);
                //}
                else
                {
                    throw new ApplicationException(storageType + " is not supported as a CreateSIPAssetPersistor option.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CreateSIPAssetPersistor for " + storageType + ". " + excp.Message);
                throw;
            }
        }
    }
}
