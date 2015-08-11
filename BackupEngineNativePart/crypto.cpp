#include "stdafx.h"
#include "ExportedFunctions.h"

EXPORT_THIS void generate_new_key_pair(keypair_callback_t cb, int key_size_multiplier){
	using namespace CryptoPP;
	RSA::PrivateKey rsa_private;
	AutoSeededRandomPool rnd;
	std::string priv, pub;
	while (true){
		rsa_private.GenerateRandomWithKeySize(rnd, 1024 * key_size_multiplier);
		if (!rsa_private.Validate(rnd, 3))
			continue;
		RSA::PublicKey rsa_public(rsa_private);
		if (!rsa_public.Validate(rnd, 3))
			continue;
		rsa_private.Save(StringSink(priv));
		rsa_public.Save(StringSink(pub));
	}
	std::wstring wpriv(priv.begin(), priv.end());
	std::wstring wpub(pub.begin(), pub.end());
	cb(wpriv.c_str(), wpub.c_str());
}
