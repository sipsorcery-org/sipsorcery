using SIPSorcery.Net;
using System;

namespace SIPSorcery.net.RTP.RTPHeaderExtensions
{
    // CVO (Coordination of Video Orientation) is a extension payload format in https://www.3gpp.org/ftp/Specs/archive/26_series/26.114/26114-i70.zip

    // Code reference:
    //  - https://chromium.googlesource.com/external/webrtc/+/e2a017725570ead5946a4ca8235af27470ca0df9/webrtc/modules/rtp_rtcp/source/rtp_header_extensions.cc#134
    //  - https://chromium.googlesource.com/external/webrtc/+/e2a017725570ead5946a4ca8235af27470ca0df9/webrtc/modules/rtp_rtcp/include/rtp_cvo.h

    public class CVOExtension: RTPHeaderExtension
    {
        public const string RTP_HEADER_EXTENSION_URI    = "urn:3gpp:video-orientation";
        public const int RTP_HEADER_EXTENSION_SIZE = 1;

        public event Action<VideoRotation> OnVideoRotationChange;

        private byte _rotation = 0; // Default rotation is 0 <=> VideoRotation.CW_0

        public CVOExtension(int id) : base(id, RTP_HEADER_EXTENSION_URI, RTP_HEADER_EXTENSION_SIZE, RTPHeaderExtensionType.OneByte, Net.SDPMediaTypesEnum.video)
        {
        }

        /// <summary>
        /// To set a new rotation
        /// </summary>
        /// <param name="videoRotation"><see cref="VideoRotation"/></param>
        public void SetRotation(VideoRotation videoRotation)
        {
            SetRotation(ConvertVideoRotationToCVOByte(videoRotation));
        }

        /// <summary>
        /// To set a new rotation
        /// </summary>
        /// <param name="rotation"><see cref="byte"/></param>
        public void SetRotation(byte rotation)
        {
            if (rotation != _rotation)
            {
                _rotation = rotation;

                // Trigger event
                var videoRotation = ConvertCVOByteToVideoRotation(_rotation);
                OnVideoRotationChange?.Invoke(videoRotation);
            }
        }

        public override byte[] Marshal()
        {
            return new[]
            {
                (byte)((Id << 4) | ExtensionSize - 1),
                _rotation
            };
        }

        public override void Unmarshal(ref MediaStreamTrack localTrack, ref MediaStreamTrack remoteTrack, RTPHeader header, byte[] data)
        {
            if (data.Length == ExtensionSize)
            {
                SetRotation(data[0]);
            }
        }

        /// <summary>
        /// Enum for clockwise (CW) rotation in degree.
        /// </summary>
        public enum VideoRotation
        {
            CW_0 = 0,
            CW_90 = 90,
            CW_180 = 180,
            CW_270 = 270
        };

        static byte ConvertVideoRotationToCVOByte(VideoRotation rotation)
        {
            switch (rotation)
            {
                case VideoRotation.CW_0:
                    return 0;
                case VideoRotation.CW_90:
                    return 1;
                case VideoRotation.CW_180:
                    return 2;
                case VideoRotation.CW_270:
                    return 3;
                default:
                    return 0;
            }
        }

        static VideoRotation ConvertCVOByteToVideoRotation(byte cvo_byte)
        {
            /* CVO byte: |0 0 0 0 C F R1 R0|
                With the following definitions: 

                C = Camera: indicates the direction of the camera used for this video stream. It can be used by the MTSI client in 
                receiver to e.g. display the received video differently depending on the source camera. 
                    0: Front-facing camera, facing the user. If camera direction is unknown by the sending MTSI client in the terminal then this is the default value used. 
                    1: Back-facing camera, facing away from the user. 
                
                F = Flip: indicates a horizontal (left-right flip) mirror operation on the video as sent on the link. 
                    0: No flip operation. If the sending MTSI client in terminal does not know if a horizontal mirror operation is necessary, then this is the default value used. 
                    1: Horizontal flip operation 

                R1, R0 = Rotation: indicates the rotation of the video as transmitted on the link.
                    0, 0 =   0° rotation                            => needs   0° CW rotation
                    0, 1 =  90° Counter Clockwise (CCW) rotation    => needs  90° CW rotation 
                    1, 0 = 180° CCW rotation                        => needs 180° CW rotation
                    1, 1 = 270° CCW rotation                        => needs 270° CW rotation
             */

            uint rotation_bits = (uint)cvo_byte & 0x3;
            switch (rotation_bits)
            {
                case 0:
                    return VideoRotation.CW_0;
                case 1:
                    return VideoRotation.CW_90;
                case 2:
                    return VideoRotation.CW_180;
                case 3:
                    return VideoRotation.CW_270;
                default:
                    return VideoRotation.CW_0;
            }
        }
    }
}
