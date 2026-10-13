#!/usr/bin/env python3
"""Asserts that the deployed Keycloak realm files carry no local development setting.

The realm file under app/stack/keycloak is for the local stack only. Staging and production
each have their own realm file under apps/keycloak/overlays/<environment>/ (ADR-0010), which
Keycloak imports as it is. This check reads those files directly and fails when one of them
contains a setting that only the local stack may have:

  - sslRequired is not "all";
  - registrationAllowed is present (registration is opened at runtime after the account import);
  - users, credentials or federated identities are present;
  - a client allows the password grant or the implicit flow, or a public client lacks PKCE S256;
  - a client carries a secret (the operator sets client secrets in Keycloak);
  - the confidential client of the web backend is missing, is public, authenticates with anything
    but a client secret, lacks PKCE S256 or the standard flow, or has a service account or the
    device grant;
  - a redirect URI, post logout redirect URI, web origin, root, base or admin URL of a client, or
    the frontend URL of the realm is a wildcard, a local address or not an exact https value;
  - e-mail verification or brute force protection is off, or duplicate e-mails are allowed;
  - a local address, a test domain or a development credential name appears anywhere;
  - the two deployed files differ in more than the display name, the redirect URIs and web
    origins of the app client and the web backend client, and the post logout redirect URIs of
    the web backend client.

It also fails when the offline sessions of a deployed realm expire: a maximum lifespan is
enabled, the idle timeout is shorter than ten years, or the app client or the web backend client
cannot request the offline_access scope. And it asserts that the local realm file announces itself as local
development only.
Python standard library only. `--self-test` exercises every rule against fixtures so the guard
cannot silently rot.
Story: task-2.27
"""
import copy
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DEV_REALM = os.path.join(ROOT, "app", "stack", "keycloak", "cichlids-realm.json")
DEPLOYED_REALMS = {
    "staging": os.path.join(ROOT, "apps", "keycloak", "overlays", "staging", "cichlids-realm.json"),
    "prod": os.path.join(ROOT, "apps", "keycloak", "overlays", "prod", "cichlids-realm.json"),
}

APP_CLIENT = "cichlids-app"
BFF_CLIENT = "cichlids-web-bff"
LOCAL_ONLY_MARKER = "local development only"
FORBIDDEN_TEXT = (
    (re.compile(r"localhost", re.I), "localhost"),
    (re.compile(r"127\.0\.0\.1"), "127.0.0.1"),
    (re.compile(r"\.test\b", re.I), "a .test domain"),
    (re.compile(r"dev-password", re.I), "dev-password"),
    (re.compile(r"dev-user", re.I), "dev-user"),
    (re.compile(r"dev-mod", re.I), "dev-mod"),
    (re.compile(r"dev-secret", re.I), "dev-secret"),
    (re.compile(r"exp://", re.I), "exp://"),
    (re.compile(re.escape(LOCAL_ONLY_MARKER), re.I), LOCAL_ONLY_MARKER),
)
# Members stay signed in on a device until they sign out (ADR-0027): an offline session has no
# maximum lifespan and an idle timeout far beyond any realistic pause.
MIN_OFFLINE_IDLE_SECONDS = 10 * 365 * 24 * 60 * 60
OFFLINE_SCOPE = "offline_access"
HTTPS_URI = re.compile(r"^https://[A-Za-z0-9.-]+(:\d+)?(/[^\s?#]*)?$")
HTTPS_ORIGIN = re.compile(r"^https://[A-Za-z0-9.-]+(:\d+)?$")


def walk_keys(node):
    """Yield every object key found anywhere in a parsed JSON document."""
    if isinstance(node, dict):
        for key, value in node.items():
            yield key
            yield from walk_keys(value)
    elif isinstance(node, list):
        for item in node:
            yield from walk_keys(item)


