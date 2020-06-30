//-----------------------------------------------------------------------------
// Filename: RTCPFeedback.cs
//
// Description:
//
//        RTCP Feedback Packet
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// header |V=2|P|    RC   |   PT=SR=200   |             length            |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                  SSRC of packet sender                        |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                  SSRC of media source                         |
// info   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        :            Feedback Control Information(FCI)                  :
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// Author(s):
// TeraBitSoftware
// 
// History:
// 29 Jun 2020  TeraBitSoftware     Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// The different types of Feedback Message Types. (RFC4585)
    /// https://tools.ietf.org/html/rfc4585#page-35
    /// </summary>
    public enum RTCPFeedbackTypesEnum : int
    {
        unassigned = 0,     // Unassigned
        NACK = 1,   		// Generic NACK	Generic negative acknowledgment		    [RFC4585]
        // reserved = 2		// Reserved												[RFC5104]
        TMMBR = 3, 			// Temporary Maximum Media Stream Bit Rate Request		[RFC5104]
        TMMBN = 4,			// Temporary Maximum Media Stream Bit Rate Notification	[RFC5104]
        RTCP_SR_REQ = 5, 	// RTCP Rapid Resynchronisation Request					[RFC6051]
        RAMS = 6,			// Rapid Acquisition of Multicast Sessions				[RFC6285]
        TLLEI = 7, 			// Transport-Layer Third-Party Loss Early Indication	[RFC6642]
        RTCP_ECN_FB = 8,	// RTCP ECN Feedback 									[RFC6679]
        PAUSE_RESUME = 9,   // Media Pause/Resume									[RFC7728]

        DBI = 10			// Delay Budget Information (DBI) [3GPP TS 26.114 v16.3.0][Ozgur_Oyman]
        // 11-30			// Unassigned	
        // Extension = 31	// Reserved for future extensions						[RFC4585]
    }

    /// <summary>
    /// The different types of Feedback Message Types. (RFC4585)
    /// https://tools.ietf.org/html/rfc4585#page-35
    /// </summary>
    public enum PSFBFeedbackTypesEnum : byte
    {
        unassigned = 0,     // Unassigned
        PLI = 1,            // Picture Loss Indication                              [RFC4585]
        SLI = 2,            // Slice Loss Indication   [RFC4585]
        RPSI = 3,           // Reference Picture Selection Indication  [RFC4585]
        FIR = 4,            // Full Intra Request Command  [RFC5104]
        TSTR = 5,           // Temporal-Spatial Trade-off Request  [RFC5104]
        TSTN = 6,           // Temporal-Spatial Trade-off Notification [RFC5104]
        VBCM = 7,           // Video Back Channel Message  [RFC5104]
        PSLEI = 8,          // Payload-Specific Third-Party Loss Early Indication  [RFC6642]
        ROI = 9,            // Video region-of-interest (ROI)	[3GPP TS 26.114 v16.3.0][Ozgur_Oyman]
        LRR = 10,           // Layer Refresh Request Command   [RFC-ietf-avtext-lrr-07]
        // 11-14		    // Unassigned	
        AFB = 15            // Application Layer Feedback  [RFC4585]
        // 16-30		    // Unassigned	
        // Extension = 31   //Extension   Reserved for future extensions  [RFC4585]
    }

    public enum FeedbackProtocol
    {
        RTCP = 0,
        PSFB = 1
    }

    public class RTCPFeedback
    {
        public int SENDER_PAYLOAD_SIZE = 20;
        public int MIN_PACKET_SIZE = 0;

        public RTCPHeader Header;
        public uint SenderSSRC; // Packet Sender
        public uint MediaSSRC;
        public ushort PID; // Packet ID (PID): 16 bits to specify a lost packet, the RTP sequence number of the lost packet.
        public ushort BLP; // bitmask of following lost packets (BLP): 16 bits
        public uint FCI; // Feedback Control Information (FCI)  

        public RTCPFeedback(uint ssrc, RTCPFeedbackTypesEnum feedbackMessageType, ushort sequenceNo, ushort bitMask)
        {
            Header = new RTCPHeader(feedbackMessageType);
            SENDER_PAYLOAD_SIZE = 12;
            MIN_PACKET_SIZE = RTCPHeader.HEADER_BYTES_LENGTH + SENDER_PAYLOAD_SIZE;
            SenderSSRC = ssrc;
            PID = sequenceNo;
            BLP = bitMask;
        }

        /// <summary>
        /// Constructor for RTP feedback reports that do not require any additional feedback control
        /// indication parameters (e.g. RTCP Rapid Resynchronisation Request).
        /// </summary>
        /// <param name="feedbackMessageType">The payload specific feedback type.</param>
        public RTCPFeedback(uint senderSsrc, uint mediaSsrc, RTCPFeedbackTypesEnum feedbackMessageType)
        {
            Header = new RTCPHeader(feedbackMessageType);
            SenderSSRC = senderSsrc;
            MediaSSRC = mediaSsrc;
            SENDER_PAYLOAD_SIZE = 8;
        }

        /// <summary>
        /// Constructor for payload feedback reports that do not require any additional feedback control
        /// indication parameters (e.g. Picture Loss Indication reports).
        /// </summary>
        /// <param name="feedbackMessageType">The payload specific feedback type.</param>
        public RTCPFeedback(uint senderSsrc, uint mediaSsrc, PSFBFeedbackTypesEnum feedbackMessageType)
        {
            Header = new RTCPHeader(feedbackMessageType);
            SenderSSRC = senderSsrc;
            MediaSSRC = mediaSsrc;
            SENDER_PAYLOAD_SIZE = 8;
        }

        /// <summary>
        /// Create a new RTCP Report from a serialised byte array.
        /// </summary>
        /// <param name="packet">The byte array holding the serialised feedback report.</param>
        public RTCPFeedback(byte[] packet)
        {
            Header = new RTCPHeader(packet);

            if (BitConverter.IsLittleEndian)
            {
                SenderSSRC = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 4));
                MediaSSRC = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 8));
            }
            else
            {
                SenderSSRC = BitConverter.ToUInt32(packet, 4);
                MediaSSRC = BitConverter.ToUInt32(packet, 8);
            }

            // TODO: Depending on the report type additional parameters will need to be deserialised.
        }

        public byte[] GetBytes()
        {
            byte[] buffer = new byte[RTCPHeader.HEADER_BYTES_LENGTH + SENDER_PAYLOAD_SIZE];
            Header.SetLength((ushort)(buffer.Length / 4 - 1));

            Buffer.BlockCopy(Header.GetBytes(), 0, buffer, 0, RTCPHeader.HEADER_BYTES_LENGTH);
            int payloadIndex = RTCPHeader.HEADER_BYTES_LENGTH;

            // All feedback packets require the Sender and Media SSRC's to be set.
            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SenderSSRC)), 0, buffer, payloadIndex, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(MediaSSRC)), 0, buffer, payloadIndex + 4, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(SenderSSRC), 0, buffer, payloadIndex, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(MediaSSRC), 0, buffer, payloadIndex + 4, 4);
            }

            switch (Header)
            {
                case var x when x.PacketType == RTCPReportTypesEnum.RTPFB && x.FeedbackMessageType == RTCPFeedbackTypesEnum.RTCP_SR_REQ:
                    // PLI feedback reports do no have any additional parameters.
                    break;
                case var x when x.PacketType == RTCPReportTypesEnum.RTPFB:
                    if (BitConverter.IsLittleEndian)
                    {
                        Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(PID)), 0, buffer, payloadIndex + 6, 2);
                        Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(BLP)), 0, buffer, payloadIndex + 8, 2);
                    }
                    else
                    {
                        Buffer.BlockCopy(BitConverter.GetBytes(PID), 0, buffer, payloadIndex + 6, 2);
                        Buffer.BlockCopy(BitConverter.GetBytes(BLP), 0, buffer, payloadIndex + 8, 2);
                    }
                    break;

                case var x when x.PacketType == RTCPReportTypesEnum.PSFB && x.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.PLI:
                    break;
                default:
                    throw new NotImplementedException($"Serialisation for feedback report {Header.PacketType} not yet implemented.");
            }
            return buffer;
        }
    }
}

