namespace SIPSorcery.Net.UnitTests
{
    /// <summary>A factory for SIPSorcery.Net.SessionParameter instances</summary>
    public static partial class SessionParameterFactory
    {
        /// <summary>A factory for SIPSorcery.Net.SessionParameter instances</summary>
        public static SDPSecurityDescription.SessionParameter Create(
            SDPSecurityDescription.SessionParameter.SrtpSessionParams param
        )
        {
            SDPSecurityDescription.SessionParameter SessionParameter = new SDPSecurityDescription.SessionParameter(param);
            return SessionParameter;
        }

        public static SDPSecurityDescription.SessionParameter Create(
            SDPSecurityDescription.SessionParameter.SrtpSessionParams param, uint paramValue
        )
        {
            SDPSecurityDescription.SessionParameter SessionParameter = new SDPSecurityDescription.SessionParameter(param);
            switch (param)
            {
                case SDPSecurityDescription.SessionParameter.SrtpSessionParams.kdr:
                    SessionParameter.Kdr = paramValue;
                    break;
                case SDPSecurityDescription.SessionParameter.SrtpSessionParams.wsh:
                    SessionParameter.Wsh = paramValue;
                    break;
                case SDPSecurityDescription.SessionParameter.SrtpSessionParams.fec_order:
                    SessionParameter.FecOrder = (SDPSecurityDescription.SessionParameter.FecTypes)paramValue;
                    break;
            }
            return SessionParameter;
        }
    }
}