def check_uri(label, what, uri, allow_app_scheme):
    """Return the violations of one URI that must be an https URL, without wildcards."""
    if "*" in uri or uri == "+":
        return [f"{label}: {what} {uri!r} contains a wildcard"]
    if allow_app_scheme and uri == "cichlids://auth":
        return []
    if not HTTPS_URI.match(uri):
        expected = "https:// or exactly cichlids://auth" if allow_app_scheme else "https://"
        return [f"{label}: {what} {uri!r} must be {expected}"]
    return []


def check_deployed_realm(text, label):
    """Return the rule violations of one deployed realm file given as JSON text."""
    try:
        realm = json.loads(text)
    except ValueError as err:
        return [f"{label}: not valid JSON ({err})"]
    failures = []

    if realm.get("sslRequired") != "all":
        failures.append(f"{label}: sslRequired must be \"all\" but is {realm.get('sslRequired')!r}")
    if "registrationAllowed" in realm:
        failures.append(f"{label}: registrationAllowed must not be set; registration is opened "
                        "by 'etl keycloak-import --open-registration' after the account import")
    if "users" in realm:
        failures.append(f"{label}: users must not be present; accounts are created outside Git")
    keys = set(walk_keys(realm))
    for key in ("credentials", "federatedIdentities"):
        if key in keys:
            failures.append(f"{label}: {key} must not appear anywhere")
    if realm.get("verifyEmail") is not True:
        failures.append(f"{label}: verifyEmail must be true")
    if realm.get("bruteForceProtected") is not True:
        failures.append(f"{label}: bruteForceProtected must be true")
    if realm.get("duplicateEmailsAllowed") is True:
        failures.append(f"{label}: duplicateEmailsAllowed must not be true")

    if realm.get("offlineSessionMaxLifespanEnabled") is not False:
        failures.append(f"{label}: offlineSessionMaxLifespanEnabled must be false")
    idle = realm.get("offlineSessionIdleTimeout")
    if not isinstance(idle, int) or isinstance(idle, bool) or idle < MIN_OFFLINE_IDLE_SECONDS:
        failures.append(f"{label}: offlineSessionIdleTimeout must be at least {MIN_OFFLINE_IDLE_SECONDS} seconds "
                        f"but is {idle!r}")

    frontend_url = (realm.get("attributes") or {}).get("frontendUrl")
    if frontend_url:
        failures += check_uri(label, "realm attribute frontendUrl", frontend_url, allow_app_scheme=False)

    for client in realm.get("clients") or []:
        name = client.get("clientId", "?")
        if client.get("directAccessGrantsEnabled") is not False:
            failures.append(f"{label}: client {name} must set directAccessGrantsEnabled to false")
        if client.get("implicitFlowEnabled") is True:
            failures.append(f"{label}: client {name} must not enable the implicit flow")
        pkce = (client.get("attributes") or {}).get("pkce.code.challenge.method")
        if client.get("publicClient") is True and pkce != "S256":
            failures.append(f"{label}: public client {name} must require PKCE S256")
        for uri in client.get("redirectUris") or []:
            failures += check_uri(label, f"client {name} redirect URI", uri, allow_app_scheme=True)
        logout_uris = (client.get("attributes") or {}).get("post.logout.redirect.uris")
        for uri in (logout_uris.split("##") if isinstance(logout_uris, str) else []):
            failures += check_uri(label, f"client {name} post logout redirect URI", uri, allow_app_scheme=True)
        for field in ("rootUrl", "baseUrl", "adminUrl"):
            value = client.get(field)
            if value:
                failures += check_uri(label, f"client {name} {field}", value, allow_app_scheme=False)
        for origin in client.get("webOrigins") or []:
            if not HTTPS_ORIGIN.match(origin):
                failures.append(f"{label}: client {name} web origin {origin!r} must be an exact https://host value")
        if "secret" in client:
            failures.append(f"{label}: client {name} must not carry a secret; the operator sets client "
                            "secrets in Keycloak from the secret store")
        if name in (APP_CLIENT, BFF_CLIENT):
            scopes = (client.get("optionalClientScopes") or []) + (client.get("defaultClientScopes") or [])
            if OFFLINE_SCOPE not in scopes:
                failures.append(f"{label}: client {name} must offer the {OFFLINE_SCOPE} client scope")
        if name == BFF_CLIENT:
            failures += check_bff_client(label, client)

    if not any(client.get("clientId") == BFF_CLIENT for client in realm.get("clients") or []):
        failures.append(f"{label}: the confidential client {BFF_CLIENT} of the web backend is missing")

    for pattern, description in FORBIDDEN_TEXT:
        if pattern.search(text):
            failures.append(f"{label}: contains {description}, a local development setting")
    return failures


