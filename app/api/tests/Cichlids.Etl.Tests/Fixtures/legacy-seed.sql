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
-- uid 1 and 2 only exist to give bob and disableduser content for the profile eligibility check
-- above (that scan ignores deleted rows). They are soft-deleted here so the tank step itself,
-- which filters on deleted = 0, never reads them.
INSERT INTO user_cichlids_tanks (uid, fe_user, deleted) VALUES
    (1, 11, 1),
    (2, 16, 1);

-- Owner legacy ids 101/102 and species legacy id 101 below are a range of their own, disjoint
-- from the species (1-5) and profile (10-16) fixtures above: TankMigrationStepTests seeds its own
-- prerequisite profile/species rows directly (see that class for why) rather than running the
-- species/profile steps, and those rows must never collide with the ones this file's other tests
-- upsert under the shared legacy ids 1-5/10-16.
--
-- uid 10: a full published tank with a dirty fish/fish_count pairing -- a resolvable species, an
-- explicit "0" placeholder, a leading double comma (empty token) with a count behind it, and a
-- species uid that does not exist in the catalog, also with a count behind it.
INSERT INTO user_cichlids_tanks
    (uid, fe_user, deleted, hidden, category, title, width, height, depth, unit,
     crdate, tstamp, fish, fish_count)
VALUES
    (10, 101, 0, 0, 1, 'Reef Tank', 100, 40, 40, 'centimeters',
     1000000500, 1000000600, '101,0,,99', '5\n\n3\n2'),
    -- uid 11: a draft (hidden) tank with an unmapped category code, no crdate (falls back to
    -- tstamp), a zeroed dimension and no unit (empty/no-value keeps the inch default).
    (11, 102, 0, 1, 99, 'Draft Tank', 0, 50, 0, NULL,
     0, 1000000700, '', ''),
    -- uid 12: owned by a legacy user id with no fe_users row at all, exercising the placeholder
    -- profile path.
    (12, 999, 0, 0, 2, 'Orphaned Tank', 200, 0, 60, NULL,
     1000000800, 1000000900, '', ''),
    -- uid 13: unit is the literal word "inches", which the legacy edit form only ever wrote when
    -- an editor had switched the field to metric, so any non-empty unit value (however it reads)
    -- means centimeters.
    (13, 101, 0, 0, 3, 'Inches Tank', 80, 35, 35, 'inches',
     1000001000, 1000001100, '', ''),
    -- uid 14: fe_user 0, the anonymous/community-archive owner.
    (14, 0, 0, 0, 6, 'Anonymous Community Tank', 0, 0, 0, NULL,
     1000001200, 1000001300, '', '');

INSERT INTO user_cichlids_comments (uid, fe_user) VALUES
    (1, 13),
    (2, 14);

-- Picture step fixtures: owner legacy ids 300/301 and tank legacy id 300 are their own range,
-- disjoint from every id range above (species/profile 1-16, tank/profile 10-14/101/102/999).
-- PictureMigrationStepTests seeds the matching profile/tank rows directly in Postgres, the same
-- way TankMigrationStepTests does, instead of running the profile/tank steps.
--
-- uid 300 exists only to give the profile-image/avatar backfill pass a legacy fe_users row to
-- read: its own pictures are irrelevant, only the two image reference columns matter.
INSERT INTO fe_users
    (uid, username, name, first_name, last_name, city, static_info_country,
     user_cichlids_auth0_image, crdate, tstamp, lastlogin, tx_dixeasylogin_openid, email,
     deleted, disable, user_cichlids_profile_image, user_cichlids_avatar_image)
VALUES
    (300, 'picturefixtureowner300', 'Picture Fixture Owner 300', 'Picture', 'Owner300', '', '',
     NULL, 2000000000, 2000000000, 0, NULL, NULL, 0, 0, 2008, 2009);

