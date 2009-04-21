// ============================================================================
// FileName: DBLinqAssetPersistor.cs
//
// Description:
// Persistor class for storing SIP assets in relational databases via DBLinq.
//
// Author(s):
// Aaron Clauson
//
// History:
// 20 Apr 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.IO;
using System.Linq;
using System.Linq.Dynamic;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using DbLinq.Data.Linq;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Sys
{
    public class DBLinqAssetPersistor<T> : SIPAssetPersistor<T> where T : class, ISIPAsset, new()
    {
        private static ILog logger = AppState.logger;
        private static string m_newLine = AppState.NewLine;

        private DataContext m_dbLinqDataContext;
        private Table<T> m_dbLinqTable;

        public override event SIPAssetDelegate<T> Added;
        public override event SIPAssetDelegate<T> Updated;
        public override event SIPAssetDelegate<T> Deleted;

        public DBLinqAssetPersistor(DataContext dbLinqDataContext) {
            m_dbLinqDataContext = dbLinqDataContext;
            m_dbLinqTable = m_dbLinqDataContext.GetTable<T>();
        }

        public override T Add(T asset) {
            try {
                m_dbLinqTable.InsertOnSubmit(asset);
                m_dbLinqDataContext.SubmitChanges();

                if (Added != null) {
                    Added(asset);
                }

                return asset;
            }
            catch (Exception excp) {
                logger.Error("Exception DBLinqAssetPersistor Add. " + excp.Message);
                throw;
            }
        }

        public override T Update(T asset) {
            try {
                m_dbLinqDataContext.ExecuteDynamicUpdate(asset);

                if (Updated != null) {
                    Updated(asset);
                }

                return asset;
            }
            catch (Exception excp) {
                logger.Error("Exception DBLinqAssetPersistor Update. " + excp.Message);
                throw;
            }
        }

        public override void Delete(T asset) {
            try {
                m_dbLinqTable.DeleteOnSubmit(asset);
                m_dbLinqDataContext.SubmitChanges();

                if (Deleted != null) {
                    Deleted(asset);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception DBLinqAssetPersistor Delete. " + excp.Message);
                throw;
            }
        }

        public override void Delete(Expression<Func<T, bool>> whereClause) {
            try {
                var batch = from asset in m_dbLinqTable.Where(whereClause)
                            select asset;

                if (batch.Count() > 0) {
                    T[] batchArray = batch.ToArray();
                    for (int index = 0; index < batchArray.Length; index++) {
                        Delete(batchArray[index]);
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception DBLinqAssetPersistor Delete (batch). " + excp.Message);
                throw;
            }
        }

        public override T Get(Guid id) {
            try {
                string idString = id.ToString();

                return (from asset in m_dbLinqTable
                        where asset.Id == idString
                        select asset).FirstOrDefault();
            }
            catch (Exception excp) {
                logger.Error("Exception DBLinqAssetPersistor Get (id). " + excp.Message);
                throw;
            }
        }

        public override int Count(Expression<Func<T, bool>> whereClause) {
            try {
                if (whereClause == null) {
                    return m_dbLinqTable.Count();
                }
                else {
                    return m_dbLinqTable.Count(whereClause);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception DBLinqAssetPersistor Count. " + excp.Message);
                throw excp;
            }
        }

        public override T Get(Expression<Func<T, bool>> whereClause) {
            try {
                if (whereClause == null) {
                    throw new ArgumentException("The where clause must be specified for a non-list Get.");
                }
                else {
                    return (from asset in m_dbLinqTable.Where(whereClause)
                            select asset).FirstOrDefault();
                }
            }
            catch (Exception excp) {
                logger.Error("Exception DBLinqAssetPersistor Get (where). " + excp.Message);
                return default(T);
            }
        }

        public override List<T> Get(Expression<Func<T, bool>> whereClause, int offset, int count)
        {
            try
            {
                if (whereClause == null) {
                    return (from asset in m_dbLinqTable
                            select asset).ToList();
                }
                else {
                    return (from asset in m_dbLinqTable.Where(whereClause)
                            select asset).ToList();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception DBLinqAssetPersistor Get. " + excp.Message);
                return null;
            }
        }
    }
}
