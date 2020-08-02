namespace ProjectCeilidh.PortAudio.Native
{
    /// <summary>
    /// Unchanging unique identifiers for each supported host API. This type is used in the <see cref="PaHostApiInfo"/> structure.
    /// The values are guaranteed to be unique and to never change, thus allowing code to be written that conditionally uses host API-specific extensions.
    /// New type ids will be allocated when support for a host API reaches "public alpha" status, prior to that developers should use the <see cref="InDevelopment"/> type id.
    /// </summary>
    internal enum PaHostApiTypeId
    {
        InDevelopment = 0,
        DirectSound = 1,
        Mme = 2,
        Asio = 3,
        SoundManager = 4,
        CoreAudio = 5,
        Oss = 6,
        Alsa = 8,
        Al = 9,
        BeOs = 10,
        Wdmks = 11,
        Jack = 12,
        Wasapi = 13,
        AudioScienceHpi = 14
    }
}
