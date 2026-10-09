-- Legacy rows for KeycloakAccountImportTests, applied after legacy-schema.sql. fe_users_auth0
-- maps three email-less Facebook subjects: one to a member with an email, two to members without.
-- The usernames cover the login username rule: alice and erin keep theirs, bob's repeats alice's
-- in other letter case while alice has the migrated profile, carol's is her address, dave's is
-- held by a self-registered user, ivy's has a space Keycloak rejects, and heidi's is held by a
-- self-registered user while her account has no email.
INSERT INTO fe_users (uid, username, email, lastlogin) VALUES
    (9101, 'alice', 'Alice@Example.test', 100),
    (9102, 'Alice', 'bob@example.test', 900),
    (9103, 'carol@example.test', 'carol@example.test', 0),
    (9104, 'dave', ' Dave@Example.test ', 0),
    (9105, 'erin', '', 0),
    (9106, 'ivy smith', 'ivy@example.test', 0),
    (9108, 'heidi', '', 0);

INSERT INTO fe_users_auth0 (sub, user_id) VALUES
    ('auth0|pw-alice', 9101),
    ('facebook|fb-dave', 9104),
    ('facebook|fb-erin', 9105),
    ('facebook|fb-heidi', 9108);
