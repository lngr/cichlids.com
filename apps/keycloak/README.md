# Keycloak realm configuration

Each deployed environment has one realm file that Keycloak imports as it is
([ADR-0025](../../docs/adr/0025-keycloak-realm-configuration-per-environment.md)).

| Environment | File | Hostnames |
|-------------|------|-----------|
| staging | `overlays/staging/cichlids-realm.json` | `auth.staging.cichlids.com`, `api.staging.cichlids.com`, web `staging.cichlids.com` |
| production | `overlays/prod/cichlids-realm.json` | `auth.cichlids.com`, `api.cichlids.com`, web `cichlids.com` |

The realm file of the local stack, `app/stack/keycloak/cichlids-realm.json`, is for local
development only and is never deployed. It must not be copied into these directories.

The two files differ only in `displayName` and in the `redirectUris` and `webOrigins` of the
public client `cichlids-app`. Staging allows `cichlids://auth` and `https://staging.cichlids.com/auth`
with the web origin `https://staging.cichlids.com`, so testers can use a browser. Production
allows `cichlids://auth` and no web origin.

## Rules enforced by CI

`tools/check-keycloak-realm.py` runs in the `keycloak-realm` job and fails when a file breaks
one of these rules:

- `sslRequired` is `all`.
- `registrationAllowed` is not set. The key must stay out of the files, because re-applying a file
  would close a registration that the operator opened after the account import.
- The files contain no `users`, `credentials` or `federatedIdentities`.
- Every client sets `directAccessGrantsEnabled` to false, none enables the implicit flow, and
  public clients require PKCE `S256`.
- Redirect URIs are `https://` URLs or exactly `cichlids://auth`, without `*`. Web origins are
  exact `https://host` values without `+` or `*`.
- `verifyEmail` and `bruteForceProtected` are true and `duplicateEmailsAllowed` is not true.
- No local address, `.test` domain, development credential name or `exp://` URL appears.
- Both files are equal apart from the allowed differences above.

Run `python3 tools/check-keycloak-realm.py --self-test` and `python3 tools/check-keycloak-realm.py`
before committing a change to a realm file.

## Reserved usernames

The user profile of the realm carries a pattern validator on the username. It refuses
`community-archive`, `former-member-<id>` and `forum-member-<id>`, the names of the system and
placeholder profiles, on the registration form and in the admin API. The user profile is part of
the realm file, so both deployed files and the local realm file hold the same definition. The API
adds a second guard: a first login whose username is reserved gets a generated handle.

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
docker exec kc-check /opt/keycloak/bin/kcadm.sh get users/count -r cichlids
curl -s http://localhost:18080/realms/cichlids/account
docker rm -f kc-check
```

The realm shows `sslRequired` `all` and registration false, the client shows no password grant
and the staging URIs, the user count is 0, and the plain-HTTP request answers "HTTPS required".

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
