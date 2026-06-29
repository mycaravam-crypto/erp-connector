---
type: Business Process
title: Authentication
description: JWT Bearer authentication protecting all API routes; BCrypt-hashed user store with dev seed and production configuration.
resource: src/Connector.Api/Auth.cs
tags: [process, auth, jwt, security, bcrypt]
timestamp: 2026-06-28T00:00:00Z
---

All API routes except `POST /api/auth/login` require a valid JWT Bearer token.
The token is issued at login, signed with a shared secret, and carries the username as the `Name` claim.

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
    ]
  }
}
```

`Auth:JwtSecret` must be set in all environments. In production, use an environment variable or
secrets manager — never commit the secret to source control.

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
