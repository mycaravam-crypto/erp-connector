---
type: Business Process
title: Authentication
description: JWT Bearer authentication protecting all API routes; BCrypt-hashed user store with dev seed and production configuration. Plus an opt-in X-Api-Key scheme for machine-to-machine callers.
resource: src/Connector.Api/Auth.cs
tags: [process, auth, jwt, security, bcrypt, api-key]
timestamp: 2026-09-03T00:00:00Z
---

All API routes except `POST /api/auth/login` require a valid JWT Bearer token, issued at login,
signed with a shared secret, carrying the username as the `Name` claim. One endpoint
(`POST /api/pipeline/run/{name}` — see [On-Demand Run](/api/on-demand-run.md)) additionally
accepts an `X-Api-Key` header for a "dedicated API user" that shouldn't need to log in
interactively; every other endpoint is unaffected by the `ApiKey` scheme's existence.

# Login

```
POST /api/auth/login
Body: { "username": "alice", "password": "alice123" }

200 OK     — { "token": "<jwt>", "username": "alice" }
401 Unauthorized — unknown user or wrong password
```

The server verifies the password against a BCrypt hash and returns a signed JWT. Token lifetime
defaults to **8 hours** (`Auth:JwtExpiryHours` in `appsettings.json`).

# Securing a Request

```
Authorization: Bearer <token>
```

All export, ERP, schema, and pipeline endpoints require this header. Missing or expired → `401`.

# User Store

| Environment | Source |
|-------------|--------|
| Development | Hard-coded seed: `alice/alice123`, `bob/bob123` (BCrypt-hashed at startup by `DevAuthSeed`). |
| Production  | `Auth:Users` list in `appsettings.json` — `Username` + `PasswordHash` (BCrypt). |

## Generating a BCrypt Hash (Development Only)

```
POST /api/auth/hash
Body: { "password": "mysecretpassword" }

200 OK — { "hash": "$2a$11$..." }
```

Development-only. Use the returned hash as `PasswordHash` when configuring production users.

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

`Auth:JwtSecret` must be set everywhere. In production use an environment variable or secrets
manager — never commit it.

# API Keys (Machine-to-Machine)

```
X-Api-Key: <raw key>
```

Checked by `ApiKeyAuthenticationHandler` (scheme `"ApiKey"`) against `ApiKeyStore`, which holds
only the SHA-256 hash of each key, never the raw value. Unlike BCrypt on login, this runs on
every request, so it uses a fast constant-time hash comparison rather than BCrypt's deliberately
slow one — safe only because a generated API key is already high-entropy, unlike a password.

On a match, the configured `Name` becomes the request identity, flowing into the audit log and
`httpContext.User.Identity!.Name!` exactly like a JWT username.

| Environment | Source |
|-------------|--------|
| Development | Hard-coded seed: key `dev-local-api-key` → identity `dev-api-key` (`DevAuthSeed`, logged at startup). |
| Production  | `Auth:ApiKeys` list — `Name` + `KeyHash`. |

## Generating a Key Hash

No dev endpoint for this — a raw SHA-256 needs no special tooling:

```
openssl rand -hex 32                    # generate a random raw key
printf '%s' '<raw key>' | sha256sum     # hash it — this is what goes in KeyHash
```

Give the raw key to the calling system; store only the hash.

## Opting an endpoint in

Adding the `ApiKey` scheme in `Program.cs` doesn't change `RequireAuthorization()`'s default
(JWT-only) for any existing endpoint. An endpoint accepts `X-Api-Key` only if it opts in
explicitly:

```csharp
.RequireAuthorization(policy =>
    policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
);
```

# Authorisation Model (Iteration 1)

Any authenticated user can list/view export runs, trigger an on-demand run or preview, and act as
Operator or Approver in the four-eyes release (provided Operator ≠ Approver). No role-based
access control yet — role separation is planned for Iteration 2. The four-eyes constraint is
enforced by identity (JWT username), not organisational role; any two registered users satisfy it.

# Related

- [Four-Eyes Release](/operations/four-eyes-release.md) — uses the authenticated username as Operator
- [Open Points](/planning/open-points.md) — role-based auth is deferred to Iteration 2