-- uid 2001: an active, published pid-21 (cichlids) picture with one legacy realurl alias.
-- uid 2002: hidden (draft) pid-29 (tanks) picture with no alias (falls through to a generated slug).
-- uid 2003: owned by legacy user 950, who has no migrated profile at all (placeholder path),
--   so the owner-kind rule makes its post archived regardless of the hidden flag.
-- uid 2004: fe_user 0 (anonymous/community archive owner), also archived regardless of hidden.
-- uid 2005: soft-deleted, must produce neither a media_item nor a post.
-- uid 2006: a video-extension pid-62 (tank technic) picture: media_item only, kind=video.
-- uid 2007: an out-of-catalogue pid (9999): media_item only, counted by pid.
-- uid 2008/2009: pid-138/139 sources for the profile-image/avatar backfill on fe_users uid 300.
-- uid 2010/2011: two pid-21/29 pictures whose realurl aliases collide on the same value_alias
--   text: the lower uniqalias uid (2010's) wins, 2011 ends up with none and gets a generated slug.
-- uid 2012: one alias that looks like an auto-generated hashid plus one that reads as a normal
--   slug: the normal one must win canonical over the hashid-looking one.
-- uid 2013/2014: identical image path, so both normalize to the same storage key: 2014 (the
--   higher legacy id) must be suffixed to stay unique.
INSERT INTO user_cichlids_pictures
    (uid, pid, tstamp, crdate, deleted, hidden, title, fe_user, image, description,
     rating, rating_count, views, delete_tstamp)
VALUES
    (2001, 21, 2000000100, 2000000050, 0, 0, 'Pic One', 301, 'user_pics/301/pic1.jpg', 'Desc one',
     4.5, 3, 10, 0),
    (2002, 29, 2000000200, 0, 0, 1, 'Pic Two', 301, 'user_pics/301/pic2.jpg', NULL,
     0, 0, 0, 0),
    (2003, 21, 2000000300, 2000000250, 0, 0, 'Pic Three', 950, 'user_pics/950/pic3.jpg', NULL,
     0, 0, 0, 0),
    (2004, 21, 2000000400, 0, 0, 0, 'Pic Four', 0, 'user_pics/anon/pic4.jpg', NULL,
     0, 0, 0, 0),
    (2005, 21, 2000000500, 0, 1, 0, 'Pic Deleted', 301, 'user_pics/301/deleted.jpg', NULL,
     0, 0, 0, 0),
    (2006, 62, 2000000600, 2000000550, 0, 0, NULL, 301, 'user_pics/301/clip.mp4', NULL,
     0, 0, 0, 0),
    (2007, 9999, 2000000700, 0, 0, 0, NULL, 301, 'user_pics/301/pic7.jpg', NULL,
     0, 0, 0, 0),
    (2008, 138, 2000000800, 0, 0, 0, NULL, 301, 'user_pics/301/profileimg.jpg', NULL,
     0, 0, 0, 0),
    (2009, 139, 2000000900, 0, 0, 0, NULL, 301, 'user_pics/301/avatar.jpg', NULL,
     0, 0, 0, 0),
    (2010, 21, 2000001000, 2000000950, 0, 0, 'Pic Ten', 301, 'user_pics/301/pic10.jpg', NULL,
     0, 0, 0, 0),
    (2011, 29, 2000001100, 0, 0, 0, 'Pic Eleven', 301, 'user_pics/301/pic11.jpg', NULL,
     0, 0, 0, 0),
    (2012, 21, 2000001200, 0, 0, 0, 'Pic Twelve', 301, 'user_pics/301/pic12.jpg', NULL,
     0, 0, 0, 0),
    (2013, 21, 2000001300, 0, 0, 0, 'Pic Thirteen', 301, 'user_pics/dup/same.jpg', NULL,
     0, 0, 0, 0),
    (2014, 21, 2000001400, 0, 0, 0, 'Pic Fourteen', 301, 'user_pics/dup/same.jpg', NULL,
     0, 0, 0, 0),
    -- uid 2015: a legacy path with a percent-encoded space and an umlaut, exercising
    -- StorageKeyNormalizer end to end through the real step (it also has its own isolated unit
    -- tests covering the normalization rules themselves, this proves the step actually wires
    -- that normalization into storage_key).
    (2015, 21, 2000001500, 0, 0, 0, 'Pic Fifteen', 301, 'user_pics/301/Gro%C3%9Fe%20Gruppe.jpg', NULL,
     0, 0, 0, 0),
    -- uid 2016: a legacy path with no directory component at all, routed under the unresolved
    -- storage bucket.
    (2016, 21, 2000001600, 0, 0, 0, 'Pic Sixteen', 301, '01_no_directory.jpg', NULL,
     0, 0, 0, 0),
    -- uid 2017: a plain pid-21 picture with a nonzero legacy rating, seeded once and never
    -- touched again by this table's other rows, so a test can freely corrupt its post's
    -- rating_average/rating_count after the first run and prove a later run leaves them alone.
    (2017, 21, 2000001700, 0, 0, 0, 'Pic Seventeen', 301, 'user_pics/301/pic17.jpg', NULL,
     2.5, 4, 0, 0);

INSERT INTO tx_realurl_uniqalias (uid, tablename, value_alias, value_id) VALUES
    (9001, 'user_cichlids_pictures', 'pic-one', 2001),
    (9010, 'user_cichlids_pictures', 'shared-slug', 2010),
    (9011, 'user_cichlids_pictures', 'shared-slug', 2011),
    (9012, 'user_cichlids_pictures', 'ab12z', 2012),
    (9013, 'user_cichlids_pictures', 'nice-slug-name', 2012);

-- uid 300: references pictures 2001 (main image), 2006 and a nonexistent uid 2099 (tank_images),
-- and 2003 (deco_images), to exercise tank.main_media_id plus all three tank_media sections
-- including a dangling reference that must be counted, not crash the run. This row is also read
-- by TankMigrationStep (it scans the whole table unconditionally), so its owner is legacy user
-- 390, a dedicated id used nowhere else in this file: reusing 301 here would race
-- TankMigrationStep's own owner resolution against the profile this file seeds directly for 301,
-- and which of the two ran first would decide whether that profile ends up Member or a
-- TankMigrationStep-created Archived placeholder.
INSERT INTO user_cichlids_tanks
    (uid, fe_user, deleted, hidden, category, title, width, height, depth, unit,
     crdate, tstamp, fish, fish_count, image, tank_images, deco_images, tec_images)
VALUES
    (300, 390, 0, 0, 1, 'Picture Fixture Tank', 100, 40, 40, 'centimeters',
     2000000000, 2000000000, '', '', '2001', ',2006,2099', ',2003', '');
