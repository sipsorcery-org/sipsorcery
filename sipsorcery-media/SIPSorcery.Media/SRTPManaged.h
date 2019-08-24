//-----------------------------------------------------------------------------
// Filename: SRTPManaged.h
//
// Description: A rudimentary Secure Real-Time Transport (SRTP) wrapper around Cisco's srtp library.
//
// History:
// ??	          Aaron Clauson	  Created.
// 24 Aug 2019  Aaron Clauson   Added header comment block.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2019 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Montreux, Switzerland (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//
// Useful Links:
// http://stackoverflow.com/questions/22692109/webrtc-srtp-decryption
// https://tools.ietf.org/html/rfc5764  Datagram Transport Layer Security (DTLS) Extension to Establish Keys for the Secure Real - time Transport Protocol(SRTP)
// https://tools.ietf.org/html/rfc3711 The Secure Real-time Transport Protocol (SRTP)
// (broken link) https://code.google.com/p/webrtc/source/browse/trunk/talk/session/media/srtpfilter.cc?r=5590 libjingle equivalent of this class, see SrtpSession::SetKey.
//-----------------------------------------------------------------------------

#pragma once

#include <winsock2.h>
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "srtp.h"
#include "err.h"
#include "openssl\srtp.h"
#include "openssl\err.h"
#include "DtlsManaged.h"

#include <msclr/marshal_cppstd.h>
#include <iostream>

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
      int ProtectRTCP(cli::array<System::Byte>^ buffer, int length);

		private:

			static const int SRTP_MASTER_KEY_KEY_LEN = 16;
			static const int SRTP_MASTER_KEY_SALT_LEN = 14;

			srtp_t * _session{ nullptr };
			System::String^ _key;
	};
}