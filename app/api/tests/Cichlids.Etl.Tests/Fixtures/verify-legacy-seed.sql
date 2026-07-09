-- Self-contained legacy dataset for VerifyStepTests: every row here is reachable, end to end,
-- through a real run of every migration step (species, profiles, tanks, pictures, comments,
-- galleries, forum), so the whole pipeline followed by verify produces a target with zero
-- mismatches. Unlike the shared legacy-schema.sql-based fixture the other step tests use, nothing
-- here relies on a test class seeding extra profile/post/tank rows directly into Postgres: doing
-- that would give the target rows with no legacy counterpart, which verify would (correctly) flag.
-- All legacy ids live in the 9000s, a range of their own not used by any other fixture.

INSERT INTO user_cichlids_genus_names (uid, title, deleted) VALUES
    (9001, 'Genusverifica', 0);

INSERT INTO user_cichlids_species_names (uid, title, deleted) VALUES
    (9001, 'verifica', 0),
    (9002, 'verificados', 0);

INSERT INTO user_cichlids_category (uid, title, deleted) VALUES
    (9001, 'Verify Lake', 0);

INSERT INTO user_cichlids_species
    (uid, title, genus, species, category, temp, ph, gh, kh, max_size, description, origin,
     habitat, morphs_text, breeding, aggro, inner_aggro, diet, links, link_texts, deleted)
VALUES
    (9001, NULL, 9001, 9001, 9001, '24-26', '7-8', '10', '8', '8cm', 'desc', 'origin',
     'habitat', NULL, 1, 1, 1, 1, '', '', 0),
    (9002, 'Species Verify Two', 9001, 9002, 9001, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
     NULL, 0, 0, 0, 0, '', '', 0);

INSERT INTO tx_realurl_uniqalias (uid, tablename, value_alias, value_id) VALUES
    (99001, 'user_cichlids_species', 'genusverifica-verifica', 9001);

-- Members: alice (9010) has all three identity providers and owns pictures, bob (9011) owns a
-- tank and has no identities, noc (9012) has no content anywhere and must stay out of the target,
-- del (9013) and dis (9014) are excluded by the source query itself (deleted/disable).
INSERT INTO fe_users
    (uid, username, name, first_name, last_name, city, static_info_country,
     user_cichlids_auth0_image, crdate, tstamp, lastlogin, tx_dixeasylogin_openid, email,
     deleted, disable)
VALUES
    (9010, 'alice-verify', 'Alice Verify', 'Alice', 'Verify', '', '', NULL,
     1700000000, 0, 0, 'openid-alice-verify', 'alice@verify.example', 0, 0),
    (9011, 'bob-verify', 'Bob Verify', 'Bob', 'Verify', '', '', NULL,
     1700000000, 0, 0, NULL, NULL, 0, 0),
    (9012, 'noc-verify', 'No Content Verify', 'NoContent', 'Verify', '', '', NULL,
     1700000000, 0, 0, NULL, NULL, 0, 0),
    (9013, 'del-verify', 'Deleted Verify', 'Deleted', 'Verify', '', '', NULL,
     1700000000, 0, 0, NULL, NULL, 1, 0),
    (9014, 'dis-verify', 'Disabled Verify', 'Disabled', 'Verify', '', '', NULL,
     1700000000, 0, 0, NULL, NULL, 0, 1);

INSERT INTO fe_users_auth0 (sub, user_id) VALUES
    ('auth0|alice-verify', 9010);

INSERT INTO user_cichlids_tanks
    (uid, fe_user, deleted, hidden, category, title, width, height, depth, unit,
     crdate, tstamp, fish, fish_count)
VALUES
    (9201, 9011, 0, 0, 1, 'Verify Tank', 10, 10, 10, NULL,
     1700000100, 1700000100, '9001', '5'),
    -- fe_user 0: the anonymous/community-archive owner, so the informative system-kind profile
    -- count has something real to report.
    (9202, 0, 0, 0, 1, 'Verify Anon Tank', 5, 5, 5, NULL,
     1700000150, 1700000150, '', '');

INSERT INTO user_cichlids_pictures
    (uid, pid, tstamp, crdate, deleted, hidden, title, fe_user, image, description,
     rating, rating_count, views, delete_tstamp)
VALUES
    (9101, 21, 1700000200, 1700000200, 0, 0, 'Verify Pic One', 9010, 'user_pics/9010/picone.jpg', NULL,
     4, 1, 0, 0),
    (9102, 21, 1700000300, 1700000300, 0, 0, 'Verify Pic Two', 9010, 'user_pics/9010/pictwo.jpg', NULL,
     0, 0, 0, 0),
    -- Soft-deleted: never migrates, so a comment targeting it below vaults as target_deleted.
    (9103, 21, 1700000350, 1700000350, 1, 0, 'Verify Pic Deleted', 9010, 'user_pics/9010/deleted.jpg', NULL,
     0, 0, 0, 0);

