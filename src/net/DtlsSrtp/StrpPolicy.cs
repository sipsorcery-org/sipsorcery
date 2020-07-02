namespace Org.BouncyCastle.Crypto.DtlsSrtp
{
    public class SrtpPolicy
    {
        public const int NULL_ENCRYPTION = 0;
        public const int AESCM_ENCRYPTION = 1;
        public const int TWOFISH_ENCRYPTION = 3;
        public const int AESF8_ENCRYPTION = 2;
        public const int TWOFISHF8_ENCRYPTION = 4;
        public const int NULL_AUTHENTICATION = 0;
        public const int HMACSHA1_AUTHENTICATION = 1;
        public const int SKEIN_AUTHENTICATION = 2;

        private int encType;
        private int encKeyLength;
        private int authType;
        private int authKeyLength;
        private int authTagLength;
        private int saltKeyLength;

        public int AuthKeyLength { get => authKeyLength; set => authKeyLength = value; }
        public int AuthTagLength { get => authTagLength; set => authTagLength = value; }
        public int AuthType { get => authType; set => authType = value; }
        public int EncKeyLength { get => encKeyLength; set => encKeyLength = value; }
        public int EncType { get => encType; set => encType = value; }
        public int SaltKeyLength { get => saltKeyLength; set => saltKeyLength = value; }

        /**
         * Construct a SRTPPolicy object based on given parameters.
         * This class acts as a storage class, so all the parameters are passed in
         * through this constructor.
         *
         * @param encType SRTP encryption type
         * @param encKeyLength SRTP encryption key length
         * @param authType SRTP authentication type
         * @param authKeyLength SRTP authentication key length
         * @param authTagLength SRTP authentication tag length
         * @param saltKeyLength SRTP salt key length
         */
        public SrtpPolicy(int encType,
                          int encKeyLength,
                          int authType,
                          int authKeyLength,
                          int authTagLength,
                          int saltKeyLength)
        {
            this.encType = encType;
            this.encKeyLength = encKeyLength;
            this.authType = authType;
            this.authKeyLength = authKeyLength;
            this.authTagLength = authTagLength;
            this.saltKeyLength = saltKeyLength;
        }
    }
}