def check_bff_client(label, client):
    """Return the violations of the client through which the API signs in web members."""
    failures = []
    attributes = client.get("attributes") or {}
    if client.get("publicClient") is not False:
        failures.append(f"{label}: client {BFF_CLIENT} must be confidential (publicClient false)")
    if client.get("bearerOnly") is True:
        failures.append(f"{label}: client {BFF_CLIENT} must not be bearer-only")
    if client.get("clientAuthenticatorType") != "client-secret":
        failures.append(f"{label}: client {BFF_CLIENT} must authenticate with a client secret")
    if attributes.get("pkce.code.challenge.method") != "S256":
        failures.append(f"{label}: client {BFF_CLIENT} must require PKCE S256")
    if client.get("standardFlowEnabled") is not True:
        failures.append(f"{label}: client {BFF_CLIENT} must enable the standard flow")
    if client.get("serviceAccountsEnabled") is not False:
        failures.append(f"{label}: client {BFF_CLIENT} must set serviceAccountsEnabled to false")
    if attributes.get("oauth2.device.authorization.grant.enabled") == "true":
        failures.append(f"{label}: client {BFF_CLIENT} must not enable the device grant")
    return failures


def comparable(text):
    """The realm without the values that may differ between the deployed environments."""
    realm = json.loads(text)
    realm.pop("displayName", None)
    for client in realm.get("clients") or []:
        if client.get("clientId") in (APP_CLIENT, BFF_CLIENT):
            client.pop("redirectUris", None)
            client.pop("webOrigins", None)
        if client.get("clientId") == BFF_CLIENT:
            (client.get("attributes") or {}).pop("post.logout.redirect.uris", None)
    return realm


def check_parity(staging_text, prod_text):
    try:
        if comparable(staging_text) != comparable(prod_text):
            return ["staging and prod differ in more than displayName, the redirectUris/webOrigins "
                    f"of {APP_CLIENT} and {BFF_CLIENT} and the post logout redirect URIs of {BFF_CLIENT}"]
    except ValueError:
        return []
    return []


def check_dev_realm(text):
    try:
        display_name = json.loads(text).get("displayName", "")
    except ValueError as err:
        return [f"dev realm: not valid JSON ({err})"]
    if LOCAL_ONLY_MARKER not in display_name.lower():
        return [f"dev realm: displayName must contain '{LOCAL_ONLY_MARKER}'"]
    return []


def read(path):
    with open(path, encoding="utf-8") as fh:
        return fh.read()


def main(argv):
    if "--self-test" in argv:
        return self_test()
    failures = []
    texts = {}
    for env, path in DEPLOYED_REALMS.items():
        rel = os.path.relpath(path, ROOT)
        if not os.path.isfile(path):
            failures.append(f"{rel}: file is missing")
            continue
        texts[env] = read(path)
        failures += check_deployed_realm(texts[env], rel)
    if len(texts) == len(DEPLOYED_REALMS):
        failures += check_parity(texts["staging"], texts["prod"])
    failures += check_dev_realm(read(DEV_REALM))
    if failures:
        print("keycloak realm check failed (ADR-0025):", file=sys.stderr)
        for failure in failures:
            print(f"  - {failure}", file=sys.stderr)
        return 1
    print("keycloak realm OK (deployed realms carry no local development setting)")
    return 0


