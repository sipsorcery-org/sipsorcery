// ============================================================================
// FileName: SIPAssetPersistor.cs
//
// Description:
// Base class for retrieving and persisting SIP asset objects from a persistent 
// data store such as a relational database or XML file.
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
//using System.Data;
using System.Linq;
//using System.Linq.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Sys
{
    public delegate void SIPAssetDelegate<T>(T asset);
    public delegate void SIPAssetsModifiedDelegate();
    public delegate void SIPAssetReloadedDelegate();
    public delegate T SIPAssetGetByIdDelegate<T>(Guid id);
    public delegate object SIPAssetGetPropertyByIdDelegate<T>(Guid id, string propertyName);
    public delegate T SIPAssetGetDelegate<T>(Expression<Func<T, bool>> where);
    public delegate int SIPAssetCountDelegate<T>(Expression<Func<T, bool>> where);
    public delegate List<T> SIPAssetGetListDelegate<T>(Expression<Func<T, bool>> where, string orderByField, int offset, int limit);
    public delegate T SIPAssetUpdateDelegate<T>(T asset);
    public delegate void SIPAssetUpdatePropertyDelegate<T>(Guid id, string propertyName, object value);
    public delegate void SIPAssetDeleteDelegate<T>(T asset);
    public delegate void SIPAssetDeleteBatchDelegate<T>(Expression<Func<T, bool>> where);

    public class SIPAssetPersistor<T> {
        private static ILog logger = AppState.logger;

        public virtual event SIPAssetDelegate<T> Added;
        public virtual event SIPAssetDelegate<T> Updated;
        public virtual event SIPAssetDelegate<T> Deleted;
        public virtual event SIPAssetsModifiedDelegate Modified;

        public virtual T Add(T asset) {
            throw new NotImplementedException("Method " + System.Reflection.MethodBase.GetCurrentMethod().Name + " in " + System.Reflection.MethodBase.GetCurrentMethod().DeclaringType.ToString() + " not implemented.");
        }

        public virtual T Update(T asset) {
            throw new NotImplementedException("Method " + System.Reflection.MethodBase.GetCurrentMethod().Name + " in " + System.Reflection.MethodBase.GetCurrentMethod().DeclaringType.ToString() + " not implemented.");
        }

        public virtual void UpdateProperty(Guid id, string propertyName, object value) {
            throw new NotImplementedException("Method " + System.Reflection.MethodBase.GetCurrentMethod().Name + " in " + System.Reflection.MethodBase.GetCurrentMethod().DeclaringType.ToString() + " not implemented.");
        }

        public virtual void Delete(T asset) {
            throw new NotImplementedException("Method " + System.Reflection.MethodBase.GetCurrentMethod().Name + " in " + System.Reflection.MethodBase.GetCurrentMethod().DeclaringType.ToString() + " not implemented.");
        }

        public virtual void Delete(Expression<Func<T, bool>> where) {
            throw new NotImplementedException("Method " + System.Reflection.MethodBase.GetCurrentMethod().Name + " in " + System.Reflection.MethodBase.GetCurrentMethod().DeclaringType.ToString() + " not implemented.");
        }

        public virtual T Get(Guid id) {
            throw new NotImplementedException("Method " + System.Reflection.MethodBase.GetCurrentMethod().Name + " in " + System.Reflection.MethodBase.GetCurrentMethod().DeclaringType.ToString() + " not implemented.");
        }

        public virtual object GetProperty(Guid id, string propertyName) {
            throw new NotImplementedException("Method " + System.Reflection.MethodBase.GetCurrentMethod().Name + " in " + System.Reflection.MethodBase.GetCurrentMethod().DeclaringType.ToString() + " not implemented.");
        }

        public virtual int Count(Expression<Func<T, bool>> where) {
            throw new NotImplementedException("Method " + System.Reflection.MethodBase.GetCurrentMethod().Name + " in " + System.Reflection.MethodBase.GetCurrentMethod().DeclaringType.ToString() + " not implemented.");
        }

        public virtual T Get(Expression<Func<T, bool>> where) {
            throw new NotImplementedException("Method " + System.Reflection.MethodBase.GetCurrentMethod().Name + " in " + System.Reflection.MethodBase.GetCurrentMethod().DeclaringType.ToString() + " not implemented.");
        }

        public virtual List<T> Get(Expression<Func<T, bool>> where,  string orderByField, int offset, int count) {
            throw new NotImplementedException("Method " + System.Reflection.MethodBase.GetCurrentMethod().Name + " in " + System.Reflection.MethodBase.GetCurrentMethod().DeclaringType.ToString() + " not implemented.");
        }
    }
}
