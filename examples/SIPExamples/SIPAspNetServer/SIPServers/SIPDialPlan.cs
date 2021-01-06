//-----------------------------------------------------------------------------
// Filename: SIPDialPlan.cs
//
// Description: This class loads and compiles the dynamic dialplan. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 05 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace demo
{
    public class SIPDialPlanGlobals
    {
        public UASInviteTransaction UasTx { get; set; }
        public ISIPAccount From { get; set; }
    }

    public class SIPDialPlan
    {
        public const string DEFAULT_DIAL_PLAN_PATH = "dialplan.txt";

        public const string DIAL_PLAN_CODE_TEMPLATE =
@"public static class DialPlanScript
{{
    public static SIPCallDescriptor Lookup(UASInviteTransaction uasTx, ISIPAccount from)
    {{
        {0}
    }}
}}";
        private readonly ILogger _logger = SIPSorcery.LogFactory.CreateLogger<SIPDialPlan>();

        private string _dialPlanPath;
        private DateTime _dialPlanLastChange;
        private ScriptRunner<Object> _dialPlanScriptRunner;
        private FileSystemWatcher _dialPlanWatcher;

        public SIPDialPlan(string dialplanPath = DEFAULT_DIAL_PLAN_PATH)
        {
            if(string.IsNullOrWhiteSpace(dialplanPath))
            {
                throw new ArgumentException(nameof(dialplanPath));
            }

            _dialPlanPath = Path.GetFullPath(dialplanPath);
        }

        public void Load()
        {
            if (!File.Exists(_dialPlanPath))
            {
                throw new ApplicationException($"The dial plan file could not be found at {_dialPlanPath}.");
            }

            CompileDialPlan(_dialPlanPath);

            _dialPlanWatcher = new FileSystemWatcher(Path.GetDirectoryName(_dialPlanPath), Path.GetFileName(_dialPlanPath));
            _dialPlanWatcher.Changed += new FileSystemEventHandler(DialPlanChanged);
            _dialPlanWatcher.EnableRaisingEvents = true;
        }

        private void CompileDialPlan(string dialplanPath)
        {
            string dialPlanScript = File.ReadAllText(dialplanPath);
            string dialPlanClass = string.Format(DIAL_PLAN_CODE_TEMPLATE, dialPlanScript);

            _logger.LogDebug($"Compiling dialplan from {dialplanPath}...");

            DateTime startTime = DateTime.Now;

            _dialPlanScriptRunner = CSharpScript.Create(dialPlanClass,
               ScriptOptions.Default
               .WithImports("System", "SIPSorcery.SIP", "SIPSorcery.SIP.App")
               .AddReferences(typeof(SIPSorcery.SIP.App.SIPCallDescriptor).GetTypeInfo().Assembly),
               typeof(SIPDialPlanGlobals))
               .ContinueWith("DialPlanScript.Lookup(UasTx, From)")
               .CreateDelegate();

            var duration = DateTime.Now.Subtract(startTime);
            _logger.LogInformation($"Dialplan successfully compiled in {duration.TotalMilliseconds:0.##}ms.");

            _dialPlanLastChange = DateTime.Now;
        }

        /// <summary>
        /// This function type is to allow B2B user agents to lookup the forwarding destination
        /// for an accepted User Agent Server (UAS) call leg. The intent is that functions
        /// can implement a form of a dialplan and pass to the B2BUA core.
        /// </summary>
        /// <param name="uas">A User Agent Server (UAS) transaction that has been accepted
        /// for forwarding.</param>
        /// <returns>A call descriptor for the User Agent Client (UAC) call leg that will
        /// be bridged to the UAS leg.</returns>
        public async Task<SIPCallDescriptor> Lookup(UASInviteTransaction uasTx, ISIPAccount from)
        {
            var result = await _dialPlanScriptRunner.Invoke(new SIPDialPlanGlobals { UasTx = uasTx, From = from });
            return result as SIPCallDescriptor;
        }

        private void DialPlanChanged(object sender, FileSystemEventArgs e)
        {
            if (DateTime.Now.Subtract(_dialPlanLastChange).TotalSeconds > 1)
            {
                _dialPlanLastChange = DateTime.Now;  // Prevent double re-loads. The file changed event fires twice when a file is saved.
                _logger.LogDebug($"Dial plan script file changed {_dialPlanPath}.");

                try
                {
                    CompileDialPlan(_dialPlanPath);
                }
                catch(Exception excp)
                {
                    _logger.LogWarning($"Exception compiling dial plan {excp.Message}.");
                }
            }
        }
    }
}
