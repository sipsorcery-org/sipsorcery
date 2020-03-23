namespace SIPSorcery.Net.UnitTests

{
    /// <summary>A factory for SIPSorcery.Net.KeyParameter instances</summary>
    public static partial class KeyParameterFactory
    {
        /// <summary>A factory for SIPSorcery.Net.KeyParameter instances</summary>
        public static SDPSecurityDescription.KeyParameter Create(
            string key_s,
            string salt_s1
        )
        {
            SDPSecurityDescription.KeyParameter keyParameter = new SDPSecurityDescription.KeyParameter(key_s, salt_s1);
            return keyParameter;
        }
    }
}
