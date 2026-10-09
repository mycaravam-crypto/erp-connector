---
type: Plan
title: Role-Based Access Control (Admin / User)
description: Plan for an Admin and a User role, with an admin-editable permission matrix that controls which menu items and actions a normal user may see and use.
tags: [security, auth, rbac, permissions, roadmap]
timestamp: 2026-10-09T00:00:00Z
---

**Status: planned, not implemented.**

# 1. Goal

Today every authenticated user can do everything. We want:

* two roles, **Admin** and **User**;
* Admins can always see and do everything;
* a normal **User** sees only the menu items, and can only use the actions (create, edit, run,
  release, ...), that an Admin has switched on for them;
* an Admin edits this in a new **Settings → Permissions** section, as a checklist.

The server enforces every rule. The UI hides what the user can't use, but hiding is only for
convenience and is never the actual protection.

# 2. Current state (what we build on)

| Area | Today |
|---|---|
| Users | Configured in `Auth:Users` (username + BCrypt hash); `DevAuthSeed` provides alice/bob in Development. No user table. |
| Token | JWT with `Name` + `iat` claims only (`AuthEndpoints.cs`). |
| Authorization | Every endpoint uses a plain `.RequireAuthorization()`. There are no roles or policies apart from the API-key scheme on `/api/pipeline/run/{name}`. |
| Settings store | `AppSettings` key/value table (`db.GetSettingAsync` / `SettingsKeys`), already used for scheduler, branding, GDPR denylist. |
| UI nav | `ConnectorNav.vue` (6 main links) + `UserMenu.vue` (ICD Schema, Settings, Audit Log). Routes in `router/index.ts`. |

# 3. Design

## 3.1 Roles

* Add `Role` (`Admin` | `User`) to `AuthUser` in `Auth:Users`. Dev seed: `alice` = Admin, `bob` = User.
* **Backward compatibility:** if a configured user has no `Role`, it is treated as **Admin** and a
  startup warning is logged. Existing deployments therefore keep working without a lockout.
  This default can be tightened later (see open questions).
* At startup, check that **at least one Admin** is configured. Fail in Production if there is none.
* The JWT gets a `role` claim. **Permissions are not put in the token.** They are resolved from the
  DB on every request (cached in memory and invalidated when they are saved), so an Admin's change
  takes effect at once instead of after the 8 h token expiry.
* User management (adding users, changing roles) stays in configuration for now. A user table and a
  user-management UI are a separate, later step (see §6).

## 3.2 Permission catalogue

These are fixed strings defined in code (`Permissions.cs`). They are grouped by menu item, and each
group has a `*.view` permission that decides whether the menu item is shown at all.

| Menu item | Permission | Grants |
|---|---|---|
| Connect | `connection.view` | See connection settings (secrets always masked) |
| | `connection.edit` | Save / test the connection |
| Source Schema | `sourceSchema.view` | Browse live source schema |
| CMDB Export Mapping | `exportMapping.view` | See mapping + presets |
| | `exportMapping.edit` | Change mapping, create/delete presets |
| Managed Export | `managedExport.view` | List runs, view details, download files |
| | `managedExport.run` | Trigger run / preview |
| | `managedExport.release` | Approve (four-eyes) a run |
| | `managedExport.deliver` | Record delivery / skip a run |
| Export Jobs | `exportJobs.view` | List definitions, runs, preview |
| | `exportJobs.create` | Create / duplicate a definition |
| | `exportJobs.edit` | Edit, enable/disable a definition |
| | `exportJobs.delete` | Delete a definition |
| | `exportJobs.run` | Run / test a definition on demand |
| Import Jobs | `importJobs.view` | List definitions, runs, preview, diffs |
| | `importJobs.create` | Create / duplicate / suggest-from-export |
| | `importJobs.edit` | Edit, enable/disable |
| | `importJobs.delete` | Delete |
| | `importJobs.run` | Upload / start an import run |
| | `importJobs.release` | Approve or reject a staged import plan (four-eyes) |
| ICD Schema | `icdSchema.view` | See ICD schema / excluded fields |
| Audit Log | `audit.view` | Browse the audit log |
| Settings | `settings.scheduler` | Edit scheduler / retention |
| | `settings.branding` | Edit branding |
| | `settings.gdpr` | Edit GDPR denylist |
| | `settings.instance` | View / regenerate instance identity |
| *(admin only, not grantable)* | — | Settings → Permissions; anything security-critical we add later |

Rules:

* Any action permission **implies its group's `.view`**. The editor ticks `.view` automatically, and
  the server computes the effective set the same way, so a user can't have "edit" without "view".
* Settings is shown in the user menu if the user has at least one `settings.*` permission. Only
  the sections they hold are shown.
* **Default for the User role** (the first time, before an Admin has saved anything): view-only on
  everything except Connect, Settings and Audit. All action permissions are off.

## 3.3 Storage

* One `AppSettings` row, key `SettingsKeys.RolePermissions`, value
  `{ "User": ["exportJobs.view", "exportJobs.run", ...] }`.
* It is keyed by role so further roles can be added later without migrating the data. No EF
  migration is needed.
* Unknown strings are ignored when the row is read, so a permission that was renamed or removed can't
  grant anything.