def good_realm():
    return {
        "realm": "cichlids",
        "displayName": "cichlids staging",
        "enabled": True,
        "sslRequired": "all",
        "verifyEmail": True,
        "bruteForceProtected": True,
        "duplicateEmailsAllowed": False,
        "offlineSessionIdleTimeout": MIN_OFFLINE_IDLE_SECONDS,
        "offlineSessionMaxLifespanEnabled": False,
        "identityProviders": [
            {"alias": "google", "enabled": False, "config": {"clientSecret": "not-configured"}},
        ],
        "clients": [
            {"clientId": "cichlids-api", "bearerOnly": True, "publicClient": False,
             "directAccessGrantsEnabled": False},
            {"clientId": APP_CLIENT, "publicClient": True, "directAccessGrantsEnabled": False,
             "implicitFlowEnabled": False,
             "redirectUris": ["cichlids://auth", "https://staging.cichlids.com/auth"],
             "webOrigins": ["https://staging.cichlids.com"],
             "attributes": {"pkce.code.challenge.method": "S256"},
             "optionalClientScopes": [OFFLINE_SCOPE]},
            {"clientId": BFF_CLIENT, "publicClient": False, "bearerOnly": False,
             "clientAuthenticatorType": "client-secret", "standardFlowEnabled": True,
             "directAccessGrantsEnabled": False, "implicitFlowEnabled": False,
             "serviceAccountsEnabled": False,
             "redirectUris": ["https://staging.cichlids.com/api/auth/callback"],
             "webOrigins": [],
             "attributes": {"pkce.code.challenge.method": "S256",
                            "post.logout.redirect.uris": "https://staging.cichlids.com/me"},
             "optionalClientScopes": [OFFLINE_SCOPE]},
        ],
    }


def bff_client(realm):
    return next(c for c in realm["clients"] if c["clientId"] == BFF_CLIENT)


def app_client(realm):
    return next(c for c in realm["clients"] if c["clientId"] == APP_CLIENT)


