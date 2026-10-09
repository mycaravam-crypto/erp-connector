---
type: Business Process
title: Roles and Permissions
description: Admin and User roles, the admin-editable permission set that decides which menu items and actions a User has, how the server and the UI enforce it, and in-app user management.
resource: src/Connector.Api/Authorization/Permissions.cs
tags: [security, auth, rbac, permissions, users]
timestamp: 2026-10-09T00:00:00Z
---

Every interactive user has one of two roles:

* **Admin:** can see and do everything, including the admin-only areas: **Connect**, **Settings → Users** and
  **Settings → Permissions**.
* **User:** can see and do only what an Admin has ticked in **Settings → Permissions**.

Permissions are set per role, not per user. API keys (`X-Api-Key`, see
[Authentication](/api/authentication.md)) are machine identities and are not affected.

# Permission catalogue

The fixed list lives in `Permissions.cs`. It is grouped by menu item. A group's `.view` permission decides
whether the menu item is shown at all, and every other permission in the group implies it. The server adds the
implied `.view` when it stores the set, and the editor ticks it automatically.

| Menu item | Permission | Allows |
|---|---|---|
| Source Schema | `sourceSchema.view` | Browse the source tables and columns |
| CMDB Export Mapping | `exportMapping.view` | See the mapping, presets and preview |
| | `exportMapping.edit` | Change the mapping, create or delete presets |
| Managed Export | `managedExport.view` | See runs and their details |
| | `managedExport.run` | Start a run ("Run Now") |
| | `managedExport.release` | Release as operator, or approve as the second person |
| | `managedExport.deliver` | Record the delivery |
| | `managedExport.skip` | Skip a pending or failed run |
| Export Jobs | `exportJobs.view` | See jobs, run history, preview |
| | `exportJobs.create` | Create and duplicate |
| | `exportJobs.edit` | Change, switch on or off |
| | `exportJobs.delete` | Delete |
| | `exportJobs.test` | Capped test run (50 records, no file) |
| | `exportJobs.download` | Full run, downloading the file |
| Import Jobs | `importJobs.view` | See jobs, runs, preview, diffs |
| | `importJobs.create` | Create, duplicate, suggest from an export |
| | `importJobs.edit` | Change, switch on or off |
| | `importJobs.delete` | Delete |
| | `importJobs.run` | Upload a file and stage it as a run |
| | `importJobs.release` | Release or reject a staged import (four-eyes) |
| ICD Schema | `icdSchema.view` | See the ICD column contract |
| Audit Log | `audit.view` | Browse the audit log |
| Settings | `settings.scheduler`, `settings.branding`, `settings.gdpr`, `settings.instance` | Each settings section separately |

Until an Admin saves the Permissions screen for the first time, the User role gets view-only access to
everything except Connect, Settings and the audit log (`Permissions.DefaultUserPermissions`).

There is no separate download permission for Managed Export: its runs are written to the staging folder and
never downloaded through the UI. The only download in the app is an Export Job's full run, which is
`exportJobs.download`.

# Enforcement

* **Server:** every `/api` endpoint declares what it needs, with `.RequirePermission(...)` (at least one of the
  given permissions) or `.RequireAdmin()`. `AuthorizationHttpTests.EveryApiEndpoint_DeclaresAPermissionOrIsExplicitlyListed`
  fails when an endpoint has neither and isn't on its short list of endpoints open to every signed-in user
  (`auth/me`, `auth/change-password`, `auth/revoke-my-sessions`, `connection/status`) or reachable without a
  session.
* **Role is read on every request:** the JWT carries only the username. `OnTokenValidated` in `Program.cs`
  loads the user from the `User` table and adds the current role as a claim. A deleted user's tokens stop
  working at once, and a role change applies to the next request.
* **Permissions are read on every request:** they are stored as one `AppSetting` row (`role_permissions`) and
  cached in memory by `RolePermissionStore`. The cache is dropped on save, so a change applies to the next
  request.
* **UI:** `GET /api/auth/me` returns the user's role and effective permissions. The router reloads it on every
  navigation. Routes carry `meta.access`, and a route the user may not open sends them to the dashboard. Nav
  links, dashboard tiles, buttons and settings sections are hidden without their permission. Edit pages open
  read-only without the edit permission. Hiding is only a convenience: the server checks every request.

# Four-eyes release

Operator and approver must still be two different users. Both must also hold the release permission
(`managedExport.release` or `importJobs.release`). The operator's permission is checked by the endpoint's
policy, and the approver's by `FourEyesReview.ValidateApproverAsync`. Admins always hold both. The Permissions
screen warns when fewer than two people could release, because then nothing could ever be released.

# Users

Users live in the `User` table (username unique and case-insensitive, BCrypt hash, role). Admins manage them in
**Settings → Users**:

* add a user with an initial password (at least 10 characters) and role;
* switch a user between Admin and User;
* reset a password, which signs that user out everywhere;
* delete a user, whose tokens stop working at once; their audit entries stay.

An Admin can't change their own role or delete themselves, and the last Admin can't be demoted or deleted.
Every user can change their own password from the user menu, which signs them out everywhere. All changes are
audited (`user_created`, `user_updated`, `user_deleted`, `password_changed`, `permissions_updated`).

## Seeding

While the `User` table is empty, startup fills it once:

* **Development:** `alice/alice123` and `bob/bob123` (Admins), `carol/carol123` (User).
* **Production:** `Auth:Users`. Each entry may set `"Role": "Admin"` or `"User"`. An entry without a role
  becomes Admin, so an existing deployment keeps every user's access after the upgrade.

After that, `Auth:Users` is ignored and users are managed in the UI. In Production, startup fails when no Admin
exists.
