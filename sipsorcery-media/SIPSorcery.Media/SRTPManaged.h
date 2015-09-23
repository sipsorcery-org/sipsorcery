// http://stackoverflow.com/questions/22692109/webrtc-srtp-decryption
// https://tools.ietf.org/html/rfc5764  Datagram Transport Layer Security (DTLS) Extension to Establish Keys for the Secure Real - time Transport Protocol(SRTP)
// https://tools.ietf.org/html/rfc3711 The Secure Real-time Transport Protocol (SRTP)
// https://code.google.com/p/webrtc/source/browse/trunk/talk/session/media/srtpfilter.cc?r=5590 libjingle equivalent of this class, see SrtpSession::SetKey.

#pragma once

#include "srtp.h"
#include "err.h"
#include "openssl\srtp.h"
#include "openssl\err.h"
#include "DtlsManaged.h"

extern "C" srtp_debug_module_t mod_srtp;
extern "C" srtp_debug_module_t srtp_mod_auth;

using namespace System;

namespace SIPSorceryMedia {

	public ref class SRTPManaged {
		public:
			SRTPManaged(cli::array<System::Byte>^ key, bool isClient);
			SRTPManaged(DtlsManaged^ dtlsContext, bool isClient);
			~SRTPManaged();
			int ProtectRTP(cli::array<System::Byte>^ buffer, int length);
			int UnprotectRTP(cli::array<System::Byte>^ buffer, int length);

		private:

			static const int SRTP_MASTER_KEY_KEY_LEN = 16;
			static const int SRTP_MASTER_KEY_SALT_LEN = 14;

			srtp_t * _session;
			System::String^ _key;
	};
}