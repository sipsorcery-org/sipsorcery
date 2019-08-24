#include "DtlsManaged.h"

#define SSL_WHERE_INFO(ssl, w, flag, msg) {                \
    if(w & flag) {                                         \
      printf("%20.20s", msg);                              \
      printf(" - %30.30s ", SSL_state_string_long(ssl));   \
      printf(" - %5.10s ", SSL_state_string(ssl));         \
      printf("\n");                                        \
	    }                                                      \
    } 


namespace SIPSorceryMedia {

	int krx_ssl_verify_peer(int ok, X509_STORE_CTX* ctx) {
		return 1;
	}

	//void krx_ssl_info_callback(const SSL* ssl, int where, int ret, const char* name) {
	void krx_ssl_info_callback(const SSL* ssl, int where, int ret) 
	{
		if (ret == 0) {
			printf("-- krx_ssl_info_callback: error occured.\n");
			return;
		}

		SSL_WHERE_INFO(ssl, where, SSL_CB_LOOP, "LOOP");
		SSL_WHERE_INFO(ssl, where, SSL_CB_HANDSHAKE_START, "HANDSHAKE START");
		SSL_WHERE_INFO(ssl, where, SSL_CB_HANDSHAKE_DONE, "HANDSHAKE DONE");
	}

	DtlsManaged::DtlsManaged(System::String ^ certFile, System::String ^ keyFile)
	{
    _certFile::set(certFile);
    _keyFile::set(keyFile);

		SSL_library_init();
		SSL_load_error_strings();
		ERR_load_BIO_strings();
		OpenSSL_add_all_algorithms();

		_k = new krx();
		_k->ctx = nullptr;
		_k->ssl = nullptr;
		_k->in_bio = nullptr;
		_k->out_bio = nullptr;
	}

	DtlsManaged::~DtlsManaged()
	{
		if (_k != nullptr) {

			if (_k->ctx != nullptr) {
				SSL_CTX_free(_k->ctx);
				_k->ctx = nullptr;
			}

			if (_k->ssl != nullptr) {
				SSL_free(_k->ssl);
				_k->ssl = nullptr;
			}

			/*if (_k->in_bio != nullptr) {
				BIO_free(_k->in_bio);
				_k->in_bio = nullptr;
			}

			if (_k->out_bio != nullptr) {
				BIO_free(_k->out_bio);
				_k->out_bio = nullptr;
			}*/

			_k = nullptr;
		}
	}

	int DtlsManaged::Init()
	{
		// Dump any openssl errors.
		ERR_print_errors_fp(stderr);

		int r = 0;

		/* create a new context using DTLS */
		_k->ctx = SSL_CTX_new(DTLS_method());	// Copes with DTLS 1.0 and 1.2.
		if (!_k->ctx) {
			printf("Error: cannot create SSL_CTX.\n");
			ERR_print_errors_fp(stderr);
			return -1;
		}

		/* set our supported ciphers */
		r = SSL_CTX_set_cipher_list(_k->ctx, "ALL:!ADH:!LOW:!EXP:!MD5:@STRENGTH");
		if (r != 1) {
			printf("Error: cannot set the cipher list.\n");
			ERR_print_errors_fp(stderr);
			return -2;
		}

		// Needed for FireFox DTLS negotiation.
		SSL_CTX_set_ecdh_auto(_k->ctx, 1);

		/* the client doesn't have to send it's certificate */
		SSL_CTX_set_verify(_k->ctx, SSL_VERIFY_PEER, krx_ssl_verify_peer);

		/* enable srtp */
		r = SSL_CTX_set_tlsext_use_srtp(_k->ctx, "SRTP_AES128_CM_SHA1_80");
		if (r != 0) {
			printf("Error: cannot setup srtp.\n");
			ERR_print_errors_fp(stderr);
			return -3;
		}

		/* load key and certificate */
		/*char certfile[1024];
		char keyfile[1024];
		sprintf(certfile, "./%s-cert.pem", "server");
		sprintf(keyfile, "./%s-key.pem", "server");*/

    std::string certFilePath = msclr::interop::marshal_as<std::string>(_certFile);

		/* certificate file; contains also the public key */
		r = SSL_CTX_use_certificate_file(_k->ctx, certFilePath.c_str(), SSL_FILETYPE_PEM);
		if (r != 1) {
			printf("Error: cannot load certificate file.\n");
			ERR_print_errors_fp(stderr);
			return -4;
		}

    std::string keyFilePath = msclr::interop::marshal_as<std::string>(_keyFile);

		/* load private key */
		r = SSL_CTX_use_PrivateKey_file(_k->ctx, keyFilePath.c_str(), SSL_FILETYPE_PEM);
		if (r != 1) {
			printf("Error: cannot load private key file.\n");
			ERR_print_errors_fp(stderr);
			return -5;
		}

		/* check if the private key is valid */
		r = SSL_CTX_check_private_key(_k->ctx);
		if (r != 1) {
			printf("Error: checking the private key failed. \n");
			ERR_print_errors_fp(stderr);
			return -6;
		}

		sprintf(_k->name, "+ %s", "server");
		
		/* create SSL* */
		_k->ssl = SSL_new(_k->ctx);
		if (!_k->ssl) {
			printf("Error: cannot create new SSL*.\n");
			return -7;
		}

		/* info callback */
		SSL_set_info_callback(_k->ssl, krx_ssl_info_callback);

		/* bios */
		_k->in_bio = BIO_new(BIO_s_mem());
		if (_k->in_bio == NULL) {
			printf("Error: cannot allocate read bio.\n");
			return -2;
		}

		BIO_set_mem_eof_return(_k->in_bio, -1); /* see: https://www.openssl.org/docs/crypto/BIO_s_mem.html */

		_k->out_bio = BIO_new(BIO_s_mem());
		if (_k->out_bio == NULL) {
			printf("Error: cannot allocate write bio.\n");
			return -3;
		}

		BIO_set_mem_eof_return(_k->out_bio, -1); /* see: https://www.openssl.org/docs/crypto/BIO_s_mem.html */

		SSL_set_bio(_k->ssl, _k->in_bio, _k->out_bio);

		/* either use the server or client part of the protocol */
		//SSL_set_connect_state(_k->ssl);
		SSL_set_accept_state(_k->ssl);

		// Dump any openssl errors.
		ERR_print_errors_fp(stderr);

		return 0;
	}

