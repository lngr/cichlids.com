# Keycloak realm configuration

Each deployed environment has one realm file that Keycloak imports as it is
([ADR-0025](../../docs/adr/0025-keycloak-realm-configuration-per-environment.md)).

| Environment | File | Hostnames |
|-------------|------|-----------|
| staging | `overlays/staging/cichlids-realm.json` | `auth.staging.cichlids.com`, `api.staging.cichlids.com`, web `staging.cichlids.com` |
| production | `overlays/prod/cichlids-realm.json` | `auth.cichlids.com`, `api.cichlids.com`, web `cichlids.com` |

The realm file of the local stack, `app/stack/keycloak/cichlids-realm.json`, is for local
development only and is never deployed. It must not be copied into these directories.

Two clients sign members in
([ADR-0027](../../docs/adr/0027-session-model-for-native-and-web-clients.md)):

- `cichlids-app`, the public client of the native app, allows `cichlids://auth` in both files.
- `cichlids-web-bff`, the confidential client through which the API signs in members of the web
  app (backend for frontend). It has the standard flow only, requires PKCE `S256` and offers
  `offline_access`. Staging allows the web login callback of the API,
  `https://staging.cichlids.com/api/auth/callback`, and the post logout redirect URI
  `https://staging.cichlids.com/me`, the Me tab that the browser returns to after a sign-out.
  Production lists no redirect URI, so the client cannot complete a login there. The web app and
  the API share one origin, so the client needs no web origin.

The two files differ only in `displayName`, in the `redirectUris` and `webOrigins` of both clients
and in the post logout redirect URIs of `cichlids-web-bff`.

### Client secret of the web backend

The files carry no client secret. Keycloak gives `cichlids-web-bff` a random secret when it
imports a file. The secret that counts is the one in the secret store: a SOPS-encrypted Secret
holds it for the API (configuration key `WebSession:ClientSecret`), and the operator sets the same
value in Keycloak after every import of a realm file:

```sh
id=$(kcadm.sh get clients -r cichlids -q clientId=cichlids-web-bff --fields id --format csv --noquotes)
kcadm.sh update "clients/$id" -r cichlids --merge -f - <<JSON
{"secret": "$WEB_BFF_CLIENT_SECRET"}
JSON
```

`WEB_BFF_CLIENT_SECRET` holds the decrypted value from the secret store; the heredoc keeps it out
of the process list. While the secret in Keycloak differs from the API's, web logins and renewals
of web sessions fail; the stored web sessions stay.

## Rules enforced by CI

`tools/check-keycloak-realm.py` runs in the `keycloak-realm` job and fails when a file breaks
one of these rules:

- `sslRequired` is `all`.
- `registrationAllowed` is not set. The key must stay out of the files, because re-applying a file
  would close a registration that the operator opened after the account import.
- The files contain no `users`, `credentials` or `federatedIdentities`.
- Every client sets `directAccessGrantsEnabled` to false, none enables the implicit flow, and
  public clients require PKCE `S256`.
- No client carries a `secret`.
- `cichlids-web-bff` exists, is confidential (`publicClient` false, not bearer-only) with the
  `client-secret` authenticator, requires PKCE `S256`, enables the standard flow and has neither a
  service account nor the device grant.
- Redirect URIs are `https://` URLs or exactly `cichlids://auth`, without `*`. Web origins are
  exact `https://host` values without `+` or `*`.
- `verifyEmail` and `bruteForceProtected` are true and `duplicateEmailsAllowed` is not true.
- `offlineSessionMaxLifespanEnabled` is false, `offlineSessionIdleTimeout` is at least ten years
  and `cichlids-app` and `cichlids-web-bff` offer the `offline_access` client scope.
- No local address, `.test` domain, development credential or secret name or `exp://` URL appears.
- Both files are equal apart from the allowed differences above.

Run `python3 tools/check-keycloak-realm.py --self-test` and `python3 tools/check-keycloak-realm.py`
before committing a change to a realm file.

## Reserved usernames

The user profile of the realm carries a pattern validator on the username. It refuses
`community-archive`, `former-member-<id>` and `forum-member-<id>`, the names of the system and
placeholder profiles, on the registration form and in the admin API. The user profile is part of
the realm file, so both deployed files and the local realm file hold the same definition. The API
adds a second guard: a first login whose username is reserved gets a generated handle.

## Offline sessions

Members stay signed in on a device until they sign out
([ADR-0027](../../docs/adr/0027-session-model-for-native-and-web-clients.md)). The app and the web
backend request `offline_access`; offline sessions have no maximum lifespan and an idle timeout of
ten years (`315360000` seconds). A user needs the `offline_access` realm role for that scope.
Users created through the admin API or the registration form receive it through the default role
`default-roles-cichlids`; `etl keycloak-import` assigns that role to every account it creates,
since a partial import grants no default roles. A user without the role cannot sign in to the app.

## Check a fresh import

```sh
docker run -d --name kc-check -p 18080:8080 \
  -e KC_BOOTSTRAP_ADMIN_USERNAME=admin -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin \
  -v "$PWD/apps/keycloak/overlays/staging:/opt/keycloak/data/import:ro" \
  quay.io/keycloak/keycloak:26.6.4 start-dev --import-realm
docker exec kc-check /opt/keycloak/bin/kcadm.sh config credentials \
  --server http://localhost:8080 --realm master --user admin --password admin
docker exec kc-check /opt/keycloak/bin/kcadm.sh get realms/cichlids --fields sslRequired,registrationAllowed
docker exec kc-check /opt/keycloak/bin/kcadm.sh get clients -r cichlids -q clientId=cichlids-app \
  --fields directAccessGrantsEnabled,redirectUris,webOrigins
docker exec kc-check /opt/keycloak/bin/kcadm.sh get clients -r cichlids -q clientId=cichlids-web-bff \
  --fields publicClient,clientAuthenticatorType,redirectUris
docker exec kc-check /opt/keycloak/bin/kcadm.sh get users/count -r cichlids
curl -s http://localhost:18080/realms/cichlids/account
docker rm -f kc-check
```

The realm shows `sslRequired` `all` and registration false, `cichlids-app` shows no password grant
and `cichlids://auth`, `cichlids-web-bff` a confidential client with the `client-secret`
authenticator and the staging callback, the user count is 0, and the plain-HTTP request answers "HTTPS required".

## Accounts

The files contain no accounts. Create operator and tester accounts with kcadm:

```sh
kcadm.sh create users -r cichlids -s username=<name> -s email=<email> -s enabled=true -s emailVerified=true
kcadm.sh set-password -r cichlids --username <name> --new-password '<password>'
kcadm.sh add-roles -r cichlids --uusername <name> --rolename admin
```

Test accounts get the realm role `tester` instead (`--rolename tester`). The staging reset keeps
every account with the realm role `admin` or `tester` and deletes all others.

## Registration

Registration opens at runtime, after the account import.

- Production go-live order: apply the realm, run `etl keycloak-import --open-registration`
  (it opens registration only when no account is missing), then create the admin accounts.
- Staging: registration stays closed. Staging imports run without `--open-registration`, and
  an operator creates the tester accounts.

## Constraints for the Keycloak deployment

- Run Keycloak with `KC_PROXY_HEADERS=xforwarded` and an https `KC_HOSTNAME`. With
  `sslRequired` `all`, wrong forwarding headers make every login fail with "HTTPS required".
- Provide SMTP settings from a SOPS secret. Email verification and password resets need them.
- Provide identity provider secrets for Google and Facebook from secrets. The files carry the
  placeholder `not-configured` and both providers are disabled.
- Do not add `registrationAllowed` to a realm file.