## 3.4 Backend enforcement

* `PermissionRequirement(string permission)` + `PermissionAuthorizationHandler`: Admin → succeed;
  otherwise look up the role's effective set via `IPermissionService` (cached).
* Register one policy per permission (`perm:exportJobs.create`, ...) and a helper
  `.RequirePermission(Permissions.ExportJobs.Create)` that replaces `.RequireAuthorization()` on
  each endpoint. All ~60 endpoints in `src/Connector.Api/Endpoints/*` get mapped explicitly.
* **No default open door:** a test enumerates every mapped endpoint and fails if any authenticated
  endpoint has no permission (or an explicit `RequireAdmin` / `AllowAnyAuthenticated` marker).
  `/api/auth/*`, `/api/health`, `/api/version` and `GET /api/branding` stay as they are.
* **API keys** (`/api/pipeline/run/{name}`) are not affected. They keep their own scheme, because
  they are machine identities, not users.
* **Four-eyes stays as it is:** two *different* users are still required. In addition, both now need
  the `*.release` permission (Admins always have it). The Permissions screen shows a warning when
  release is switched off for Users and fewer than two Admins are configured, because then nothing
  could ever be released.

## 3.5 New endpoints

| Endpoint | Who | Purpose |
|---|---|---|
| `GET /api/auth/me` | any authenticated | `{ username, role, permissions[] }`: the effective set the UI uses |
| `GET /api/settings/permissions` | Admin | Catalogue (groups, labels, descriptions) + current User set |
| `PUT /api/settings/permissions` | Admin | Save the User set; audited as `permissions_changed` with a before/after diff |

## 3.6 Frontend

* `api/auth.ts`: after login, call `/api/auth/me` and keep the result in a small reactive store
  (`usePermissions()` → `can(perm)`, `isAdmin`). It is re-fetched on page reload, since the session
  lives in `sessionStorage`.
* `ConnectorNav.vue` / `UserMenu.vue`: each link gets a `permission` field and is filtered with
  `can()`. The divider logic (`idx === 3`) is replaced with an explicit group split so it still
  works when links are hidden.
* `router/index.ts`: routes get `meta.permission`. `beforeEach` sends the user to the first route
  they are allowed to open (or to a "no access" page) instead of rendering a view whose API calls
  would all fail with 403. The dashboard falls back the same way.
* Buttons: "New export job", "Run", "Delete", "Release", "Deliver", "Save" etc. are hidden or
  disabled with `v-if="can('exportJobs.create')"`. Edit views open **read-only** when the user has
  `.view` but not `.edit`.
* `api/*.ts`: handle a 403 globally with a toast ("You don't have permission for this action").
  This covers the case where permissions change while the user is on the page.
* **Settings → Permissions** (Admin only): one card per menu item with a checkbox per permission and
  its description. It has "Select all / none" per group, ticks `.view` automatically, shows the
  four-eyes warning from §3.4, and uses the usual `SaveStatusAlert`.
* Header: show the role next to the username in `UserMenu`.

# 4. Implementation phases

Each phase can be released on its own and leaves the app working.

1. **Roles + claims:** `Role` on `AuthUser`, dev seed, startup validation, `role` claim,
   `GET /api/auth/me`. No behaviour change yet.
2. **Permission model + server enforcement:** catalogue, storage, handler/policies, all endpoints
   mapped, endpoint-coverage test, `/api/settings/permissions`, audit entry.
3. **UI gating:** permission store, nav/menu filtering, route guards, button gating, read-only edit
   views, 403 toast.
4. **Admin screen:** Settings → Permissions editor + four-eyes warning.
5. **Docs:** README (Features → Governance, Configuration → `Auth:Users[].Role`), `knowledge/api`
   auth page, `knowledge/security`. Move this page out of `planning/` once it is done.

# 5. Tests

* xUnit: handler (Admin bypass, granted/denied, implied `.view`, unknown strings ignored), cache
  invalidation on save, one allowed/denied test per endpoint group, endpoint-coverage test,
  startup fails without an Admin in Production, four-eyes with a User lacking `*.release`.
* Vitest: `usePermissions`, nav/menu filtering, router redirects, the Permissions editor
  (auto-tick `.view`, save payload), and button visibility on ExportDefinitionsView /
  ImportDefinitionsView / ExportDetail.
* Playwright: log in as `bob` (User) with a restricted set → hidden menu items, direct URL redirects,
  no "New" button; log in as `alice` (Admin) → change the set → `bob` sees the change after a reload.

# 6. Open questions

1. **Default role for users without `Role`:** Admin (non-breaking, proposed) or User (safer, but
   needs a config change on upgrade)?
2. **Per-user overrides:** is per-role enough, or should individual users get extra or fewer
   permissions? The storage format allows adding this later.
3. **User management in the UI** (create users, reset passwords, assign roles): this needs a user
   table instead of `Auth:Users`. It is a separate, larger change. Do we want it?
4. **Downloading export files:** should this be its own permission (`managedExport.download`),
   separate from viewing the run list?
5. Should a User ever be able to see the **Connect** page (read-only, secrets masked), or should it
   stay Admin-only?
