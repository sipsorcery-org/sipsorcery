using SIPSorcery.Net;
using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.net.RTP.RTPHeaderExtensions
{
    // CVO (Coordination of Video Orientation) is a extension payload format in  https://www.3gpp.org/ftp/Specs/archive/26_series/26.114/26114-i70.zip

    // Code reference:
    //  - https://chromium.googlesource.com/external/webrtc/+/e2a017725570ead5946a4ca8235af27470ca0df9/webrtc/modules/rtp_rtcp/source/rtp_header_extensions.cc#134
    //  - https://chromium.googlesource.com/external/webrtc/+/e2a017725570ead5946a4ca8235af27470ca0df9/webrtc/modules/rtp_rtcp/include/rtp_cvo.h

    public class CVOExtension: RTPHeaderExtension
    {
        public const string RTP_HEADER_EXTENSION_URI    = "urn:3gpp:video-orientation";

        private VideoRotation rotation = VideoRotation.kVideoRotation_0;

        public CVOExtension(int id) : base(id, RTP_HEADER_EXTENSION_URI, RTPHeaderExtensionType.OneByte, Net.SDPMediaTypesEnum.video)
        {
        }

        public override byte[] WriteHeader()
        {
            return null;
        }

        public override void ReadHeader(ref MediaStreamTrack localTrack, ref MediaStreamTrack remoteTrack, RTPHeader header, byte[] data)
        {
            if ( (data != null) && data.Length > 0)
            {
                var currentRotation = ConvertCVOByteToVideoRotation((uint)data[0]);
                if(currentRotation != rotation)
                {
                    rotation = currentRotation;
                    // TODO - need to trigger event
                }
            }
            
        }

        // enum for clockwise rotation.
        public enum VideoRotation
        {
            kVideoRotation_0 = 0,
            kVideoRotation_90 = 90,
            kVideoRotation_180 = 180,
            kVideoRotation_270 = 270
        };

        static uint ConvertVideoRotationToCVOByte(VideoRotation rotation)
        {
            switch (rotation)
            {
                case VideoRotation.kVideoRotation_0:
                    return 0;
                case VideoRotation.kVideoRotation_90:
                    return 1;
                case VideoRotation.kVideoRotation_180:
                    return 2;
                case VideoRotation.kVideoRotation_270:
                    return 3;
                default:
                    return 0;
            }
        }

        static VideoRotation ConvertCVOByteToVideoRotation(uint cvo_byte)
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

            uint rotation_bits = cvo_byte & 0x3;
            switch (rotation_bits)
            {
                case 0:
                    return VideoRotation.kVideoRotation_0;
                case 1:
                    return VideoRotation.kVideoRotation_90;
                case 2:
                    return VideoRotation.kVideoRotation_180;
                case 3:
                    return VideoRotation.kVideoRotation_270;
                default:
                    return VideoRotation.kVideoRotation_0;
            }
        }
    }
}