def self_test():
    def dumps(realm):
        return json.dumps(realm, indent=2)

    def mutate(fn):
        realm = copy.deepcopy(good_realm())
        fn(realm)
        return realm

    def set_key(key, value):
        return lambda r: r.__setitem__(key, value)

    def set_app(key, value):
        return lambda r: app_client(r).__setitem__(key, value)

    with_user = lambda r: r.__setitem__("users", [{
        "username": "someone", "credentials": [{"type": "password", "value": "x"}]}])
    nested_credentials = lambda r: r["identityProviders"][0].__setitem__(
        "credentials", [{"type": "password"}])
    nested_federated = lambda r: r["identityProviders"][0].__setitem__("federatedIdentities", [])
    drop_grant_flag = lambda r: app_client(r).pop("directAccessGrantsEnabled")
    drop_pkce = lambda r: app_client(r).__setitem__("attributes", {})

    def set_bff(key, value):
        return lambda r: bff_client(r).__setitem__(key, value)

    set_bff_attribute = lambda key, value: (lambda r: bff_client(r).__setitem__(
        "attributes", {**bff_client(r)["attributes"], key: value}))
    drop_bff = lambda r: r.__setitem__("clients", [c for c in r["clients"] if c["clientId"] != BFF_CLIENT])

    set_attribute = lambda key, value: (lambda r: app_client(r).__setitem__(
        "attributes", {**app_client(r)["attributes"], key: value}))
    set_realm_attribute = lambda key, value: (lambda r: r.__setitem__("attributes", {key: value}))

    cases = [
        ("good realm", good_realm(), 0),
        ("good realm with https URLs and a logout redirect",
         mutate(lambda r: (set_app("rootUrl", "https://staging.cichlids.com")(r),
                           set_attribute("post.logout.redirect.uris", "cichlids://auth##https://staging.cichlids.com/auth")(r),
                           set_realm_attribute("frontendUrl", "https://auth.staging.cichlids.com")(r))), 0),
        ("null redirect and origin lists", mutate(lambda r: (set_app("redirectUris", None)(r),
                                                              set_app("webOrigins", None)(r))), 0),
        ("null client list, so no web backend client", mutate(set_key("clients", None)), 1),
        ("http root URL", mutate(set_app("rootUrl", "http://staging.cichlids.com")), 1),
        ("localhost base URL", mutate(set_app("baseUrl", "http://localhost:8081")), 1),
        ("wildcard admin URL", mutate(set_app("adminUrl", "https://*.cichlids.com")), 1),
        ("app scheme as root URL", mutate(set_app("rootUrl", "cichlids://auth")), 1),
        ("plus post logout redirect", mutate(set_attribute("post.logout.redirect.uris", "+")), 1),
        ("wildcard post logout redirect", mutate(set_attribute("post.logout.redirect.uris", "https://cichlids.com/*")), 1),
        ("http post logout redirect", mutate(set_attribute("post.logout.redirect.uris", "cichlids://auth##http://cichlids.com/")), 1),
        ("http frontend URL", mutate(set_realm_attribute("frontendUrl", "http://auth.cichlids.com")), 1),
        ("sslRequired none", mutate(set_key("sslRequired", "none")), 1),
        ("sslRequired external", mutate(set_key("sslRequired", "external")), 1),
        ("registrationAllowed true", mutate(set_key("registrationAllowed", True)), 1),
        ("registrationAllowed false", mutate(set_key("registrationAllowed", False)), 1),
        ("user with a password", mutate(with_user), 1),
        ("credentials below an identity provider", mutate(nested_credentials), 1),
        ("federated identities", mutate(nested_federated), 1),
        ("verifyEmail off", mutate(set_key("verifyEmail", False)), 1),
        ("brute force protection off", mutate(set_key("bruteForceProtected", False)), 1),
        ("duplicate e-mails allowed", mutate(set_key("duplicateEmailsAllowed", True)), 1),
        ("offline session lifespan enabled", mutate(set_key("offlineSessionMaxLifespanEnabled", True)), 1),
        ("offline session lifespan flag missing", mutate(lambda r: r.pop("offlineSessionMaxLifespanEnabled")), 1),
        ("offline session idle of 30 days", mutate(set_key("offlineSessionIdleTimeout", 2592000)), 1),
        ("offline session idle missing", mutate(lambda r: r.pop("offlineSessionIdleTimeout")), 1),
        ("offline session idle as text", mutate(set_key("offlineSessionIdleTimeout", "315360000")), 1),
        ("offline scope as a default scope", mutate(lambda r: (set_app("optionalClientScopes", [])(r),
                                                              set_app("defaultClientScopes", [OFFLINE_SCOPE])(r))), 0),
        ("offline scope missing", mutate(set_app("optionalClientScopes", ["profile"])), 1),
        ("password grant", mutate(set_app("directAccessGrantsEnabled", True)), 1),
        ("password grant flag missing", mutate(drop_grant_flag), 1),
        ("implicit flow", mutate(set_app("implicitFlowEnabled", True)), 1),
        ("PKCE missing", mutate(drop_pkce), 1),
        ("wildcard redirect", mutate(set_app("redirectUris", ["https://cichlids.com/*"])), 1),
        ("custom scheme wildcard", mutate(set_app("redirectUris", ["cichlids://*"])), 1),
        ("localhost redirect", mutate(set_app("redirectUris", ["http://localhost:8081/auth"])), 1),
        ("http redirect", mutate(set_app("redirectUris", ["http://cichlids.com/auth"])), 1),
        ("plus web origin", mutate(set_app("webOrigins", ["+"])), 1),
        ("wildcard web origin", mutate(set_app("webOrigins", ["*"])), 1),
        ("web origin with a path", mutate(set_app("webOrigins", ["https://cichlids.com/app"])), 1),
        ("test domain", mutate(set_key("description", "mail to ops@cichlids.test")), 1),
        ("loopback address", mutate(set_key("description", "http://127.0.0.1:8180")), 1),
        ("development password", mutate(set_key("description", "dev-password")), 1),
        ("expo redirect", mutate(set_app("redirectUris", ["exp://192.168.0.2:8081"])), 1),
        ("local-only marker", mutate(set_key("displayName", "cichlids (local development only)")), 1),
        ("not JSON", None, 1),
        ("web BFF client missing", mutate(drop_bff), 1),
        ("web BFF client public", mutate(set_bff("publicClient", True)), 1),
        ("web BFF client without the public flag", mutate(lambda r: bff_client(r).pop("publicClient")), 1),
        ("web BFF client bearer only", mutate(set_bff("bearerOnly", True)), 1),
        ("web BFF client signed JWT authentication", mutate(set_bff("clientAuthenticatorType", "client-jwt")), 1),
        ("web BFF client with a committed secret", mutate(set_bff("secret", "s3cr3t-value")), 1),
        ("app client with a committed secret", mutate(set_app("secret", "s3cr3t-value")), 1),
        ("web BFF client without PKCE", mutate(set_bff("attributes", {})), 1),
        ("web BFF client with plain PKCE", mutate(set_bff_attribute("pkce.code.challenge.method", "plain")), 1),
        ("web BFF client without the standard flow", mutate(set_bff("standardFlowEnabled", False)), 1),
        ("web BFF client with a service account", mutate(set_bff("serviceAccountsEnabled", True)), 1),
        ("web BFF client with the device grant",
         mutate(set_bff_attribute("oauth2.device.authorization.grant.enabled", "true")), 1),
        ("web BFF client without the offline scope", mutate(set_bff("optionalClientScopes", [])), 1),
        ("web BFF client with a localhost logout redirect",
         mutate(set_bff_attribute("post.logout.redirect.uris", "http://localhost:8081/me")), 1),
        ("development secret", mutate(set_key("description", "cichlids-web-bff-dev-secret")), 1),
    ]
    failed = 0
    for name, realm, expected in cases:
        text = "{" if realm is None else dumps(realm)
        got = 1 if check_deployed_realm(text, name) else 0
        if got != expected:
            print(f"SELF-TEST FAIL: {name}: expected {'failure' if expected else 'pass'}", file=sys.stderr)
            failed += 1

    staging = good_realm()
    prod = copy.deepcopy(staging)
    prod["displayName"] = "cichlids"
    app_client(prod)["redirectUris"] = ["cichlids://auth"]
    app_client(prod)["webOrigins"] = []
    drifted = copy.deepcopy(prod)
    drifted["verifyEmail"] = False
    bff_client(prod)["redirectUris"] = []
    bff_client(prod)["attributes"].pop("post.logout.redirect.uris")
    drifted_client = copy.deepcopy(prod)
    app_client(drifted_client)["standardFlowEnabled"] = False
    drifted_bff = copy.deepcopy(prod)
    bff_client(drifted_bff)["serviceAccountsEnabled"] = True
    parity_cases = [
        ("parity of staging and prod", prod, False),
        ("parity with a drifted realm setting", drifted, True),
        ("parity with a drifted client setting", drifted_client, True),
        ("parity with a drifted web BFF client setting", drifted_bff, True),
    ]
    for name, other, expect_failure in parity_cases:
        got = bool(check_parity(dumps(staging), dumps(other)))
        if got != expect_failure:
            print(f"SELF-TEST FAIL: {name}", file=sys.stderr)
            failed += 1

    dev_cases = [
        ("dev realm marked local", {"displayName": "cichlids (local development only)"}, False),
        ("dev realm without marker", {"displayName": "cichlids"}, True),
        ("dev realm without display name", {}, True),
    ]
    for name, realm, expect_failure in dev_cases:
        if bool(check_dev_realm(dumps(realm))) != expect_failure:
            print(f"SELF-TEST FAIL: {name}", file=sys.stderr)
            failed += 1

    if os.path.isfile(DEV_REALM):
        if not check_deployed_realm(read(DEV_REALM), "dev realm as staging"):
            print("SELF-TEST FAIL: the dev realm file passed as a deployed realm", file=sys.stderr)
            failed += 1

    if failed:
        return 1
    print(f"self-test OK ({len(cases) + len(parity_cases) + len(dev_cases) + 1} cases)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
