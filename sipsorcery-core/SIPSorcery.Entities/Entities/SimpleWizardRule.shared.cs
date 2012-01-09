using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.SIP;

#if !SILVERLIGHT
using SIPSorcery.Sys;
#endif

namespace SIPSorcery.Entities
{
    public partial class SimpleWizardRule
    {
        private const string TIMES_REGEX = @";(?<startHour>\d+):(?<startMinute>\d+)-(?<endHour>\d+):(?<endMinute>\d+)";

        public SimpleWizardCommandTypes CommandType
        {
            get
            {
                if (this.Command != null)
                {
                    return (SimpleWizardCommandTypes)Enum.Parse(typeof(SimpleWizardCommandTypes), this.Command.Replace(" ", String.Empty), true);
                }

                return SimpleWizardCommandTypes.None;
            }
        }

        public SimpleWizardToMatchTypes SimpleWizardToMatchType
        {
            get
            {
                if (this.ToMatchType != null && this.ToMatchType.Trim().Length > 0)
                {
                    return (SimpleWizardToMatchTypes)Enum.Parse(typeof(SimpleWizardToMatchTypes), this.ToMatchType.Replace(" ", String.Empty), true);
                }

                return SimpleWizardToMatchTypes.Any;
            }
        }

        public string RuleCommandDescription
        {
            get 
            {
                if (CommandType == SimpleWizardCommandTypes.Dial)
                {
                    return CommandParameter1 + "@" + CommandParameter2;
                }
                else if (CommandType == SimpleWizardCommandTypes.DialAdvanced)
                {
                    string dialString = CommandParameter1;

                    if (CommandParameter2 != null && CommandParameter2.Trim().Length > 0)
                    {
                        dialString += "," + ((CommandParameter2 != null && CommandParameter2.Trim().Length > 0) ? CommandParameter2 : "0");
                    }

                    if (CommandParameter3 != null && CommandParameter3.Trim().Length > 0)
                    {
                        dialString += (CommandParameter2 != null && CommandParameter2.Trim().Length > 0) ? String.Empty : ",0";
                        dialString += "," + ((CommandParameter3 != null && CommandParameter3.Trim().Length > 0) ? CommandParameter3 : "0");
                    }

                    return  dialString;
                }
                else if (CommandType == SimpleWizardCommandTypes.Reject)
                {
                    if (CommandParameter2 == null)
                    {
                        var statusCode = SIPResponseStatusCodes.GetStatusTypeForCode(Convert.ToInt32(CommandParameter1));
                        return CommandParameter1 + " " + statusCode.ToString();
                    }
                    else
                    {
                        return CommandParameter1 + " " + CommandParameter2;
                    }
                }
                else if (CommandType == SimpleWizardCommandTypes.HighriseLookup)
                {
                    return CommandParameter1;
                }
                else
                {
                    return CommandParameter1;
                }
            }
        }

        /// <summary>
        /// For an incoming rule the description of how the To match is being handled. The options are Any (i.e. all calls will be matched),
        /// a specific SIP account or a specific provider.
        /// </summary>
        public string ToDescription
        {
            get
            {
                if (ToSIPAccount != null)
                {
                    return ToSIPAccount;
                }
                else if (ToProvider != null)
                {
                    return ToProvider;
                }
                else
                {
                    return "Any";
                }
            }
        }

        /// <summary>
        /// Gets a list of the days that the rule's time pattern matches.
        /// </summary>
        /// <returns>A list of matching days.</returns>
        public List<DayOfWeek> MatchedDays()
        {
            var days = new List<DayOfWeek>();

            if (TimePattern.IndexOf("M", StringComparison.OrdinalIgnoreCase) != -1)
            {
                days.Add(DayOfWeek.Monday);
            }
            
            if (TimePattern.IndexOf("Tu", StringComparison.OrdinalIgnoreCase) != -1)
            {
                days.Add(DayOfWeek.Tuesday);
            }
            
            if (TimePattern.IndexOf("W", StringComparison.OrdinalIgnoreCase) != -1)
            {
                days.Add(DayOfWeek.Wednesday);
            }
            
            if (TimePattern.IndexOf("Th", StringComparison.OrdinalIgnoreCase) != -1)
            {
                days.Add(DayOfWeek.Thursday);
            }
            
            if (TimePattern.IndexOf("F", StringComparison.OrdinalIgnoreCase) != -1)
            {
                days.Add(DayOfWeek.Friday);
            }
            
            if (TimePattern.IndexOf("Sa", StringComparison.OrdinalIgnoreCase) != -1)
            {
                days.Add(DayOfWeek.Saturday);
            }
            
            if (TimePattern.IndexOf("Su", StringComparison.OrdinalIgnoreCase) != -1)
            {
                days.Add(DayOfWeek.Sunday);
            }

            return days;
        }

