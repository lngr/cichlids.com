-- Species catalog: lookups shared by several rows below.
INSERT INTO user_cichlids_genus_names (uid, title, deleted) VALUES
    (100, 'Testonoma', 0),
    (101, 'Otherus', 0);

INSERT INTO user_cichlids_species_names (uid, title, deleted) VALUES
    (200, 'testus', 0),
    (201, 'brokenus', 0),
    (202, 'threeus', 0),
    (203, 'fourus', 0);

INSERT INTO user_cichlids_category (uid, title, deleted) VALUES
    (10, 'Lake Test', 0);

-- uid 1: title blank (forces the "<genus> <name>" fallback), two links split from parallel
-- lines (the second with no matching label line), and two common names that dedupe to one.
INSERT INTO user_cichlids_species
    (uid, title, genus, species, category, temp, ph, gh, kh, max_size, description, origin,
     habitat, morphs_text, breeding, aggro, inner_aggro, diet, links, link_texts, deleted)
VALUES
    (1, '', 100, 200, 10, '24-26', '7.5-8.5', '10-15', '8-12', '10cm', 'desc', 'origin',
     'habitat', 'morph', 1, 2, 1, 2, 'http://a.example\nhttp://b.example', 'LinkA\n', 0),
    -- uid 2: every enum column has an out-of-range legacy code, and category points at a uid
    -- that does not exist, so the lookup miss path is exercised too.
    (2, 'Broken Enum Species', 100, 201, 999, NULL, NULL, '', '', NULL, NULL, NULL, NULL, NULL,
     9, 9, 9, 9, '', '', 0),
    -- uid 3 and 4 resolve to the same realurl alias below (a collision to detect and suffix).
    (3, 'Species Three', 101, 202, 10, NULL, NULL, '', '', NULL, NULL, NULL, NULL, NULL,
     0, 0, 0, 0, '', '', 0),
    (4, 'Species Four', 101, 203, 10, NULL, NULL, '', '', NULL, NULL, NULL, NULL, NULL,
     0, 0, 0, 0, '', '', 0),
    -- uid 5 is soft-deleted and must never reach the target.
    (5, 'Deleted Species', 100, 200, 10, NULL, NULL, '', '', NULL, NULL, NULL, NULL, NULL,
     0, 0, 0, 0, '', '', 1);

INSERT INTO user_cichlids_common_name (uid, title, deleted) VALUES
    (500, 'Test Cichlid', 0),
    (501, 'Test Cichlid', 0);

INSERT INTO user_cichlids_species_common_mm (uid_local, uid_foreign, tablenames, sorting) VALUES
    (1, 500, '', 1),
    (1, 501, '', 2);

INSERT INTO tx_realurl_uniqalias (uid, tablename, value_alias, value_id) VALUES
    (1000, 'user_cichlids_species', 'testonoma_testus', 1),
    (1001, 'user_cichlids_species', 'collision_slug', 3),
    (1002, 'user_cichlids_species', 'collision_slug', 4);

-- Members: alice has all three identity providers, bob has zero legacy timestamps and no
-- identities, charlie has no content anywhere and must stay out of the target, the dup pair
-- shares both a username and a legacy-openid value to exercise both collision paths at once,
-- and the last two have content but are excluded by deleted/disable.
INSERT INTO fe_users
    (uid, username, name, first_name, last_name, city, static_info_country,
     user_cichlids_auth0_image, crdate, tstamp, lastlogin, tx_dixeasylogin_openid, email,
     deleted, disable)
VALUES
    (10, 'alice', 'Alice A', 'Alice', 'A', 'Berlin', 'DEU', 'http://img/alice.png',
     1000000000, 1000000100, 1000000200, 'openid-alice', 'alice@example.com', 0, 0),
    (11, 'bob', '', 'Bob', 'B', '', '', NULL, 0, 0, 0, NULL, NULL, 0, 0),
    (12, 'charlie', 'Charlie C', 'Charlie', 'C', '', '', NULL, 1000000000, 0, 0, NULL, NULL, 0, 0),
    (13, 'dupuser', 'Dup One', 'Dup', 'One', '', '', NULL, 1000000000, 0, 0, 'dup-openid', NULL, 0, 0),
    (14, 'dupuser', 'Dup Two', 'Dup', 'Two', '', '', NULL, 1000000000, 0, 0, 'dup-openid', NULL, 0, 0),
    (15, 'deleteduser', 'Deleted', 'Deleted', 'User', '', '', NULL, 1000000000, 0, 0, NULL, NULL, 1, 0),
    (16, 'disableduser', 'Disabled', 'Disabled', 'User', '', '', NULL, 1000000000, 0, 0, NULL, NULL, 0, 1);

INSERT INTO fe_users_auth0 (sub, user_id) VALUES
    ('auth0|alice', 10),
    ('auth0|dup13', 13),
    ('auth0|dup14', 14);

INSERT INTO user_cichlids_pictures (uid, fe_user) VALUES
    (1, 10),
    (2, 15);
INSERT INTO user_cichlids_tanks (uid, fe_user) VALUES
    (1, 11),
    (2, 16);
INSERT INTO user_cichlids_comments (uid, fe_user) VALUES
    (1, 13),
    (2, 14);
