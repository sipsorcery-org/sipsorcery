// ============================================================================
// FileName: WrapperImpersonationContext.cs
//
// Description:
// Creates a Windows impersonation context for a specified set of login credentials.
//
// Author(s):
// Aaron Clauson
//
// History:
// 18 Jul 2010  Aaron Clauson   Created from http://www.vanotegem.nl/PermaLink,guid,36633846-2eca-40fe-9957-2859d8a244dc.aspx.
//
// License: 
// Public domain.
// ============================================================================

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Security.Permissions;
using System.Threading;

#if UNITTEST
using NUnit.Framework;
#endif

public class WrapperImpersonationContext
{
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool LogonUser(String lpszUsername, String lpszDomain,
    String lpszPassword, int dwLogonType, int dwLogonProvider, ref IntPtr phToken);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public extern static bool CloseHandle(IntPtr handle);

    private const int LOGON32_PROVIDER_DEFAULT = 0;
    private const int LOGON32_LOGON_INTERACTIVE = 2;

    private string m_Domain;
    private string m_Password;
    private string m_Username;
    private IntPtr m_Token;

    private WindowsImpersonationContext m_Context = null;

    protected bool IsInContext
    {
        get { return m_Context != null; }
    }

    public WrapperImpersonationContext(string domain, string username, string password)
    {
        m_Domain = domain;
        m_Username = username;
        m_Password = password;
    }

    [PermissionSetAttribute(SecurityAction.Demand, Name = "FullTrust")]
    public void Enter()
    {
        if (this.IsInContext) return;
        m_Token = new IntPtr(0);
        try
        {
            m_Token = IntPtr.Zero;
            bool logonSuccessfull = LogonUser(
               m_Username,
               m_Domain,
               m_Password,
               LOGON32_LOGON_INTERACTIVE,
               LOGON32_PROVIDER_DEFAULT,
               ref m_Token);
            if (logonSuccessfull == false)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error);
            }
            WindowsIdentity identity = new WindowsIdentity(m_Token);
            m_Context = identity.Impersonate();
        }
        catch 
        {
            throw;
        }
    }

    [PermissionSetAttribute(SecurityAction.Demand, Name = "FullTrust")]
    public void Leave()
    {
        if (this.IsInContext == false) return;
        m_Context.Undo();

        if (m_Token != IntPtr.Zero) CloseHandle(m_Token);
        m_Context = null;
    }

    #region Unit testing.

    #if UNITTEST

    [TestFixture]
    public class WrapperImpersonationContextUnitTest 
    { 
        [TestFixtureSetUp]
        public void Init() { }

        [TestFixtureTearDown]
        public void Dispose() { }

        [Test]
        public void SampleTest() 
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
        }

        [Test]
        public void EnterContextUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            Console.WriteLine("identity=" + WindowsIdentity.GetCurrent().Name + ".");
            using (StreamReader sr = new StreamReader(@"c:\temp\impersonationtest.txt"))
            {
                Console.WriteLine(sr.ReadToEnd());
            }

            WrapperImpersonationContext context = new WrapperImpersonationContext(null, "sipsorcery-appsvr", "password");
            context.Enter();
            Console.WriteLine("identity=" + WindowsIdentity.GetCurrent().Name + ".");

            using (StreamReader sr = new StreamReader(@"c:\temp\impersonationtest.txt"))
            {
                Console.WriteLine(sr.ReadToEnd());
            }

            context.Leave();
            Console.WriteLine("identity=" + WindowsIdentity.GetCurrent().Name + ".");
        }
    }

    #endif

    #endregion
}