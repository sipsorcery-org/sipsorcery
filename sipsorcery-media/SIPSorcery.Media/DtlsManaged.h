//-----------------------------------------------------------------------------
// Filename: DtlsManaged.h
//
// Description: A rudimentary Data Transport Layer Security (DTLS) wrapper around OpenSSL DTLS functions.
//
// History:
// ??	          Aaron Clauson	  Created, based on https://gist.github.com/roxlu/9835067.
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
// OpenSSL License:
// This application includes software developed by the OpenSSL Project and cryptographic software written by Eric Young (eay@cryptsoft.com)
// See the accompanying LICENSE file for conditions.
//-----------------------------------------------------------------------------

#pragma once

#include "srtp2/srtp.h"
#include "openssl\srtp.h"
#include "openssl\err.h"
#include <msclr\marshal.h>
#include <msclr\marshal_cppstd.h>

#include <string>

namespace SIPSorceryMedia {

	typedef struct {
		SSL_CTX* ctx;                                                                       /* main ssl context */
		SSL* ssl;                                                                           /* the SSL* which represents a "connection" */
		BIO* in_bio;                                                                        /* we use memory read bios */
		BIO* out_bio;                                                                       /* we use memory write bios */
		char name[512];
	} krx;

	public ref class DtlsManaged
	{
	private:
		krx * _k { nullptr };
    property System::String ^ _certFile;
    property System::String ^ _keyFile;

		static int VerifyPeer(int ok, X509_STORE_CTX* ctx);
	public:

    /*
    * Initialises 
    */
		DtlsManaged(System::String ^ certFile, System::String ^ _keyFile);
		~DtlsManaged();
		int Init();
		//int DoHandshake(cli::array<System::Byte>^ buffer, int bufferLength); 
		int Write(cli::array<System::Byte>^ buffer, int bufferLength);
		int Read(cli::array<System::Byte>^ buffer, int bufferLength);
		bool IsHandshakeComplete();
		int GetState();

		property SSL* Ssl {
			SSL* get() {
				return _k->ssl;
			}
		}
	};
}