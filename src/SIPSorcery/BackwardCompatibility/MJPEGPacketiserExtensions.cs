namespace SIPSorcery.net.RTP.Packetisation;

public static class MJPEGPacketiserExtensions
{
    extension(MJPEGPacketiser source)
    {
        public static MJPEGPacketiser.MJPEGData? GetFrameData(byte[] jpegFrame, out MJPEGPacketiser.MJPEG customData)
        {
            var frameData = MJPEGPacketiser.GetFrameData(jpegFrame);
            customData = frameData.customData;
            return frameData.frameData;
        }
    }
}
