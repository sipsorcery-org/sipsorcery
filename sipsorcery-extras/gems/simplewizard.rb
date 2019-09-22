def ProcessMatch(matchedRule)
	sys.Log("SimpleWizard matched on rule \"#{matchedRule.Description}\" with #{matchedRule.Pattern}, command #{matchedRule.Command}, #{matchedRule.RuleCommandDescription}.")
	case matchedRule.Command  
		when "Dial"
		    dialString = EvalDialString(matchedRule.CommandParameter1)
			#sys.Log("Dial string #{dialString}.")
			sys.Dial(dialString + "@" + matchedRule.CommandParameter2)
		when "DialAdvanced"
		    ringTime = matchedRule.CommandParameter2 != nil ? matchedRule.CommandParameter2.to_i : 0
			callDuration = matchedRule.CommandParameter3 != nil ? matchedRule.CommandParameter3.to_i : 0
			dialString = EvalDialString(matchedRule.CommandParameter1)
			#sys.Log("Dial string #{dialString}.")
			sys.Dial(dialString, ringTime, callDuration)
		when "Reject"
			sys.Respond(matchedRule.CommandParameter1.to_i, matchedRule.CommandParameter2)
		when "HighriseLookup"
			#crm.LookupHighriseContact(matchedRule.CommandParameter1,matchedRule.CommandParameter2, req.Header.From, false, true)
			result = crm.LookupHighriseContact(matchedRule.CommandParameter1,matchedRule.CommandParameter2, req.Header.From, matchedRule.CommandParameter3.match(/True/), matchedRule.CommandParameter4.match(/True/))
			if result != nil
				newDisplayName = result.PersonName
				newDisplayName += (result.CompanyName != nil) ? ", " + result.CompanyName : ""
				sys.Log("Setting From header display name to " + newDisplayName + ".")
				sys.SetFromHeader(newDisplayName, nil, nil)
				sys.SetCallerCRMDetails(result)
			else
				sys.Log("No match found for Highrise lookup.")
			end
		else
			sys.Log("Error command #{matchedRule.Command} not recognised.")
	end
end

# Checks a Dial command parameter for the a particular format and if found does some Ruby pre-processing.
def EvalDialString(dialString)
  if dialString =~ /^#\{.*\}$/
    #sys.Log(dialString[2..-2])
	res = eval dialString[2..-2].to_s
	#sys.Log("Result #{res}.")
    return res
  else
    return dialString
  end
end

sys.Log("SimpleWizard processing commenced.")
if sys.Out
	rules = lookup.GetSimpleWizardRules(sys.DialPlanName, "Out")
	rules.each{|rule| 
		#sys.Log("#{rule.Pattern} #{rule.Command} #{rule.RuleCommandDescription}")
		if rule.PatternType == "Exact" && rule.Pattern == req.URI.User
			ProcessMatch(rule)
		elsif rule.PatternType == "Prefix"
		    prefixPattern = "^" + rule.Pattern.TrimStart('_')
			prefixPattern = prefixPattern.gsub("X", "[0-9]")
			prefixPattern = prefixPattern.gsub("Z", "[1-9]")
			prefixPattern = prefixPattern.gsub("N", "[2-9]")
			prefixPattern = prefixPattern.gsub("*", "\\*")
			prefixPattern = prefixPattern.gsub("+", "\\+")
			#sys.Log("prefixPattern=#{prefixPattern}.")
		    if req.URI.User =~ /#{prefixPattern}/
			  ProcessMatch(rule)
			end
		elsif rule.PatternType == "Regex" && req.URI.User =~ /#{rule.Pattern}/
			ProcessMatch(rule)
		end
	}
else
    toAccount = req.URI.UnescapedUser + "@" + sys.GetCanonicalDomain(req.URI.Host)
	#sys.Log("Incoming rule, FromName=#{req.Header.From.FromName}, FromURI=#{req.Header.From.FromURI.ToParameterlessString()} to #{toAccount}.")
	rules = lookup.GetSimpleWizardRules(sys.DialPlanName, "In")
	rules.each{|rule| 
		#sys.Log("#{rule.Pattern} #{rule.Command} #{rule.RuleCommandDescription}")
		if ( ( rule.ToMatchType == "Any" ||
		       (rule.ToMatchType == "ToSIPAccount" && rule.ToMatchParameter == toAccount) ||
			   (rule.ToMatchType == "ToSIPProvider" && rule.ToMatchParameter == toAccount.sub(/\.[^\.]*@.*$/, "").sub(/^.*\./, "")) ||
			   (rule.ToMatchType == "Regex" && toAccount =~ /#{rule.ToMatchParameter}/)
			  ) &&
		   (rule.TimePattern == nil || rule.IsTimeMatch(System::DateTimeOffset.UtcNow, sys.GetTimezone)) &&
		   (rule.Pattern == nil || req.Header.From.FromName =~ /#{rule.Pattern}/ || req.Header.From.FromURI.ToParameterlessString() =~ /#{rule.Pattern}/)) then
		     ProcessMatch(rule)
		end
	}
end
