using System;

namespace SIPSorcery.SIP.App.Media
{
    public class MediaState
    {
        private bool _remoteOnHold;

        public virtual bool LocalOnHold { get; set; }

        public virtual bool RemoteOnHold
        {
            get => _remoteOnHold;
            set
            {
                if (_remoteOnHold != value)
                {
                    RemoteOnHoldChanged?.Invoke(value);
                    _remoteOnHold = value;
                }
            }
        }

        public event Action<bool> RemoteOnHoldChanged;
    }
}