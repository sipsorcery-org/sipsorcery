// https://gist.github.com/roxlu/9835067

#pragma once

#include "srtp.h"
#include "openssl\srtp.h"
#include "openssl\err.h"

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
		krx * _k;

		static int VerifyPeer(int ok, X509_STORE_CTX* ctx);
	public:
		DtlsManaged();
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