#include <iostream>
#include <msclr/marshal_cppstd.h>
#include "SRTPManaged.h"

using namespace System;
using namespace System::Runtime::InteropServices;

namespace SIPSorceryMedia {

	SRTPManaged::SRTPManaged(cli::array<System::Byte>^ key, bool isClient)
	{
		srtp_init();

		// Need pre-processor directive of ENABLE_DEBUGGING for libsrtp debugging.
		//debug_on(mod_srtp);
		//debug_on(srtp_mod_auth);

		pin_ptr<System::Byte> p = &key[0];
		unsigned char* pby = p;
		unsigned char* pch = reinterpret_cast<unsigned char*>(pby);

		auto policy = new srtp_policy_t(); 

		// set policy to describe a policy for an SRTP stream
		srtp_crypto_policy_set_rtp_default(&policy->rtp);
		srtp_crypto_policy_set_rtcp_default(&policy->rtcp);

		policy->key = pch;
		policy->window_size = 128;
		policy->allow_repeat_tx = 0;
		policy->ssrc.type = (isClient) ? ssrc_any_outbound : ssrc_any_inbound;
		policy->next = NULL;
		_session = new srtp_t();
		auto err = srtp_create(_session, policy);

		delete(policy);

		std::cout << "Create srtp session result " << err << "." << std::endl;
	}

	SRTPManaged::SRTPManaged(DtlsManaged^ dtlsContext, bool isClient)
	{
		srtp_init();

		// Need pre-processor directive of ENABLE_DEBUGGING for libsrtp debugging.
		//debug_on(mod_srtp);
		//debug_on(srtp_mod_auth);

		unsigned char dtls_buffer[SRTP_MASTER_KEY_KEY_LEN * 2 + SRTP_MASTER_KEY_SALT_LEN * 2];
		unsigned char client_write_key[SRTP_MASTER_KEY_KEY_LEN + SRTP_MASTER_KEY_SALT_LEN];
		unsigned char server_write_key[SRTP_MASTER_KEY_KEY_LEN + SRTP_MASTER_KEY_SALT_LEN];
		size_t offset = 0;

		const char * label = "EXTRACTOR-dtls_srtp";

		SRTP_PROTECTION_PROFILE * srtp_profile = SSL_get_selected_srtp_profile(dtlsContext->Ssl);

		int res = SSL_export_keying_material(dtlsContext->Ssl,
			dtls_buffer,
			sizeof(dtls_buffer),
			label,
			strlen(label),
			NULL,
			0,
			0);

		if (res != 1)
		{
			printf("Export of SSL key information failed.\n");
		}
		else
		{
			memcpy(&client_write_key[0], &dtls_buffer[offset], SRTP_MASTER_KEY_KEY_LEN);
			offset += SRTP_MASTER_KEY_KEY_LEN;
			memcpy(&server_write_key[0], &dtls_buffer[offset], SRTP_MASTER_KEY_KEY_LEN);
			offset += SRTP_MASTER_KEY_KEY_LEN;
			memcpy(&client_write_key[SRTP_MASTER_KEY_KEY_LEN], &dtls_buffer[offset], SRTP_MASTER_KEY_SALT_LEN);
			offset += SRTP_MASTER_KEY_SALT_LEN;
			memcpy(&server_write_key[SRTP_MASTER_KEY_KEY_LEN], &dtls_buffer[offset], SRTP_MASTER_KEY_SALT_LEN);

			auto policy = new srtp_policy_t(); // (srtp_policy_t *)malloc(sizeof(srtp_policy_t));
			/*srtp_policy_t policy;
			memset(&policy, 0, sizeof(policy));*/

			//switch (srtp_profile->id)
			//{
			//case SRTP_AES128_CM_SHA1_80:
			//	srtp_crypto_policy_set_aes_cm_128_hmac_sha1_80(&policy->rtp);
			//	srtp_crypto_policy_set_aes_cm_128_hmac_sha1_80(&policy->rtcp);
			//	break;
			//case SRTP_AES128_CM_SHA1_32:
			//	srtp_crypto_policy_set_aes_cm_128_hmac_sha1_32(&policy->rtp);   // rtp is 32,
			//	srtp_crypto_policy_set_aes_cm_128_hmac_sha1_80(&policy->rtcp);  // rtcp still 80
			//	break;
			//default:
			//	printf("Unable to create SRTP policy.\n");
			//}

			srtp_crypto_policy_set_rtp_default(&policy->rtp);
			srtp_crypto_policy_set_rtcp_default(&policy->rtcp);

			/*printf("SRTP server encryption key: ");
			for (int i = 0; i < SRTP_MASTER_KEY_KEY_LEN + SRTP_MASTER_KEY_SALT_LEN; i++)
			{
				printf("%x", client_write_key[i]);
			}
			printf("\n");*/

			/* Init transmit direction */
			policy->key = (isClient) ? client_write_key : server_write_key;
			//_policy->key = server_write_key;

			policy->ssrc.value = 0;
			policy->window_size = 128;
			policy->allow_repeat_tx = 0;
			policy->ssrc.type = (isClient) ? ssrc_any_inbound : ssrc_any_outbound;
			policy->next = NULL;
			_session = new srtp_t();

			auto err = srtp_create(_session, policy);
			if (err != srtp_err_status_ok) {
				printf("Unable to create SRTP session.\n");
			}

			delete(policy);

			if (isClient)
			{
				std::cout << "Create srtp client session result " << err << "." << std::endl;
			}
			else
			{
				std::cout << "Create srtp server session result " << err << "." << std::endl;
			}
		}
	}

	int SRTPManaged::UnprotectRTP(cli::array<System::Byte>^ buffer, int length)
	{
		pin_ptr<System::Byte> p = &buffer[0];
		unsigned char* pby = p;
		char* pch = reinterpret_cast<char*>(pby);

		srtp_err_status_t result = srtp_unprotect(*_session, pch, &length);

		return result;
	}

	int SRTPManaged::ProtectRTP(cli::array<System::Byte>^ buffer, int length)
	{
		pin_ptr<System::Byte> p = &buffer[0];
		unsigned char* pby = p;
		char* pch = reinterpret_cast<char*>(pby);

		srtp_err_status_t result = srtp_protect(*_session, pch, &length);

		return result;
	}

	SRTPManaged::~SRTPManaged()
	{
		if (_session != nullptr)
		{
			srtp_dealloc(*_session);
			_session = nullptr;
		}
	}
}
