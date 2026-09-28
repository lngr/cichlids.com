-- Legacy rows for KeycloakAccountImportTests, applied after legacy-schema.sql. fe_users_auth0
-- maps two email-less Facebook subjects: one to a member with an email, one to a member without.
INSERT INTO fe_users (uid, username, email) VALUES
    (9101, 'alice', 'Alice@Example.test'),
    (9104, 'dave', ' Dave@Example.test '),
    (9105, 'erin', '');

INSERT INTO fe_users_auth0 (sub, user_id) VALUES
    ('auth0|pw-alice', 9101),
    ('facebook|fb-dave', 9104),
    ('facebook|fb-erin', 9105);
