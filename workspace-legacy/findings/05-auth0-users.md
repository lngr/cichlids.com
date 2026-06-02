# Auth0 User Export — Forensic Analysis

**Source file:** `/workspaces/_extract/auth0-cichlids.json` (4,455,556 bytes ≈ 4.3 MB)
**Analysis date:** 2026-06-02
**Method:** `jq` (the host Python `json` stdlib module is missing/broken, so jq was used throughout)

---

## 1. Format & Record Count

- **Format:** NDJSON (newline-delimited JSON — one JSON object per line). Not a JSON array.
- **Total user records:** **12,147** (line count = 12,147; `jq -s 'length'` = 12,147; file ends with a complete, non-truncated object).

This is the Auth0 **"human-readable" user export** (capitalized, space-separated keys such as `Email Verified`). This is the *report* export, **not** the importable bulk-user JSON format. Critically, this format does **not** carry password hashes or `user_metadata`/`app_metadata`.

## 2. Fields Present (union of all keys)

Only 11 distinct top-level keys exist across the entire dataset. No nested objects/arrays anywhere (`jq` confirmed 0 object/array values).

| Key            | Records present | Notes |
|----------------|-----------------|-------|
| `Id`           | 12,147 (100%)   | e.g. `auth0|<24-hex>`, `google-oauth2|<n>`, `facebook|<n>` |
| `Connection`   | 12,147 (100%)   | identity provider |
| `Created At`   | 12,147 (100%)   | ISO-8601 |
| `Updated At`   | 12,147 (100%)   | ISO-8601 (NOT a real "last login") |
| `Email Verified`| 12,147 (100%)  | boolean |
| `Picture`      | 12,147 (100%)   | mostly Gravatar URLs |
| `Email`        | 12,121 (99.8%)  | 26 records have no email |
| `Nickname`     | 972 (8.0%)      | |
| `Name`         | 900 (7.4%)      | |
| `Given Name`   | 566 (4.7%)      | (social logins) |
| `Family Name`  | 551 (4.5%)      | (social logins) |

**Absent / NOT in this export:** no password/hash/salt field, no `user_metadata`, no `app_metadata`, no `username`, no `last_login`/`logins_count`, no `phone`, no MFA data.

### Anonymized example record (password-database user)

```json
{
  "Id": "auth0|aaaaaaaaaaaaaaaaaaaaaaaa",
  "Nickname": "Jane Doe",
  "Name": "user@example.com",
  "Email": "user@example.com",
  "Email Verified": true,
  "Picture": "https://secure.gravatar.com/avatar/<md5>?s=480&r=pg&d=...",
  "Connection": "Username-Password-Authentication",
  "Created At": "2018-03-24T14:36:48.335Z",
  "Updated At": "2023-08-07T16:17:39.606Z"
}
```

*(No password-hash field exists to redact — see §3.)*

## 3. Password Storage — CRITICAL FINDING

- **Password hashes are NOT included in this export.** Verified by `grep -E '\$2[aby]\$[0-9]{2}\$'` → **0 bcrypt hashes**; no `hash`/`salt`/`password` fields exist. (The only `password`-substring hits are the connection name `Username-Password-Authentication`; the only `hash=` hits are inside Facebook profile-picture URLs.)
- **Algorithm:** Auth0 stores Database-Connection passwords as **bcrypt** internally, but bcrypt hashes are *only* available via the separate, support-gated **"Bulk User Export" / `users-export` job that requires explicit hash inclusion** (or a support ticket). They are **not present in this file.**
- **Importable into a new auth system?** **No — not from this file.** Without the bcrypt hashes, the 11,575 password users **cannot** be migrated silently.

**Migration options for password users (no hashes available):**
1. Request the password-hash export from Auth0 (support process) — if obtained, bcrypt is fully portable into any modern auth backend.
2. Otherwise: import accounts as profile shells and force a **password reset / "claim your account" email** flow on first login. Email is verified for 99.2% of users, so reset-link delivery is viable.

## 4. Identity Providers / Connections

