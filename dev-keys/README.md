# dev-keys/

**DEV ONLY — DO NOT USE THESE KEYS IN PRODUCTION.**

RSA-2048 keypair used by Enki's `HeimdallLicenseFileGenerator` to sign
generated `.lic` files in local dev.

**This keypair is the same one Nabu used and matches Marduk's hardcoded
`AMR.Core.Licensing.Infrastructure.PublicKeyPem.Value`.** Do NOT
regenerate it with `openssl genrsa` — Marduk verifies every `.lic`
signature against the hardcoded public key, so a fresh dev keypair
makes every Enki-issued license fail signature verification in
Esagila and any other field decoder. The contract test
`HeimdallLicenseFileGeneratorTests.Generated_envelope_verifies_against_marduk_production_public_key`
guards this — if you change the keypair, that test will fail.

To rotate the keypair properly, you must coordinate the change with
Marduk (`AMR.Core.Licensing/Infrastructure/PublicKeyPem.cs`) and any
field-deployed decoder bundle, or every previously-issued license
stops verifying.

**Production must override** `Licensing:PrivateKeyPath` in environment-
specific config (or via the `Enki__Licensing__PrivateKeyPath`
environment variable). The generator refuses to start if the path is
missing — fail-loud is intentional.

The committed key is published in plain text on a public repo and
should be considered compromised. Anyone trying to verify a license
signed by this key cannot trust its origin — that's fine for dev,
because nothing field-deployed should be talking to a dev-key license.