	/*int DtlsManaged::DoHandshake(cli::array<System::Byte>^ buffer, int bufferLength)
	{
		SSL_do_handshake(_k->ssl);

		int pending = BIO_ctrl_pending(_k->out_bio);

		if (pending > 0) {
			
			pin_ptr<System::Byte> p = &buffer[0];
			unsigned char* pby = p;
			char* pch = reinterpret_cast<char*>(pby);

			int read = BIO_read(_k->out_bio, p, bufferLength);

			return read;
		}

		return 0;
	}*/

	int DtlsManaged::Write(cli::array<System::Byte>^ buffer, int bufferLength)
	{
		pin_ptr<System::Byte> p = &buffer[0];
		unsigned char* pby = p;
		char* pch = reinterpret_cast<char*>(pby);

		int written = BIO_write(_k->in_bio, pch, bufferLength);

		if (written > 0 && !SSL_is_init_finished(_k->ssl))
		{
			SSL_do_handshake(_k->ssl);
		}

		// Dump any openssl errors.
		ERR_print_errors_fp(stderr);

		return written;
	}

	int DtlsManaged::Read(cli::array<System::Byte>^ buffer, int bufferLength)
	{
		int pending = BIO_ctrl_pending(_k->out_bio);

		if (pending > 0) {

			pin_ptr<System::Byte> p = &buffer[0];
			unsigned char* pby = p;
			char* pch = reinterpret_cast<char*>(pby);

			int read = BIO_read(_k->out_bio, p, bufferLength);

			return read;
		}

		// Dump any openssl errors.
		ERR_print_errors_fp(stderr);

		return 0;
	}

	bool DtlsManaged::IsHandshakeComplete()
	{
		// Dump any openssl errors.
		ERR_print_errors_fp(stderr);

		return _k->ssl->in_handshake == 0;
	}

	int DtlsManaged::GetState()
	{
		// Dump any openssl errors.
		ERR_print_errors_fp(stderr);

		return _k->ssl->state;
	}

	//int krx_ssl_handle_traffic(krx* from, krx* to) {

	//	// Did SSL write something into the out buffer
	//	char outbuf[4096];
	//	int written = 0;
	//	int read = 0;
	//	int pending = BIO_ctrl_pending(from->out_bio);

	//	if (pending > 0) {
	//		read = BIO_read(from->out_bio, outbuf, sizeof(outbuf));
	//	}
	//	printf("%s Pending %d, and read: %d\n", from->name, pending, read);

	//	if (read > 0) {
	//		written = BIO_write(to->in_bio, outbuf, read);
	//	}

	//	if (written > 0) {
	//		if (!SSL_is_init_finished(to->ssl)) {
	//			SSL_do_handshake(to->ssl);
	//		}
	//		else {
	//			read = SSL_read(to->ssl, outbuf, sizeof(outbuf));
	//			printf("%s read: %s\n", to->name, outbuf);
	//		}
	//	}

	//	return 0;
	//}

	int DtlsManaged::VerifyPeer(int ok, X509_STORE_CTX* ctx) 
	{
		return 1;
	}
}