| Connection                       | Count  | Share  | Has password? |
|----------------------------------|--------|--------|---------------|
| `Username-Password-Authentication` | 11,575 | 95.3% | Yes (hash NOT exported) |
| `google-oauth2`                  | 320    | 2.6%   | No (social)   |
| `facebook`                       | 252    | 2.1%   | No (social)   |

The `Id` prefix matches the `Connection` 1:1 (11,575 `auth0|`, 320 `google-oauth2|`, 252 `facebook|`).

## 5. Useful Profile Fields for the New App

- **email** — present for 12,121/12,147 (26 missing).
- **email_verified** — present, mostly true (see §6).
- **created_at** — full range **2018-03-24 → 2023-09-19** (community migrated to Auth0 in March 2018; last new signups Sept 2023).
- **last_login** — **NOT available.** Only `Updated At` exists (range 2018-03-24 → 2023-09-20); this reflects profile mutations, not logins, and cannot be trusted as activity signal.
- **picture** — present for all; 96% Gravatar (re-derivable from email), social logins use FB/Google CDN URLs (likely now stale/expired, e.g. `fbsbx.com` signed URLs).
- **nickname / name / given/family name** — sparse (8% / 7%), useful where present.

### Legacy TYPO3 user-id link — CRITICAL FINDING

- **No legacy/numeric TYPO3 user-id field exists anywhere in this export.**
- There is **no** `user_metadata`/`app_metadata` (the usual place such a link would live).
- Auth0 `Id` suffixes are **24-char hex Mongo ObjectIDs** (all 11,575 auth0 users), **not** numeric TYPO3 IDs. `jq` found **0** auth0 IDs that are purely numeric.
- `Nickname` values: only **1** of 972 is purely numeric (`"3082618"`) — an isolated coincidence, not a systematic legacy-id mapping.
- **Conclusion: this file alone cannot link an Auth0 account to a TYPO3 `user_pics/<id>` photo folder.** The only join key available is **email** (or possibly nickname), which would have to be matched against the TYPO3/MySQL user table to recover the numeric legacy ID.

## 6. Key Counts

| Metric | Count | Notes |
|--------|-------|-------|
| Total users | 12,147 | |
| Email verified (`true`) | 12,052 (99.2%) | |
| Email NOT verified (`false`) | 95 | all are password users |
| Records with no `Email` at all | 26 | |
| Social-only (no password: google + facebook) | **572** (4.7%) | migrate via OAuth re-link |
| Password-connection users | 11,575 (95.3%) | hashes NOT in this file |
| Users with a legacy-TYPO3-id link field | **0** | no such field exists |
| Distinct emails appearing on >1 connection | 75 | same person, multiple login methods (potential account-merge needed) |

**Cross-connection duplicate emails (75 groups, candidates for account linking):**
- password + facebook: 34
- password + google: 37
- password + facebook + google: 2
- facebook + google: 2

## 7. Migratability Assessment

| Aspect | Verdict |
|--------|---------|
| **Profiles (email, verified status, dates, picture)** | Easily migratable — clean, well-formed, 99%+ have email. |
| **Password users (95%)** | **Blocked** from silent migration — no hashes in this export. Either obtain the bcrypt-hash export from Auth0 (then fully portable) or force password-reset on first login. |
| **Social users (5%, 572)** | Migratable only by re-authenticating against Google/Facebook OAuth in the new app (no secret to migrate; the FB/Google `Id` and email allow re-linking). Note: stored picture URLs for social users are likely expired. |
| **Linking to TYPO3 photo folders** | **Not possible from this file** — no numeric legacy ID present. Must join on **email** (and/or nickname) against the legacy MySQL user table to recover the `user_pics/<id>` mapping. This dependency should be flagged for the DB-restore work. |
| **Account de-duplication** | 75 emails span multiple connections — plan a merge strategy keyed on email. |

### Recommended next steps
1. Decide on the password path: pursue the Auth0 bcrypt-hash export, or commit to a reset-on-first-login flow (low risk given 99% verified emails).
2. Treat **email** as the migration primary key; build the TYPO3-userid mapping by joining Auth0 email → legacy MySQL user table → `user_pics/<id>`.
3. Plan the 75 multi-connection email merges and the 26 email-less records (manual review).
