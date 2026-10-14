---
type: Security
title: Authenticated connector-to-connector manifests
description: Per-peer HMAC-SHA256 manifests for paired connector instances.
---

# Signed manifests (Issue #229)

An export manifest optionally includes `Signature`: a 64-character lowercase hex HMAC-SHA256 value over a **versioned, length-prefixed** encoding of `Sha256Checksum`, `Producer.Application`, `Producer.Version` and `Producer.InstanceId`. Changing any authenticated field invalidates it. The receiving connector verifies the signature **after checksum verification but before staging/diffing or ERP writes**. A failed check quarantines the file and records an audit event; secrets and signing values are not logged.

## Pairing setup

Generate a separate high-entropy **32-byte or longer** shared secret for each pairing and transfer it through a secure channel:

```sh
openssl rand -base64 32
```

Use `GET /api/settings/instance` to read each instance ID. With a JWT authorized for `SettingsInstance`, configure the **same secret** on both sides, but address the *other* instance ID:

- On exporter A, `PUT /api/settings/manifest-signing/peers/{B-instance-id}` with `{"sharedSecret":"<base64>","useForOutbound":true}`.
- On importer B, `PUT /api/settings/manifest-signing/peers/{A-instance-id}` with `{"sharedSecret":"<same-base64>","useForOutbound":false}`.
- On importer B, `PUT /api/settings/manifest-signing/policy` with `{"requireSignedConnectorFiles":true}` to reject unknown or unsigned connector manifests.

`GET /api/settings/manifest-signing` returns **only** policy, outbound peer ID and trusted peer IDs, never keys. `DELETE /api/settings/manifest-signing/peers/{instanceId}` removes trust for that peer. Settings are stored through the existing encrypted-at-rest `AppSetting.Value` converter (Data Protection key ring must be backed up separately). Avoid putting secrets into audit records or exported configuration snapshots.

## Compatibility, limitations and operations

Without outbound configuration, old manifests continue to be unsigned. If a sender ID is explicitly trusted, signatures are **always mandatory** for that producer even before global enforcement is enabled. When global enforcement is enabled, unknown producer IDs fail and any file with no `Producer` targeting a connector-paired import definition (`IntegrationKey`) fails. Non-paired vendor files without `Producer` remain valid.

Export signing currently supports **one selected outbound peer per connector instance**; multiple inbound trusted peers are supported. Use separate sender instances or an enhancement for simultaneous multiple outbound recipients. Rotation requires updating both sides to the new secret in a coordinated window; there is no grace period for old signatures. A restored or cloned instance should regenerate its identity and update pairing registrations. HMAC authenticates parties holding the shared key, not a legal/person-bound non-repudiation signature.

Four-eyes approval, manifest SHA-256 integrity, import allowlists, replay/idempotency checks and audit logging remain in effect.
