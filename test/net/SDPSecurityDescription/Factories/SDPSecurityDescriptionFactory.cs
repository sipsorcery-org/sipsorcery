namespace SIPSorcery.Net.UnitTests
{
    /// <summary>A factory for SIPSorcery.Net.SDPSecurityDescription instances</summary>
    public static partial class SDPSecurityDescriptionFactory
    {
        /// <summary>A factory for SIPSorcery.Net.SDPSecurityDescription instances</summary>
        public static SDPSecurityDescription Create(uint tag_u, SDPSecurityDescription.CryptoSuites crypto_i)
        {
            SDPSecurityDescription sDPSecurityDescription
               = new SDPSecurityDescription(tag_u, crypto_i);

            return sDPSecurityDescription;

            // TODO: Edit factory method of SDPSecurityDescription
            // This method should be able to configure the object in all possible ways.
            // Add as many parameters as needed,
            // and assign their values to each field by using the API.
        }
    }
}
