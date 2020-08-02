namespace ProjectCeilidh.PortAudio
{
    /// <summary>
    /// The type of host API
    /// </summary>
    public enum PortAudioHostApiType
    {
        /// <summary>
        /// DirectSound
        /// </summary>
        DirectSound = 1,
        /// <summary>
        /// MME
        /// </summary>
        Mme = 2,
        /// <summary>
        /// ASIO
        /// </summary>
        Asio = 3,
        /// <summary>
        /// SoundManager
        /// </summary>
        SoundManager = 4,
        /// <summary>
        /// CoreAudio
        /// </summary>
        CoreAudio = 5,
        /// <summary>
        /// OSS
        /// </summary>
        Oss = 6,
        /// <summary>
        /// ALSA
        /// </summary>
        Alsa = 8,
        /// <summary>
        /// AL
        /// </summary>
        Al = 9,
        /// <summary>
        /// BeOS
        /// </summary>
        BeOs = 10,
        /// <summary>
        /// WDM/KS
        /// </summary>
        Wdmks = 11,
        /// <summary>
        /// Jack
        /// </summary>
        Jack = 12,
        /// <summary>
        /// WASAPI
        /// </summary>
        Wasapi = 13,
        /// <summary>
        /// AudioScienceHPI
        /// </summary>
        AudioScienceHpi = 14
    }
}