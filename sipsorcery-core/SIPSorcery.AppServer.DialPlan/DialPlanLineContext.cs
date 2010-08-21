using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public class DialPlanLineContext : DialPlanContext
    {
        public const string DIALPLAN_LINESTART_KEY = "exten";
        public const char REGEX_DELIMITER_CHAR = '/';  

        internal List<DialPlanCommand> m_commands = new List<DialPlanCommand>();

        public DialPlanLineContext(
            SIPMonitorLogDelegate monitorLogDelegate,
            SIPTransport sipTransport,
            DialogueBridgeCreatedDelegate createBridge,
            SIPEndPoint outboundProxy,
            ISIPServerUserAgent sipServerUserAgent,
            SIPDialPlan dialPlan,
            List<SIPProvider> sipProviders,
            string traceDirectory,
            string callersNetworkId,
            Guid customerID)
            : base(monitorLogDelegate, sipTransport, createBridge, outboundProxy, sipServerUserAgent, dialPlan, sipProviders, traceDirectory, callersNetworkId, customerID)
         {
             ContextType = DialPlanContextsEnum.Line;
             string[] dialPlanEntries = dialPlan.DialPlanScript.Split(new char[] { '\n' });
             ParseDialPlan(dialPlanEntries);
         }

         private void ParseDialPlan(string[] dialPlanEntries)
         {
             try
             {
                 if (dialPlanEntries != null)
                 {
                     foreach (string line in dialPlanEntries)
                     {
                         if (line == null || line.Trim().Length == 0 || line.Trim().StartsWith(";"))
                         {
                             // Do nothing, blank or comment line.
                         }
                         else
                         {
                             if (line.Trim().StartsWith(DIALPLAN_LINESTART_KEY))
                             {
                                 // Split the line on the exten => data to get the data now that the line is recognised as a valid extension line.
                                 string op = Regex.Match(line, @"exten\s*(?<op>[=~>]+).+").Result("${op}");
                                 DialPlanOpsEnum opType = DialPlanOpTypes.GetDialPlanOp(op);
                                 string[] lineFields = Regex.Split(line, op);

                                 if (lineFields == null || lineFields.Length <= 1)
                                 {
                                     logger.Warn("Dial plan line was invalid ignored: " + line);
                                 }
                                 else
                                 {
                                     string dstPattern = null;
                                     int priority = 1;
                                     string app = null;

                                     if (opType == DialPlanOpsEnum.Regex)
                                     {
                                         // Check for regex delimiter characters before splitting up the command line. If the regex contains commas it needs to be extracted first.
                                         if (lineFields[1].Trim().StartsWith(REGEX_DELIMITER_CHAR.ToString()))
                                         {
                                             int endDelimIndex = lineFields[1].Trim().Substring(1).IndexOf(REGEX_DELIMITER_CHAR);
                                             dstPattern = lineFields[1].Trim().Substring(1, endDelimIndex);
                                             string[] dataFields = lineFields[1].Trim().Substring(endDelimIndex + 3).Split(new char[] { ',' }, 2);
                                             priority = Convert.ToInt32(dataFields[0]);
                                             app = dataFields[1];
                                         }
                                     }

                                     if (dstPattern == null)
                                     {
                                         string[] dataFields = lineFields[1].Trim().Split(new char[] { ',' }, 3);
                                         if (dataFields == null || dataFields.Length <= 2)
                                         {
                                             logger.Warn("Dial plan line was invalid ignored: " + line);
                                         }
                                         else
                                         {
                                             dstPattern = dataFields[0];
                                             priority = Convert.ToInt32(dataFields[1]);
                                             app = dataFields[2];
                                         }
                                     }

                                     if (dstPattern != null && app != null)
                                     {
                                         DialPlanCommand command = new DialPlanCommand(opType, dstPattern, priority, app, m_sipProviders);
                                         m_commands.Add(command);
                                     }
                                 }
                             }
                             else
                             {
                                 logger.Warn("Unknown dial plan command ignored: " + line);
                             }
                         }
                     }
                 }
             }
             catch (Exception excp)
             {
                 logger.Error("Exception ParseDialPlan. " + excp.Message);
                 throw excp;
             }
         }

         /// <summary>
         /// Decides on which dial plan line matches an incoming call request. The decision is made by 
         /// attempting to locate a match in the user's dial plan. Variable substitutions, all case insensitive:
         ///   - ${dst} and ${exten} will be replaced with request URI user, if ${exten:2} two chars will be trimmed from the start,
         ///   - ${fromname} will be replaced with the From header name,
         ///   - ${fromuriuser} will be replaced with the From header URI user.
         ///   
         /// Extension (or more correctly SIP URI user matching):
         ///   - If the pattern starts with an _ then Asterisk compaitble mode is used:
         ///     - X = any digit,
         ///     - Z = any digit 1 to 9,
         ///     - N = any digit 2 to 9.
         ///   - Otherwise the match is either an equality or regex match depending on the operator.
         /// </summary>
         /// <param name="sipRequest">The received call request.</param>
         /// <returns>A struct indicating where and how the call should be forwarded on.</returns>
         public DialPlanCommand GetDialPlanMatch(string callDestination)
         {
             try
             {
                 //LastUsed = DateTime.Now;

                 if (m_commands.Count > 0)
                 {
                     bool match = false;

                     #region Attempt to find a matching dial plan command for the call.

                     foreach (DialPlanCommand dialPlanCommand in m_commands)
                     {
                         switch (dialPlanCommand.Operation)
                         {
                             case DialPlanOpsEnum.Equals:
                                 // Two modes on the equals match, if pattern starts with an underscore use Asterisk compatible mode.

                                 if (dialPlanCommand.Destination.StartsWith("_"))
                                 {
                                     // X = \d.
                                     // Z = [1-9]
                                     // N = [2-9]
                                     string astPattern = dialPlanCommand.Destination.Substring(1);
                                     string dst = callDestination;
                                     int dstCharIndex = 0;
                                     int astCharIndex = 0;

                                     while (dst != null && dst.Length > dstCharIndex && astPattern != null && astPattern.Length > astCharIndex)
                                     {
                                         char astChar = astPattern.ToCharArray()[astCharIndex];
                                         string dstSubStr = dst.Substring(dstCharIndex, 1);

                                         if (astChar == '.')
                                         {
                                             match = true;
                                             break;
                                         }
                                         else if (astChar == 'x' || astChar == 'X')
                                         {
                                             if (Regex.Match(dstSubStr, @"[^\d]").Success)
                                             {
                                                 break;
                                             }
                                         }
                                         else if (astChar == 'z' || astChar == 'Z')
                                         {
                                             if (Regex.Match(dstSubStr, "[^1-9]").Success)
                                             {
                                                 break;
                                             }
                                         }
                                         else if (astChar == 'n' || astChar == 'N')
                                         {
                                             if (Regex.Match(dstSubStr, "[^2-9]").Success)
                                             {
                                                 break;
                                             }
                                         }
                                         else if (astChar == '[')
                                         {
                                             int closingBracketIndex = astPattern.Substring(astCharIndex).IndexOf(']');  // Find the next closing bracket after the starting one for this range.
                                             string range = astPattern.Substring(astCharIndex, closingBracketIndex + 1);
                                             if (!Regex.Match(dstSubStr, range).Success)
                                             {
                                                 break;
                                             }

                                             //  Move the pattern index up to the closing bracket.
                                             astCharIndex = astCharIndex + closingBracketIndex;
                                         }
                                         else if (astChar.ToString() != dstSubStr)
                                         {
                                             break;
                                         }

                                         if (dstCharIndex == dst.Length - 1 && astCharIndex == astPattern.Length - 1)
                                         {
                                             match = true;
                                             break;
                                         }
                                         else
                                         {
                                             dstCharIndex++;
                                             astCharIndex++;
                                         }
                                     }
                                 }
                                 else
                                 {
                                     match = (dialPlanCommand.Destination == callDestination);
                                 }
                                 break;
                             case DialPlanOpsEnum.Regex:
                                 match = Regex.Match(callDestination, dialPlanCommand.Destination).Success;
                                 break;
                             default:
                                 // No match.
                                 break;
                         }

                         if (match)
                         {
                             logger.Debug("Dial Plan Match for " + callDestination + " and " + dialPlanCommand.ToString());
                             return dialPlanCommand;
                         }
                     }

                     #endregion
                 }

                 return null;
             }
             catch (Exception excp)
             {
                 logger.Error("Exception GetDialPlanMatch. " + excp.Message);
                 throw excp;
             }
         }

         /// <summary>
         /// Used for incoming calls where an exact match is required on the sipswitch username.
         /// </summary>
         public DialPlanCommand GetDialPlanExactMatch(SIPRequest sipRequest)
         {
             try
             {
                 //LastUsed = DateTime.Now;

                 if (m_commands.Count > 0)
                 {
                     bool match = false;
                     foreach (DialPlanCommand dialPlanCommand in m_commands)
                     {
                         switch (dialPlanCommand.Operation)
                         {
                             case DialPlanOpsEnum.Equals:
                                 match = (dialPlanCommand.Destination == sipRequest.URI.User);
                                 break;

                             default:
                                 break;
                         }

                         if (match)
                         {
                             logger.Debug("Dial Plan Exact Match for " + sipRequest.URI.User + " and " + dialPlanCommand.ToString());
                             return dialPlanCommand;
                         }
                     }
                 }

                 return null;
             }
             catch (Exception excp)
             {
                 logger.Error("Exception GetDialPlanExactMatch. " + excp.Message);
                 throw excp;
             }
         }
    }
}