/*
6.  Format of RTCP Feedback Messages

   This section defines the format of the low-delay RTCP feedback
   messages.These messages are classified into three categories as
   follows:

   - Transport layer FB messages
   - Payload-specific FB messages
   - Application layer FB messages

   Transport layer FB messages are intended to transmit general purpose
   feedback information, i.e., information independent of the particular
   codec or the application in use.The information is expected to be
   generated and processed at the transport/RTP layer.  Currently, only
   a generic negative acknowledgement (NACK) message is defined.

   Payload-specific FB messages transport information that is specific
   to a certain payload type and will be generated and acted upon at the
   codec "layer".  This document defines a common header to be used in
   conjunction with all payload-specific FB messages.The definition of
   specific messages is left either to RTP payload format specifications
   or to additional feedback format documents.

   Application layer FB messages provide a means to transparently convey
   feedback from the receiver's to the sender's application.  The
   information contained in such a message is not expected to be acted
   upon at the transport/RTP or the codec layer.The data to be
   exchanged between two application instances is usually defined in the
   application protocol specification and thus can be identified by the
   application so that there is no need for additional external
   information.Hence, this document defines only a common header to be
   used along with all application layer FB messages.  From a protocol
   point of view, an application layer FB message is treated as a
   special case of a payload-specific FB message.

      Note: Proper processing of some FB messages at the media sender
      side may require the sender to know which payload type the FB
      message refers to.Most of the time, this knowledge can likely be
      derived from a media stream using only a single payload type.
      However, if several codecs are used simultaneously (e.g., with
      audio and DTMF) or when codec changes occur, the payload type
      information may need to be conveyed explicitly as part of the FB
      message.This applies to all




Ott, et al.Standards Track[Page 31]

RFC 4585                        RTP/AVPF July 2006


      payload-specific as well as application layer FB messages.  It is
      up to the specification of an FB message to define how payload
      type information is transmitted.

   This document defines two transport layer and three (video) payload-
   specific FB messages as well as a single container for application
   layer FB messages.  Additional transport layer and payload-specific
   FB messages MAY be defined in other documents and MUST be registered
   through IANA (see Section 9, "IANA Considerations").

   The general syntax and semantics for the above RTCP FB message types
   are described in the following subsections.

6.1.   Common Packet Format for Feedback Messages

   All FB messages MUST use a common packet format that is depicted in
   Figure 3:

    0                   1                   2                   3
    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   |V=2|P|   FMT   |       PT      |          length               |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   |                  SSRC of packet sender                        |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   |                  SSRC of media source                         |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   :            Feedback Control Information(FCI)                  :
   :                                                               :

           Figure 3: Common Packet Format for Feedback Messages

   The fields V, P, SSRC, and length are defined in the RTP
   specification[2], the respective meaning being summarized below:

   version(V) : 2 bits
      This field identifies the RTP version.The current version is 2.

   padding(P) : 1 bit
      If set, the padding bit indicates that the packet contains
      additional padding octets at the end that are not part of the
      control information but are included in the length field.

Ott, et al.Standards Track[Page 32]

RFC 4585                        RTP/AVPF July 2006


   Feedback message type (FMT): 5 bits
      This field identifies the type of the FB message and is
      interpreted relative to the type (transport layer, payload-
      specific, or application layer feedback).  The values for each of
      the three feedback types are defined in the respective sections
      below.

   Payload type (PT): 8 bits
      This is the RTCP packet type that identifies the packet as being
      an RTCP FB message.Two values are defined by the IANA:

            Name   | Value | Brief Description
         ----------+-------+------------------------------------
            RTPFB  |  205  | Transport layer FB message
            PSFB   |  206  | Payload-specific FB message

   Length: 16 bits
      The length of this packet in 32-bit words minus one, including the
      header and any padding.  This is in line with the definition of
      the length field used in RTCP sender and receiver reports[3].

   SSRC of packet sender: 32 bits
      The synchronization source identifier for the originator of this
      packet.

   SSRC of media source: 32 bits
      The synchronization source identifier of the media source that
      this piece of feedback information is related to.

   Feedback Control Information (FCI): variable length
      The following three sections define which additional information
      MAY be included in the FB message for each type of feedback:
      transport layer, payload-specific, or application layer feedback.
      Note that further FCI contents MAY be specified in further
      documents.

   Each RTCP feedback packet MUST contain at least one FB message in the
   FCI field.Sections 6.2 and 6.3 define for each FCI type, whether or
   not multiple FB messages MAY be compressed into a single FCI field.
   If this is the case, they MUST be of the same type, i.e., same FMT.
   If multiple types of feedback messages, i.e., several FMTs, need to
   be conveyed, then several RTCP FB messages MUST be generated and
   SHOULD be concatenated in the same compound RTCP packet.

Ott, et al.                 Standards Track                    [Page 33]

RFC 4585                        RTP/AVPF July 2006


6.2.   Transport Layer Feedback Messages

   Transport layer FB messages are identified by the value RTPFB as RTCP
   message type.

   A single general purpose transport layer FB message is defined in
   this document: Generic NACK.  It is identified by means of the FMT
   parameter as follows:

   0:    unassigned
   1:    Generic NACK
   2-30: unassigned
   31:   reserved for future expansion of the identifier number space

   The following subsection defines the formats of the FCI field for
   this type of FB message.  Further generic feedback messages MAY be
   defined in the future.

6.2.1.  Generic NACK

   The Generic NACK message is identified by PT= RTPFB and FMT = 1.

   The FCI field MUST contain at least one and MAY contain more than one
   Generic NACK.

   The Generic NACK is used to indicate the loss of one or more RTP
   packets.The lost packet(s) are identified by the means of a packet
   identifier and a bit mask.

   Generic NACK feedback SHOULD NOT be used if the underlying transport
   protocol is capable of providing similar feedback information to the
   sender (as may be the case, e.g., with DCCP).

   The Feedback Control Information(FCI) field has the following Syntax
  (Figure 4) :

    0                   1                   2                   3
    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   |            PID                |             BLP               |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

               Figure 4: Syntax for the Generic NACK message

   Packet ID(PID): 16 bits
     The PID field is used to specify a lost packet.The PID field

     refers to the RTP sequence number of the lost packet.


Ott, et al.                 Standards Track                    [Page 34]

RFC 4585                        RTP/AVPF July 2006



  bitmask of following lost packets (BLP): 16 bits
     The BLP allows for reporting losses of any of the 16 RTP packets

     immediately following the RTP packet indicated by the PID.The
     BLP's definition is identical to that given in [6].  Denoting the

     BLP's least significant bit as bit 1, and its most significant bit
      as bit 16, then bit i of the bit mask is set to 1 if the receiver

     has not received RTP packet number (PID+i) (modulo 2^16) and
     indicates this packet is lost; bit i is set to 0 otherwise.Note
     that the sender MUST NOT assume that a receiver has received a
     packet because its bit mask was set to 0.  For example, the least

     significant bit of the BLP would be set to 1 if the packet

     corresponding to the PID and the following packet have been lost.
     However, the sender cannot infer that packets PID+2 through PID+16

     have been received simply because bits 2 through 15 of the BLP are
      0; all the sender knows is that the receiver has not reported them
      as lost at this time.

  The length of the FB message MUST be set to 2+n, with n being the

  number of Generic NACKs contained in the FCI field.

  The Generic NACK message implicitly references the payload type
  through the sequence number(s).

6.3.  Payload-Specific Feedback Messages

  Payload-Specific FB messages are identified by the value PT= PSFB as
  RTCP message type.


  Three payload-specific FB messages are defined so far plus an
  application layer FB message.They are identified by means of the
  FMT parameter as follows:

      0:     unassigned
      1:     Picture Loss Indication (PLI)
      2:     Slice Loss Indication (SLI)
      3:     Reference Picture Selection Indication (RPSI)
      4-14:  unassigned
      15:    Application layer FB (AFB) message
      16-30: unassigned
      31:    reserved for future expansion of the sequence number space

  The following subsections define the FCI formats for the payload-

  specific FB messages, Section 6.4 defines FCI format for the
  application layer FB message.


Ott, et al.                 Standards Track                    [Page 35]

RFC 4585                        RTP/AVPF July 2006


6.3.1.  Picture Loss Indication (PLI)

  The PLI FB message is identified by PT= PSFB and FMT = 1.


  There MUST be exactly one PLI contained in the FCI field.

6.3.1.1.  Semantics

  With the Picture Loss Indication message, a decoder informs the

  encoder about the loss of an undefined amount of coded video data

  belonging to one or more pictures.  When used in conjunction with any
  video coding scheme that is based on inter-picture prediction, an
  encoder that receives a PLI becomes aware that the prediction chain

  may be broken.The sender MAY react to a PLI by transmitting an

  intra-picture to achieve resynchronization (making this message
  effectively similar to the FIR message as defined in [6]); however,
   the sender MUST consider congestion control as outlined in Section 7,
   which MAY restrict its ability to send an intra frame.

   Other RTP payload specifications such as RFC 2032 [6]
already define
  a feedback mechanism for some for certain codecs.  An application
  supporting both schemes MUST use the feedback mechanism defined in
   this specification when sending feedback.  For backward compatibility
  reasons, such an application SHOULD also be capable to receive and
  react to the feedback scheme defined in the respective RTP payload
  format, if this is required by that payload format.

6.3.1.2.  Message Format

  PLI does not require parameters.  Therefore, the length field MUST be
   2, and there MUST NOT be any Feedback Control Information.

   The semantics of this FB message is independent of the payload type.

6.3.1.3.  Timing Rules

  The timing follows the rules outlined in Section 3.  In systems that
  employ both PLI and other types of feedback, it may be advisable to
  follow the Regular RTCP RR timing rules for PLI, since PLI is not as
   delay critical as other FB types.

6.3.1.4.  Remarks

  PLI messages typically trigger the sending of full intra-pictures.
   Intra-pictures are several times larger then predicted (inter-)
   pictures.  Their size is independent of the time they are generated.
   In most environments, especially when employing bandwidth-limited
  links, the use of an intra-picture implies an allowed delay that is a



Ott, et al.                 Standards Track [Page 36]

RFC 4585                        RTP/AVPF July 2006


   significant multitude of the typical frame duration.  An example: If
  the sending frame rate is 10 fps, and an intra-picture is assumed to
  be 10 times as big as an inter-picture, then a full second of latency
  has to be accepted.  In such an environment, there is no need for a
  particular short delay in sending the FB message.  Hence, waiting for
   the next possible time slot allowed by RTCP timing rules as per [2]
with Tmin=0 does not have a negative impact on the system
  performance.

6.3.2.  Slice Loss Indication (SLI)

   The SLI FB message is identified by PT=PSFB and FMT=2.

   The FCI field MUST contain at least one and MAY contain more than one
  SLI.

6.3.2.1.  Semantics

  With the Slice Loss Indication, a decoder can inform an encoder that
  it has detected the loss or corruption of one or several consecutive
  macroblock(s) in scan order (see below).  This FB message MUST NOT be
  used for video codecs with non-uniform, dynamically changeable
  macroblock sizes such as H.263 with enabled Annex Q.  In such a case,
   an encoder cannot always identify the corrupted spatial region.

6.3.2.2.  Format

  The Slice Loss Indication uses one additional FCI field, the content
  of which is depicted in Figure 6.  The length of the FB message MUST
  be set to 2 + n, with n being the number of SLIs contained in the FCI
   field.

    0                   1                   2                   3
    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
   + -+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   | First | Number | PictureID |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

            Figure 6: Syntax of the Slice Loss Indication(SLI)

   First: 13 bits
      The macroblock(MB) address of the first lost macroblock.  The MB
      numbering is done such that the macroblock in the upper left
      corner of the picture is considered macroblock number 1 and the
      number for each macroblock increases from left to right and then
      from top to bottom in raster - scan order(such that if there is a
       total of N macroblocks in a picture, the bottom right macroblock
       is considered macroblock number N).



Ott, et al.                 Standards Track[Page 37]


RFC 4585                        RTP / AVPF                       July 2006


   Number: 13 bits
      The number of lost macroblocks, in scan order as discussed above.

   PictureID: 6 bits
      The six least significant bits of the codec - specific identifier
      that is used to reference the picture in which the loss of the
      macroblock(s) has occurred.  For many video codecs, the PictureID
      is identical to the Temporal Reference.

   The applicability of this FB message is limited to a small set of
   video codecs; therefore, no explicit payload type information is
   provided.

6.3.2.3.Timing Rules

 The efficiency of algorithms using the Slice Loss Indication is
 reduced greatly when the Indication is not transmitted in a timely
   fashion.Motion compensation propagates corrupted pixels that are
 not reported as being corrupted.Therefore, the use of the algorithm
discussed in Section 3 is highly recommended.

6.3.2.4.Remarks

   The term Slice is defined and used here in the sense of MPEG-1-- a
  consecutive number of macroblocks in scan order.  More recent video
  coding standards sometimes have a different understanding of the term
   Slice.In H.263(1998), for example, a concept known as "rectangular

slice" exists.  The loss of one rectangular slice may lead to the

necessity of sending more than one SLI in order to precisely identify

the region of lost / damaged MBs.


The first field of the FCI defines the first macroblock of a picture
as 1 and not, as one could suspect, as 0.This was done to align

this specification with the comparable mechanism available in ITU - T

Rec.H.245[24].The maximum number of macroblocks in a picture
(2 * *13 or 8192) corresponds to the maximum picture sizes of most of

the ITU - T and ISO / IEC video codecs.If future video codecs offer

larger picture sizes and / or smaller macroblock sizes, then an

additional FB message has to be defined.The six least significant

bits of the Temporal Reference field are deemed to be sufficient to

indicate the picture in which the loss occurred.


The reaction to an SLI is not part of this specification.One

typical way of reacting to an SLI is to use intra refresh for the
affected spatial region.


Ott, et al.Standards Track[Page 38]


RFC 4585                        RTP / AVPF                       July 2006



Algorithms were reported that keep track of the regions affected by

motion compensation, in order to allow for a transmission of Intra

macroblocks to all those areas, regardless of the timing of the FB
(see H.263(2000) Appendix I[17] and[15]).Although the timing of

the FB is less critical when those algorithms are used than if they
   are not, it has to be observed that those algorithms correct large
   parts of the picture and, therefore, have to transmit much higher
   data volume in case of delayed FBs.

6.3.3.Reference Picture Selection Indication(RPSI)

   The RPSI FB message is identified by PT = PSFB and FMT = 3.

   There MUST be exactly one RPSI contained in the FCI field.

6.3.3.1.Semantics

   Modern video coding standards such as MPEG - 4 visual version 2[16] or
    H.263 version 2[17] allow using older reference pictures than the
   most recent one for predictive coding.  Typically, a first -in-first -
   out queue of reference pictures is maintained.If an encoder has
   learned about a loss of encoder - decoder synchronicity, a known -as-
   correct reference picture can be used.As this reference picture is
   temporally further away then usual, the resulting predictively coded
   picture will use more bits.

   Both MPEG - 4 and H.263 define a binary format for the "payload" of an
  
     RPSI message that includes information such as the temporal ID of the
  
     damaged picture and the size of the damaged region.This bit string
     is typically small (a couple of dozen bits), of variable length, and
   self - contained, i.e., contains all information that is necessary to
   perform reference picture selection.

   Both MPEG-4 and H.263 allow the use of RPSI with positive feedback
   information as well.That is, pictures(or Slices) are reported that
were decoded without error.Note that any form of positive feedback
MUST NOT be used when in a multiparty session(reporting positive

feedback about individual reference pictures at RTCP intervals is not
expected to be of much use anyway).


Ott, et al.                 Standards Track[Page 39]


RFC 4585                        RTP / AVPF                       July 2006


6.3.3.2.Format

   The FCI for the RPSI message follows the format depicted in Figure 7:

    0                   1                   2                   3
    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
   + -+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   | PB | 0 | Payload Type | Native RPSI bit string |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   | defined per codec...                | Padding(0) |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

   Figure 7: Syntax of the Reference Picture Selection Indication(RPSI)

   PB: 8 bits
      The number of unused bits required to pad the length of the RPSI
      message to a multiple of 32 bits.

   0:  1 bit
      MUST be set to zero upon transmission and ignored upon reception.

   Payload Type: 7 bits
      Indicates the RTP payload type in the context of which the native
      RPSI bit string MUST be interpreted.

   Native RPSI bit string: variable length
      The RPSI information as natively defined by the video codec.

   Padding: #PB bits
      A number of bits set to zero to fill up the contents of the RPSI
      message to the next 32 - bit boundary.The number of padding bits
      MUST be indicated by the PB field.

6.3.3.3.Timing Rules

   RPSI is even more critical to delay than algorithms using SLI.This
   is because the older the RPSI message is, the more bits the encoder
   has to spend to re-establish encoder - decoder synchronicity.See[15]
   for some information about the overhead of RPSI for certain bit
   rate / frame rate / loss rate scenarios.

   Therefore, RPSI messages should typically be sent as soon as
   possible, employing the algorithm of Section 3.


*/
