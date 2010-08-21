//-----------------------------------------------------------------------------
// Filename: AuditLog.cs
//
// Description: Audits activities on the PBX CRM.
//
// History:
// 1 Oct 2005	Aaron Clauson	Created.
//
// License: 
// Public Domain
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Data;
using System.Text.RegularExpressions;
using System.Xml;
using Aza.Configuration;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace Aza.Configuration
{	
	public class AuditLog_other
	{	
		private static ILog logger = AppState.logger;

		private StorageLayer m_storageLayer = null;
		private string m_dbConnStr = null;
		private StorageTypes m_storageType;

		public AuditLog_other(StorageTypes storageType, string dbConnStr)
		{
			m_storageType = storageType;
			m_dbConnStr = dbConnStr;

			m_storageLayer = new StorageLayer(m_storageType, m_dbConnStr);
		}

		public void AddEntry(string description)
		{

		}

		#region Unit tests.
		
		#if UNITTEST

		[TestFixture]
		public class AuditLogUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");
			}
	
		}

		
		#endif

		#endregion
	}
}
