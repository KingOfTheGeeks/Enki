# dev-keys/

**DEV ONLY — DO NOT USE THESE KEYS IN PRODUCTION.**

RSA-2048 keypair used by Enki's `HeimdallLicenseFileGenerator` to sign
generated `.lic` files in local dev. Generated with:

```
openssl genrsa -out private.pem 2048
openssl rsa  -in private.pem -pubout -out public.pem
```

**Production must override** `Licensing:PrivateKeyPath` in environment-
specific config (or via the `Enki__Licensing__PrivateKeyPath`
environment variable). The generator refuses to start if the path is
missing — fail-loud is intentional.

The committed key is published in plain text on a public repo and
should be considered compromised. Anyone trying to verify a license
signed by this key cannot trust its origin — that's fine for dev,
because nothing field-deployed should be talking to a dev-key license.