        public int GetStartHour()
        {
            var timesMatch = Regex.Match(TimePattern, TIMES_REGEX);
            if(timesMatch.Success)
            {
                return Convert.ToInt32(timesMatch.Result("${startHour}"));
            }

            return 0;
        }

        public int GetStartMinute()
        {
            var timesMatch = Regex.Match(TimePattern, TIMES_REGEX);
            if(timesMatch.Success)
            {
                return Convert.ToInt32(timesMatch.Result("${startMinute}"));
            }

            return 0;
        }

        public int GetEndHour()
        {
            var timesMatch = Regex.Match(TimePattern, TIMES_REGEX);
            if(timesMatch.Success)
            {
                return Convert.ToInt32(timesMatch.Result("${endHour}"));
            }

            return 0;
        }

        public int GetEndMinute()
        {
            var timesMatch = Regex.Match(TimePattern, TIMES_REGEX);
            if (timesMatch.Success)
            {
                return Convert.ToInt32(timesMatch.Result("${endMinute}"));
            }

            return 0;
        }
        
#if !SILVERLIGHT

        /// <summary>
        /// Determines if the current rule should be applied depending on the time pattern that has been applied to it.
        /// </summary>
        /// <param name="timezone">The timezone to adjust for. This will typically be the timezone set by the user that owns
        /// the dialplan. If no timezone is specified UTC will be assumed.</param>
        /// <returns>True if the rule should be applied, false otherwise.</returns>
        public bool IsTimeMatch(DateTimeOffset now, string timezone)
        {
            var localTime = now.AddMinutes(TimeZoneHelper.GetTimeZonesUTCOffsetMinutes(timezone));

            if (localTime.DayOfWeek == DayOfWeek.Monday && TimePattern.IndexOf("M", StringComparison.OrdinalIgnoreCase) == -1)
            {
                return false;
            }
            else if (localTime.DayOfWeek == DayOfWeek.Tuesday && TimePattern.IndexOf("Tu", StringComparison.OrdinalIgnoreCase) == -1)
            {
                return false;
            }
            else if (localTime.DayOfWeek == DayOfWeek.Wednesday && TimePattern.IndexOf("W", StringComparison.OrdinalIgnoreCase) == -1)
            {
                return false;
            }
            else if (localTime.DayOfWeek == DayOfWeek.Thursday && TimePattern.IndexOf("Th", StringComparison.OrdinalIgnoreCase) == -1)
            {
                return false;
            }
            else if (localTime.DayOfWeek == DayOfWeek.Friday && TimePattern.IndexOf("F", StringComparison.OrdinalIgnoreCase) == -1)
            {
                return false;
            }
            else if (localTime.DayOfWeek == DayOfWeek.Saturday && TimePattern.IndexOf("Sa", StringComparison.OrdinalIgnoreCase) == -1)
            {
                return false;
            }
            else if (localTime.DayOfWeek == DayOfWeek.Sunday && TimePattern.IndexOf("Su", StringComparison.OrdinalIgnoreCase) == -1)
            {
                return false;
            }

            var timesMatch = Regex.Match(TimePattern, TIMES_REGEX);
            if(timesMatch.Success)
            {
                int startHour = Convert.ToInt32(timesMatch.Result("${startHour}"));
                int startMinute = Convert.ToInt32(timesMatch.Result("${startMinute}"));
                int endHour = Convert.ToInt32(timesMatch.Result("${endHour}"));
                int endMinute = Convert.ToInt32(timesMatch.Result("${endMinute}"));

                TimeSpan startTime = new TimeSpan(startHour, startMinute, 0);
                TimeSpan endTime = new TimeSpan(endHour, endMinute, 0);

                if (localTime.TimeOfDay < startTime || localTime.TimeOfDay > endTime)
                {
                    return false;
                }
            }

            return true;
        }

#endif

    }
}
