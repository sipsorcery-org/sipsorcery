using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.SIP.App;

namespace SIPSorcery.AppServer.DialPlan
{
    public enum DialPlanOpsEnum
    {
        Unknown = 0,
        Equals = 1,
        Regex = 3,
    }

    public class DialPlanOpTypes
    {
        public static DialPlanOpsEnum GetDialPlanOp(string dialPlanOp)
        {
            if (dialPlanOp == "=" || dialPlanOp == "==" || dialPlanOp == "=>")
            {
                return DialPlanOpsEnum.Equals;
            }
            else if (dialPlanOp == "=~")
            {
                return DialPlanOpsEnum.Regex;
            }
            else
            {
                return DialPlanOpsEnum.Unknown;
            }
        }
    }

    public class DialPlanCommand
    {
        public string Destination;
        public int Priority;
        public DialPlanOpsEnum Operation;
        public string Command;
        public string Data;
        public List<SIPProvider> SIPProviders = new List<SIPProvider>();

        public DialPlanCommand(DialPlanOpsEnum opType, string dst, int priority, string command, List<SIPProvider> sipProviders)
        {
            Operation = opType;
            Destination = dst;
            Priority = priority;
            SIPProviders = sipProviders;

            int startBracketIndex = command.IndexOf("(");
            int endBracketIndex = command.IndexOf(")");
            int dataLength = endBracketIndex - startBracketIndex - 1;

            Command = command.Substring(0, startBracketIndex);
            Data = command.Substring(startBracketIndex + 1, dataLength);
        }

        public override string ToString()
        {
            string opTypeStr = (Operation == DialPlanOpsEnum.Regex) ? "=~" : "=";
            return DialPlanLineContext.DIALPLAN_LINESTART_KEY + opTypeStr + " " + Destination + "," + Priority + "," + Command + "(" + Data + ")";
        }
    }
}
