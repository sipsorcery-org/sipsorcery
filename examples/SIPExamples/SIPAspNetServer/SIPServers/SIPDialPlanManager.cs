//-----------------------------------------------------------------------------
// Filename: SIPDialPlanManager.cs
//
// Description: This class loads and compiles dynamic dialplans. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 05 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
// 09 Jan 2021  Aaron Clauson   Load dialplan from database instead of filesystem.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using demo.DataAccess;

namespace demo
{
    public class SIPDialPlanGlobals
    {
        public UASInviteTransaction UasTx { get; set; }
        public ISIPAccount From { get; set; }
    }

    public class SIPDialPlanManager
    {
        public const string DEFAULT_DIALPLAN_NAME = "default";
        public const string DIAL_PLAN_CODE_TEMPLATE =
@"public static class DialPlanScript
{{
    public static SIPCallDescriptor Lookup(UASInviteTransaction uasTx, ISIPAccount from)
    {{
        {0}
    }}
}}";
        private readonly ILogger _logger = SIPSorcery.LogFactory.CreateLogger<SIPDialPlanManager>();

        private DateTime _dialplanLastUpdated = DateTime.MinValue;
        private SIPDialPlanDataLayer _sipDialPlanDataLayer;
        private ScriptRunner<Object> _dialPlanScriptRunner;

        public SIPDialPlanManager(IDbContextFactory<SIPAssetsDbContext> dbContextFactory)
        {
            _sipDialPlanDataLayer = new SIPDialPlanDataLayer(dbContextFactory);
        }

        public void LoadDialPlan()
        {
            var dialplan = _sipDialPlanDataLayer.Get(x => x.DialPlanName == DEFAULT_DIALPLAN_NAME);

            if (dialplan == null)
            {
                _logger.LogError($"SIP DialPlan Manager could not load the default dialplan. Ensure a dialplan with the name of \"{DEFAULT_DIALPLAN_NAME}\" exists.");
            }
            else
            {
                CompileDialPlan(dialplan);
            }
        }

        private void CompileDialPlan(SIPDialPlan dialplan)
        {
            try
            {
                string dialPlanClass = string.Format(DIAL_PLAN_CODE_TEMPLATE, dialplan.DialPlanScript);

                _logger.LogDebug($"Compiling dialplan...");

                DateTime startTime = DateTime.Now;

                _dialPlanScriptRunner = CSharpScript.Create(dialPlanClass,
                   ScriptOptions.Default
                   .WithImports("System", "SIPSorcery.SIP", "SIPSorcery.SIP.App")
                   .AddReferences(typeof(SIPSorcery.SIP.App.SIPCallDescriptor).GetTypeInfo().Assembly),
                   typeof(SIPDialPlanGlobals))
                   .ContinueWith("DialPlanScript.Lookup(UasTx, From)")
                   .CreateDelegate();

                var duration = DateTime.Now.Subtract(startTime);
                _logger.LogInformation($"SIP DialPlan Manger successfully compiled dialplan in {duration.TotalMilliseconds:0.##}ms.");
                _dialplanLastUpdated = dialplan.LastUpdate;
            }
            catch (Exception excp)
            {
                _logger.LogError($"SIP DialPlan Manger failed to compile dialplan. {excp.Message}");
            }
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
            if (_dialplanLastUpdated == DateTime.MinValue)
            {
                // Indicates the dialplan failed to load when the server started up.
                LoadDialPlan();
            }
            else
            {
                var dialplan = _sipDialPlanDataLayer.Get(x => x.DialPlanName == DEFAULT_DIALPLAN_NAME);

                if (dialplan == null)
                {
                    _logger.LogError($"SIP DialPlan Manager could not load the default dialplan. Ensure a dialplan with the name of \"{DEFAULT_DIALPLAN_NAME}\" exists.");
                }
                else if(dialplan.LastUpdate > _dialplanLastUpdated)
                {
                    _logger.LogInformation($"SIP DialPlan Manager loading updated dialplan.");
                    CompileDialPlan(dialplan);
                }
            }

            if (_dialPlanScriptRunner != null)
            {
                var result = await _dialPlanScriptRunner.Invoke(new SIPDialPlanGlobals { UasTx = uasTx, From = from });
                return result as SIPCallDescriptor;
            }
            else
            {
                return null;
            }
        }
    }
}
