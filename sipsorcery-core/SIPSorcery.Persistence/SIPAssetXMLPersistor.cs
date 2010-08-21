// ============================================================================
// FileName: SIPAssetXMLPersistor.cs
//
// Description:
// Persistor class for storing SIP assets in an XML file.
//
// Author(s):
// Aaron Clauson
//
// History:
// 13 Apr 2009	Aaron Clauson	Created.
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
using System.IO;
using System.Linq;
using System.Linq.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Persistence
{
    public class SIPAssetXMLPersistor<T> : SIPAssetPersistor<T> where T : ISIPAsset, new()
    {
        private const int RELOAD_SPACING_SECONDS = 3;                           // Minimum interval the XML file change events will be allowed.
        private const int MINIMUM_FILE_UDPATE_INTERVAL = 2;

        private static string m_newLine = AppState.NewLine;

        private Dictionary<Guid, T> m_sipAssets = new Dictionary<Guid, T>();

        private string m_xmlAssetFilePath;
        private FileSystemWatcher m_xmlFileWatcher;
        private DateTime m_lastReload;
        private bool m_savePending;
        private DateTime m_lastSave = DateTime.MinValue;

        public override event SIPAssetDelegate<T> Added;
        public override event SIPAssetDelegate<T> Updated;
        public override event SIPAssetDelegate<T> Deleted;
        public override event SIPAssetsModifiedDelegate Modified;

        public SIPAssetXMLPersistor(string xmlFilePath)
        {
            m_xmlAssetFilePath = xmlFilePath;

            if (!File.Exists(m_xmlAssetFilePath))
            {
                logger.Warn("File " + m_xmlAssetFilePath + " does not exist for SIPAssetXMLPersistor, creating new one.");
                FileStream fs = File.Create(m_xmlAssetFilePath);
                byte[] bytes = Encoding.ASCII.GetBytes("<" + (new T()).GetXMLDocumentElementName() + "/>");
                fs.Write(bytes, 0, bytes.Length);
                fs.Close();
            }

            try
            {
                XmlDocument sipAssetDOM = LoadSIPAssetsDOM(m_xmlAssetFilePath);
                LoadSIPAssetsFromXML(sipAssetDOM);
            }
            catch (Exception excp)
            {
                logger.Error("Exception loading XML from " + m_xmlAssetFilePath + " (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override T Add(T sipAsset)
        {
            try
            {
                if (sipAsset == null)
                {
                    throw new ArgumentException("The " + sipAsset.GetType().ToString() + " cannot be empty for Add.");
                }
                else if (Get(a => a.Id == sipAsset.Id) != null)
                {
                    throw new ArgumentException("SIP Asset with id " + sipAsset.Id.ToString() + " already exists.");
                }

                Guid id = sipAsset.Id;

                lock (m_sipAssets)
                {
                    m_sipAssets.Add(id, sipAsset);
                }

                WriteSIPAssetXML();

                if (Added != null)
                {
                    Added(sipAsset);
                }

                return Get(id);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAssetXMLPersistor Add (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override T Update(T sipAsset)
        {
            try
            {
                if (sipAsset == null)
                {
                    throw new ArgumentException("The SIP Asset cannot be empty for an Update.");
                }

                Guid id = sipAsset.Id;

                if (m_sipAssets.ContainsKey(id))
                {

                    lock (m_sipAssets)
                    {
                        m_sipAssets[id] = sipAsset;
                    }

                    WriteSIPAssetXML();

                    if (Updated != null)
                    {
                        Updated(sipAsset);
                    }

                    return m_sipAssets[id];
                }
                else
                {
                    throw new ApplicationException("Could not update SIP Asset with id " + sipAsset.Id + " (for " + typeof(T).Name + "), no existing asset found.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAssetXMLPersistor Update. " + excp.Message);
                throw;
            }
        }

        public override void UpdateProperty(Guid id, string propertyName, object value)
        {
            try
            {
                // Find modified poperty.
                PropertyInfo property = typeof(T).GetProperty(propertyName);
                if (property == null)
                {
                    throw new ApplicationException("Property " + propertyName + " for " + typeof(T).Name + " could not be found, UpdateProperty failed.");
                }
                else
                {
                    if (m_sipAssets.ContainsKey(id))
                    {
                        lock (m_sipAssets)
                        {
                            property.SetValue(m_sipAssets[id], value, null);
                        }

                        WriteSIPAssetXML();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAssetXMLPersistor UpdateProperty (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override void IncrementProperty(Guid id, string propertyName)
        {
            try
            {
                // Find modified poperty.
                PropertyInfo property = typeof(T).GetProperty(propertyName);
                if (property == null)
                {
                    throw new ApplicationException("Property " + propertyName + " for " + typeof(T).Name + " could not be found, IncrementProperty failed.");
                }
                else
                {
                    if (m_sipAssets.ContainsKey(id))
                    {
                        lock (m_sipAssets)
                        {
                            property.SetValue(m_sipAssets[id], Convert.ToInt32(property.GetValue(m_sipAssets[id], null)) + 1, null);
                        }

                        WriteSIPAssetXML();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAssetXMLPersistor IncrementProperty (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override void DecrementProperty(Guid id, string propertyName)
        {
            try
            {
                // Find modified poperty.
                PropertyInfo property = typeof(T).GetProperty(propertyName);
                if (property == null)
                {
                    throw new ApplicationException("Property " + propertyName + " for " + typeof(T).Name + " could not be found, DecrementProperty failed.");
                }
                else
                {
                    if (m_sipAssets.ContainsKey(id))
                    {
                        lock (m_sipAssets)
                        {
                            property.SetValue(m_sipAssets[id], Convert.ToInt32(property.GetValue(m_sipAssets[id], null)) - 1, null);
                        }

                        WriteSIPAssetXML();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAssetXMLPersistor DecrementProperty (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override void Delete(T sipAsset)
        {
            try
            {
                if (sipAsset == null)
                {
                    throw new ArgumentException("The SIP Asset cannot be empty for Delete.");
                }

                //logger.Debug("SIPAssetsXMLPersistor attempting to delete " + sipAsset.Id + " type " + sipAsset.GetType().ToString() + ".");

                Guid id = sipAsset.Id;

                T existingAsset = m_sipAssets[id];

                if (existingAsset != null)
                {

                    lock (m_sipAssets)
                    {
                        m_sipAssets.Remove(id);
                    }

                    WriteSIPAssetXML();

                    if (Deleted != null)
                    {
                        Deleted(sipAsset);
                    }
                }
                else
                {
                    throw new ApplicationException("An attempt was made to delete a non-existent " + sipAsset.GetType().ToString() + " failed in SIPAssetXMLPersistor.");
                }
            }
            catch (ApplicationException appExcp)
            {
                logger.Warn(appExcp.Message);
            }
            catch (Exception excp)
            {
                logger.Error("Exception Delete (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override void Delete(Expression<Func<T, bool>> whereClause)
        {
            try
            {
                var batch = m_sipAssets.Values.Where(a => whereClause.Compile()(a));

                if (batch.Count() > 0)
                {

                    //logger.Debug("SIPAssetXMLPersistor deleting " + batch.Count() + " " + batch.First().GetType().ToString() + ".");

                    T[] batchArray = batch.ToArray();
                    for (int index = 0; index < batchArray.Length; index++)
                    {
                        Delete(batchArray[index]);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAssetXMLPersistor Delete (batch) (for " + typeof(T).Name + "). " + excp.Message);
            }
        }

        public override T Get(Guid id)
        {

            if (m_sipAssets.Count == 0)
            {
                return default(T);
            }
            else
            {
                if (m_sipAssets.ContainsKey(id))
                {
                    return m_sipAssets[id];
                }
                else
                {
                    logger.Debug("Could not locate a " + typeof(T).Name + " SIP Asset for id " + id.ToString() + ".");
                    return default(T);
                }
            }
        }

        public override object GetProperty(Guid id, string propertyName)
        {
            if (m_sipAssets.Count == 0)
            {
                return null;
            }
            else
            {
                if (m_sipAssets.ContainsKey(id))
                {
                    // Find modified poperty.
                    PropertyInfo property = typeof(T).GetProperty(propertyName);
                    if (property == null)
                    {
                        throw new ApplicationException("Property " + propertyName + " for " + typeof(T).Name + " could not be found, GetProperty failed.");
                    }
                    else
                    {
                        return property.GetValue(m_sipAssets[id], null);
                    }
                }
                else
                {
                    logger.Debug("Could not locate a " + typeof(T).Name + " SIP Asset for id " + id.ToString() + ".");
                    return null;
                }
            }
        }

        public override int Count(Expression<Func<T, bool>> whereClause)
        {
            try
            {
                if (whereClause == null)
                {
                    return m_sipAssets.Values.Count;
                }
                else
                {
                    return m_sipAssets.Values.Where(a => whereClause.Compile()(a)).Count();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAssetXMLPersistor Count (for " + typeof(T).Name + "). " + excp.Message);
                throw excp;
            }
        }

        public override T Get(Expression<Func<T, bool>> whereClause)
        {
            try
            {
                return m_sipAssets.Values.Where(a => whereClause.Compile()(a)).FirstOrDefault();
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAssetXMLPersistor Get (for " + typeof(T).Name + "). " + excp.Message);
                return default(T);
            }
        }

        public override List<T> Get(Expression<Func<T, bool>> whereClause, string orderByField, int offset, int count)
        {
            try
            {
                List<T> subList = null;
                if (whereClause == null)
                {
                    subList = m_sipAssets.Values.ToList<T>();
                }
                else
                {
                    subList = m_sipAssets.Values.Where(a => whereClause.Compile()(a)).ToList<T>();
                }

                if (subList != null)
                {
                    if (offset >= 0)
                    {
                        if (count == 0 || count == Int32.MaxValue)
                        {
                            return subList.OrderBy(x => x.Id).Skip(offset).ToList<T>();
                        }
                        else
                        {
                            return subList.OrderBy(x => x.Id).Skip(offset).Take(count).ToList<T>();
                        }
                    }
                    else
                    {
                        return subList.OrderBy(x => x.Id).ToList<T>(); ;
                    }
                }

                return subList;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAssetXMLPersistor Get (for " + typeof(T).Name + "). " + excp.Message);
                return null;
            }
        }

        private void LoadSIPAssetsFromXML(XmlDocument xmlDom)
        {

            Dictionary<Guid, object> assets = (new T()).Load(xmlDom);

            m_sipAssets.Clear();
            foreach (KeyValuePair<Guid, object> keyValPair in assets)
            {
                m_sipAssets.Add(keyValPair.Key, (T)keyValPair.Value);
            }

            if (Modified != null)
            {
                Modified();
            }

            if (m_xmlFileWatcher == null)
            {
                string dir = Path.GetDirectoryName(m_xmlAssetFilePath);
                string file = Path.GetFileName(m_xmlAssetFilePath);
                logger.Debug("Starting file watch on " + dir + " and " + file + ".");
                m_xmlFileWatcher = new FileSystemWatcher(dir, file);
                m_xmlFileWatcher.Changed += new FileSystemEventHandler(AssetXMLFileChanged);
                m_xmlFileWatcher.EnableRaisingEvents = true;
            }
        }

        private void AssetXMLFileChanged(object sender, FileSystemEventArgs e)
        {
            if (DateTime.Now.Subtract(m_lastReload).TotalSeconds > RELOAD_SPACING_SECONDS)
            {
                try
                {
                    m_lastReload = DateTime.Now;
                    logger.Debug("Reloading SIP Assets.");

                    XmlDocument sipAssetDOM = LoadSIPAssetsDOM(m_xmlAssetFilePath);
                    LoadSIPAssetsFromXML(sipAssetDOM);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception AssetXMLFileChanged. " + excp.Message);
                }
            }
        }

        private XmlDocument LoadSIPAssetsDOM(string filePath)
        {

            string copyFilename = filePath + ".copy";
            File.Copy(filePath, copyFilename, true);

            XmlDocument sipAssetDOM = new XmlDocument();
            sipAssetDOM.Load(copyFilename);

            if (sipAssetDOM == null || sipAssetDOM.DocumentElement == null)
            {
                throw new ApplicationException("Could not load SIP Assets XML.");
            }
            else
            {
                return sipAssetDOM;
            }
        }

        private void WriteSIPAssetXML()
        {
            if (!m_savePending)
            {
                m_savePending = true;
                ThreadPool.QueueUserWorkItem(WriteSIPAssetXMLAsync);
            }
        }

        private void WriteSIPAssetXMLAsync(object state)
        {
            try
            {
                if (m_xmlFileWatcher != null)
                {
                    m_xmlFileWatcher.EnableRaisingEvents = false;
                }

                if (DateTime.Now.Subtract(m_lastSave).TotalSeconds < MINIMUM_FILE_UDPATE_INTERVAL)
                {
                    Thread.Sleep(MINIMUM_FILE_UDPATE_INTERVAL * 1000);
                }

                //logger.Debug("Attempting to write SIP Asset XML to " + m_xmlAssetFilePath + ".");

                StreamWriter sipAssetStream = new StreamWriter(m_xmlAssetFilePath, false);
                string docElementName = (new T()).GetXMLDocumentElementName();
                sipAssetStream.WriteLine("<" + docElementName + ">");

                lock (m_sipAssets)
                {
                    foreach (T sipAsset in m_sipAssets.Values)
                    {
                        sipAssetStream.Write(((ISIPAsset)sipAsset).ToXML());
                    }
                }

                sipAssetStream.WriteLine("</" + docElementName + ">");
                sipAssetStream.Close();

                m_lastSave = DateTime.Now;
            }
            catch (Exception excp)
            {
                logger.Error("Exception WriteSIPAssetXMLAsync. " + excp.Message);
            }
            finally
            {
                m_savePending = false;

                try
                {
                    if (m_xmlFileWatcher != null)
                    {
                        m_xmlFileWatcher.EnableRaisingEvents = true;
                    }
                }
                catch (Exception excp)
                {
                    logger.Error("Exception WriteSIPAssetXMLAsync finally. " + excp.Message);
                }
            }
        }

        public static Dictionary<Guid, object> LoadAssetsFromXMLRecordSet(XmlDocument dom)
        {
            try
            {
                Dictionary<Guid, object> assets = new Dictionary<Guid, object>();

                DataSet sipAssetSet = new DataSet();
                XmlTextReader xmlReader = new XmlTextReader(dom.OuterXml, XmlNodeType.Document, null);
                sipAssetSet.ReadXml(xmlReader);

                if (sipAssetSet != null && sipAssetSet.Tables != null && sipAssetSet.Tables.Count > 0)
                {
                    foreach (DataRow row in sipAssetSet.Tables[0].Rows)
                    {
                        try
                        {
                            T sipAsset = new T();
                            sipAsset.Load(row);
                            //logger.Debug(" loaded " + sipAsset.GetType().ToString() + " id " + sipAsset.GetId() + ".");
                            assets.Add(sipAsset.Id, sipAsset);
                        }
                        catch (Exception excp)
                        {
                            logger.Error("Exception loading SIP asset record in LoadAssetsFromXMLRecordSet (" + (new T()).GetType().ToString() + "). " + excp.Message);
                        }
                    }

                    logger.Debug(" " + assets.Count + " " + (new T()).GetType().ToString() + " assets loaded from XML record set.");
                }
                else
                {
                    //logger.Warn("The XML supplied to LoadAssetsFromXMLRecordSet for asset type " + (new T()).GetType().ToString() + " did not contain any assets.");
                    logger.Debug(" no " + (new T()).GetType().ToString() + " assets loaded from XML record set.");
                }

                xmlReader.Close();

                return assets;
            }
            catch (Exception excp)
            {
                logger.Error("Exception LoadAssetsFromXMLRecordSet. " + excp.Message);
                throw;
            }
        }
    }
}
