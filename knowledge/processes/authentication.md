---
type: Business Process
title: Authentication
description: JWT Bearer authentication protecting all API routes; BCrypt-hashed user store with dev seed and production configuration. Plus an opt-in X-Api-Key scheme for machine-to-machine callers.
resource: src/Connector.Api/Auth.cs
tags: [process, auth, jwt, security, bcrypt, api-key]
timestamp: 2026-09-03T00:00:00Z
---

All API routes except `POST /api/auth/login` require a valid JWT Bearer token.
The token is issued at login, signed with a shared secret, and carries the username as the `Name` claim.
A small number of endpoints (currently just `POST /api/pipeline/run/{name}` — see
[On-Demand Run](/processes/on-demand-run.md)) additionally accept an `X-Api-Key` header instead, for a
"dedicated API user" that shouldn't need to log in interactively. Every other endpoint is unaffected —
adding the `ApiKey` scheme did not change the default JWT requirement anywhere else.

# Login

```
POST /api/auth/login
Body: { "username": "alice", "password": "alice123" }

200 OK     — { "token": "<jwt>", "username": "alice" }
401 Unauthorized — unknown user or wrong password
```

The server verifies the plaintext password against a BCrypt hash. On success it returns a signed JWT.
Token lifetime defaults to **8 hours** (configurable: `Auth:JwtExpiryHours` in `appsettings.json`).

# Securing a Request

```
Authorization: Bearer <token>
```

All export, ERP, schema, and pipeline endpoints require this header.
A missing or expired token returns `401 Unauthorized`.

# User Store

| Environment | Source |
|-------------|--------|
| Development | Hard-coded seed: `alice/alice123`, `bob/bob123` (BCrypt-hashed at startup by `DevAuthSeed`). |
| Production  | `Auth:Users` list in `appsettings.json` — each entry has `Username` and `PasswordHash` (BCrypt). |

## Generating a BCrypt Hash (Development Only)

```
POST /api/auth/hash
Body: { "password": "mysecretpassword" }

200 OK — { "hash": "$2a$11$..." }
```

This endpoint is only active in the Development environment. Use the returned hash as the
`PasswordHash` value when configuring production users in `appsettings.json`.

# Configuration

```json
{
  "Auth": {
    "JwtSecret": "<32+ char secret>",
    "JwtExpiryHours": 8,
    "Users": [
      { "Username": "alice", "PasswordHash": "$2a$11$..." },
      { "Username": "bob",   "PasswordHash": "$2a$11$..." }
    ],
    "ApiKeys": [
      { "Name": "erp-bot", "KeyHash": "<sha256 hex>" }
    ]
  }
}
```

`Auth:JwtSecret` must be set in all environments. In production, use an environment variable or
secrets manager — never commit the secret to source control.

# API Keys (Machine-to-Machine)

```
X-Api-Key: <raw key>
```

Checked by `ApiKeyAuthenticationHandler` (scheme name `"ApiKey"`) against `ApiKeyStore`, which holds
only the SHA-256 hash of each configured key — never the raw value, at rest or in memory. Unlike the
BCrypt check on login, this runs on every request rather than once, so it deliberately uses a fast,
constant-time hash comparison instead of BCrypt's intentionally slow one; that trade-off only holds
because a generated API key is already high-entropy, unlike a human-chosen password.

On a match, the configured `Name` becomes the request's identity — same as a JWT's username, so it
flows straight into the audit log and any `httpContext.User.Identity!.Name!` read.

| Environment | Source |
|-------------|--------|
| Development | Hard-coded seed: key `dev-local-api-key` → identity `dev-api-key` (`DevAuthSeed`, logged at startup). |
| Production  | `Auth:ApiKeys` list in `appsettings.json` — each entry has `Name` and `KeyHash`. |

## Generating a Key Hash

There's no dev endpoint for this (unlike BCrypt password hashes) — a raw SHA-256 needs no special
tooling to reproduce correctly:

```
openssl rand -hex 32                    # generate a random raw key
printf '%s' '<raw key>' | sha256sum     # hash it — this is what goes in KeyHash
```

Give the raw key to the calling system; store only the hash in `Auth:ApiKeys`.

## Opting an endpoint in

By default `RequireAuthorization()` only accepts the JWT Bearer scheme — adding the `ApiKey` scheme in
`Program.cs` does not change that for any existing endpoint. An endpoint accepts `X-Api-Key` only if it
explicitly says so:

```csharp
.RequireAuthorization(policy =>
    policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
);
```

# Authorisation Model (Iteration 1)

Any authenticated user can:
- List and view export runs.
- Trigger an on-demand run or preview.
- Act as Operator in the four-eyes release.
- Act as Approver — provided they are a different registered user than the Operator.

There is no role-based access control in Iteration 1.
Role separation (Operator vs Approver roles) is planned for Iteration 2.

The four-eyes constraint is enforced by identity (username from JWT must differ from the approver name),
not by organisational roles. In Iteration 1 any two registered users fulfil the constraint.

# Related

- [Four-Eyes Release](/processes/four-eyes-release.md) — uses the authenticated username as Operator
- [Open Points](/processes/open-points.md) — role-based auth is deferred to Iteration 2