INSERT INTO tx_realurl_uniqalias (uid, tablename, value_alias, value_id) VALUES
    (99101, 'user_cichlids_pictures', 'verify-pic-one', 9101);

-- uid 9301: text plus a star rating, split into both a comment and a rating row.
-- uid 9302: rating-only (no text).
-- uid 9303: tank-side comment (no rating).
-- uid 9304: targets picture 9999, which does not exist in the legacy source at all -- vaulted
--   with reason target_missing.
-- uid 9305: targets picture 9103 (soft-deleted, never migrated) -- vaulted target_deleted.
-- uid 9306: an unrecognized type code -- vaulted unknown_type.
-- uid 9307: neither text nor rating -- content-empty, skipped entirely.
-- uid 9308: soft-deleted at the top level -- never reaches target resolution.
-- uid 9309: author legacy id 9999 has no fe_users row at all -- placeholder profile path.
INSERT INTO user_cichlids_comments
    (uid, type, item, rating, poster, note, fe_user, tstamp, crdate, deleted, hidden,
     delete_tstamp, delete_reason, delete_user, score)
VALUES
    (9301, 1, 9101, 4, NULL, 'Nice picture', 9011, 1700000500, 0, 0, 0, 0, NULL, NULL, 0),
    (9302, 1, 9101, 5, NULL, NULL, 9010, 1700000510, 0, 0, 0, 0, NULL, NULL, 0),
    (9303, 2, 9201, 0, NULL, 'Tank comment text', 9011, 1700000520, 0, 0, 0, 0, NULL, NULL, 0),
    (9304, 1, 9999, 0, NULL, 'ghost', 9010, 1700000530, 0, 0, 0, 0, NULL, NULL, 0),
    (9305, 1, 9103, 0, NULL, 'deleted target', 9010, 1700000540, 0, 0, 0, 0, NULL, NULL, 0),
    (9306, 9, 9101, 0, NULL, 'unknown type', 9010, 1700000550, 0, 0, 0, 0, NULL, NULL, 0),
    (9307, 1, 9101, 0, NULL, NULL, 9010, 1700000560, 0, 0, 0, 0, NULL, NULL, 0),
    (9308, 1, 9101, 0, NULL, 'ignored deleted', 9010, 1700000570, 0, 1, 0, 0, NULL, NULL, 0),
    (9309, 1, 9101, 0, NULL, 'from ghost user', 9999, 1700000580, 0, 0, 0, 0, NULL, NULL, 0);

INSERT INTO user_cichlids_comments_rated (comment_uid, fe_user, rated, tstamp) VALUES
    -- Earliest of the two 9301/9010 rows wins (+1), the later one is a counted duplicate.
    (9301, 9010, 1, '2024-01-01 10:00:00'),
    (9301, 9010, -1, '2024-01-02 10:00:00'),
    -- An invalid rated value from a second voter: dropped.
    (9301, 9011, 2, '2024-01-01 11:00:00'),
    -- A vote on a rating-only row: it never became a comment, so this never migrates either.
    (9302, 9010, 1, '2024-01-01 12:00:00');

INSERT INTO user_cichlids_gallery (uid, tstamp, deleted, hidden, title, fe_user) VALUES
    (9501, 1700000400, 0, 0, 'Verify Gallery', 9010);

INSERT INTO user_cichlids_gallery_pictures_mm (uid_gallery, uid_picture, sorting) VALUES
    (9501, 9101, 0),
    (9501, 9102, 1);

-- Forum: one thread (root plus a guest reply) in forum_id 1 (cichlids), with one attachment on
-- the root. The root's author matches alice-verify's e-mail identity.
INSERT INTO cichlids_phorum5.phorum_users (user_id, email, display_name) VALUES
    (9401, 'alice@verify.example', 'Alice Verify Forum');

INSERT INTO cichlids_phorum5.phorum_messages
    (message_id, forum_id, thread, parent_id, author, subject, body, user_id, datestamp, status)
VALUES
    (98001, 1, 98001, 0, '', 'Verify Root', 'Root body text.', 9401, 1700000600, 2),
    (98002, 1, 98001, 98001, 'VerifyGuest', '', 'Reply body text.', 0, 1700000700, 2);

INSERT INTO cichlids_phorum5.phorum_files (file_id, filename, file_data, message_id, link) VALUES
    -- Decodes to the ASCII string "VERIFY-ATTACHMENT-BYTES".
    (99001, 'verify-attach.txt', 'VkVSSUZZLUFUVEFDSE1FTlQtQllURVM=', 98001, 'message');
