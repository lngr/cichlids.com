-- Minimal legacy schema: only the columns the ETL steps read, typed loosely so the fixture
-- script can seed edge cases without fighting NOT NULL constraints the production schema has for
-- unrelated columns. The DROP TABLE statements make the script safe to re-run against the same
-- container if seeding is retried after a transient connection failure during container startup.

DROP TABLE IF EXISTS user_cichlids_genus_names;
DROP TABLE IF EXISTS user_cichlids_species_names;
DROP TABLE IF EXISTS user_cichlids_category;
DROP TABLE IF EXISTS user_cichlids_species;
DROP TABLE IF EXISTS user_cichlids_common_name;
DROP TABLE IF EXISTS user_cichlids_species_common_mm;
DROP TABLE IF EXISTS tx_realurl_uniqalias;
DROP TABLE IF EXISTS fe_users;
DROP TABLE IF EXISTS fe_users_auth0;
DROP TABLE IF EXISTS user_cichlids_pictures;
DROP TABLE IF EXISTS user_cichlids_tanks;
DROP TABLE IF EXISTS user_cichlids_comments;
DROP TABLE IF EXISTS user_cichlids_comments_rated;
DROP TABLE IF EXISTS user_cichlids_gallery;
DROP TABLE IF EXISTS user_cichlids_gallery_pictures_mm;
DROP TABLE IF EXISTS user_cichlids_species_pictures_mm;

CREATE TABLE user_cichlids_genus_names (
    uid INT PRIMARY KEY,
    title TEXT,
    deleted TINYINT NOT NULL DEFAULT 0
);

CREATE TABLE user_cichlids_species_names (
    uid INT PRIMARY KEY,
    title TEXT,
    deleted TINYINT NOT NULL DEFAULT 0
);

CREATE TABLE user_cichlids_category (
    uid INT PRIMARY KEY,
    title TEXT,
    deleted TINYINT NOT NULL DEFAULT 0
);

CREATE TABLE user_cichlids_species (
    uid INT PRIMARY KEY,
    title TEXT,
    genus INT NOT NULL,
    species INT NOT NULL,
    category INT NOT NULL,
    temp VARCHAR(50),
    ph VARCHAR(50),
    gh TEXT,
    kh TEXT,
    max_size VARCHAR(50),
    description TEXT,
    origin TEXT,
    habitat TEXT,
    morphs_text TEXT,
    breeding INT NOT NULL DEFAULT 0,
    aggro INT NOT NULL DEFAULT 0,
    inner_aggro INT NOT NULL DEFAULT 0,
    diet INT NOT NULL DEFAULT 0,
    links TEXT,
    link_texts TEXT,
    deleted TINYINT NOT NULL DEFAULT 0
);

CREATE TABLE user_cichlids_common_name (
    uid INT PRIMARY KEY,
    title TEXT,
    deleted TINYINT NOT NULL DEFAULT 0
);

CREATE TABLE user_cichlids_species_common_mm (
    uid_local INT NOT NULL,
    uid_foreign INT NOT NULL,
    tablenames VARCHAR(30) NOT NULL DEFAULT '',
    sorting INT NOT NULL DEFAULT 0
);

CREATE TABLE tx_realurl_uniqalias (
    uid INT PRIMARY KEY,
    tablename VARCHAR(255),
    value_alias VARCHAR(255),
    value_id INT NOT NULL
);

CREATE TABLE fe_users (
    uid INT PRIMARY KEY,
    username VARCHAR(50) NOT NULL,
    name VARCHAR(100) NOT NULL DEFAULT '',
    first_name VARCHAR(50) NOT NULL DEFAULT '',
    last_name VARCHAR(50) NOT NULL DEFAULT '',
    city VARCHAR(50) NOT NULL DEFAULT '',
    static_info_country CHAR(3) NOT NULL DEFAULT '',
    user_cichlids_auth0_image VARCHAR(512),
    crdate BIGINT NOT NULL DEFAULT 0,
    tstamp BIGINT NOT NULL DEFAULT 0,
    lastlogin BIGINT NOT NULL DEFAULT 0,
    tx_dixeasylogin_openid VARCHAR(255),
    email VARCHAR(80),
    deleted TINYINT NOT NULL DEFAULT 0,
    disable TINYINT NOT NULL DEFAULT 0,
    user_cichlids_profile_image BIGINT,
    user_cichlids_avatar_image BIGINT
);

CREATE TABLE fe_users_auth0 (
    sub VARCHAR(255) PRIMARY KEY,
    user_id INT
);

CREATE TABLE user_cichlids_pictures (
    uid INT PRIMARY KEY,
    pid INT NOT NULL DEFAULT 0,
    tstamp BIGINT NOT NULL DEFAULT 0,
    crdate BIGINT NOT NULL DEFAULT 0,
    deleted TINYINT NOT NULL DEFAULT 0,
    hidden TINYINT NOT NULL DEFAULT 0,
    title TEXT,
    fe_user INT NOT NULL,
    image VARCHAR(1000),
    description MEDIUMTEXT,
    rating FLOAT NOT NULL DEFAULT 0,
    rating_count INT NOT NULL DEFAULT 0,
    views INT NOT NULL DEFAULT 0,
    delete_tstamp BIGINT NOT NULL DEFAULT 0,
    delete_reason VARCHAR(500),
    delete_user INT
);

CREATE TABLE user_cichlids_tanks (
    uid INT PRIMARY KEY,
    fe_user INT NOT NULL,
    deleted TINYINT NOT NULL DEFAULT 0,
    hidden TINYINT NOT NULL DEFAULT 0,
    tstamp BIGINT NOT NULL DEFAULT 0,
    crdate BIGINT NOT NULL DEFAULT 0,
    category INT NOT NULL DEFAULT 0,
    title TEXT,
    description TEXT,
    gravel TEXT,
    plants TEXT,
    more_deco TEXT,
    light TEXT,
    light_duration TEXT,
    filtration TEXT,
    more_tec TEXT,
    water_ph TEXT,
    water_kh TEXT,
    water_gh TEXT,
    water_no2 TEXT,
    water_no3 TEXT,
    water_po4 TEXT,
    more_water TEXT,
    food TEXT,
    more TEXT,
    width INT NOT NULL DEFAULT 0,
    height INT NOT NULL DEFAULT 0,
    depth INT NOT NULL DEFAULT 0,
    unit TEXT,
    fish TEXT,
    fish_count TEXT,
    image BLOB,
    tank_images BLOB,
    deco_images BLOB,
    tec_images BLOB
);

CREATE TABLE user_cichlids_comments (
    uid INT PRIMARY KEY,
    type INT NOT NULL DEFAULT 0,
    item INT NOT NULL DEFAULT 0,
    rating INT NOT NULL DEFAULT 0,
    poster TEXT,
    note TEXT,
    fe_user INT NOT NULL,
    tstamp BIGINT NOT NULL DEFAULT 0,
    crdate BIGINT NOT NULL DEFAULT 0,
    deleted TINYINT NOT NULL DEFAULT 0,
    hidden TINYINT NOT NULL DEFAULT 0,
    delete_tstamp BIGINT NOT NULL DEFAULT 0,
    delete_reason VARCHAR(500),
    delete_user INT,
    score INT NOT NULL DEFAULT 0
);

CREATE TABLE user_cichlids_comments_rated (
    comment_uid INT NOT NULL,
    fe_user INT NOT NULL,
    rated INT NOT NULL,
    tstamp DATETIME NOT NULL
);

CREATE TABLE user_cichlids_gallery (
    uid INT PRIMARY KEY,
    tstamp BIGINT NOT NULL DEFAULT 0,
    crdate BIGINT NOT NULL DEFAULT 0,
    deleted TINYINT NOT NULL DEFAULT 0,
    hidden TINYINT NOT NULL DEFAULT 0,
    title TEXT,
    fe_user INT NOT NULL
);

CREATE TABLE user_cichlids_gallery_pictures_mm (
    uid_gallery INT NOT NULL,
    uid_picture INT NOT NULL,
    sorting INT NOT NULL DEFAULT 0
);

CREATE TABLE user_cichlids_species_pictures_mm (
    uid_local INT NOT NULL,
    uid_foreign INT NOT NULL,
    sorting INT NOT NULL DEFAULT 0
);

-- The forum lives in its own legacy database (cichlids_phorum5) on the same MySQL server as the
-- TYPO3 tables above, so ForumMigrationStep reaches it through fully qualified table names on the
-- same connection. The fixture mirrors that with a second database in this same container.
CREATE DATABASE IF NOT EXISTS cichlids_phorum5 CHARACTER SET latin1 COLLATE latin1_swedish_ci;

DROP TABLE IF EXISTS cichlids_phorum5.phorum_messages;
DROP TABLE IF EXISTS cichlids_phorum5.phorum_users;
DROP TABLE IF EXISTS cichlids_phorum5.phorum_files;

CREATE TABLE cichlids_phorum5.phorum_messages (
    message_id INT PRIMARY KEY,
    forum_id INT NOT NULL DEFAULT 0,
    thread INT NOT NULL DEFAULT 0,
    parent_id INT NOT NULL DEFAULT 0,
    author VARCHAR(255) NOT NULL DEFAULT '',
    subject VARCHAR(255) NOT NULL DEFAULT '',
    body TEXT NOT NULL,
    user_id INT NOT NULL DEFAULT 0,
    datestamp INT NOT NULL DEFAULT 0,
    status TINYINT NOT NULL DEFAULT 2,
    moved TINYINT(1) NOT NULL DEFAULT 0
) CHARACTER SET latin1;

CREATE TABLE cichlids_phorum5.phorum_users (
    user_id INT PRIMARY KEY,
    email VARCHAR(100) NOT NULL DEFAULT '',
    display_name VARCHAR(255) NOT NULL DEFAULT ''
) CHARACTER SET latin1;

CREATE TABLE cichlids_phorum5.phorum_files (
    file_id INT PRIMARY KEY,
    filename VARCHAR(255) NOT NULL DEFAULT '',
    file_data MEDIUMTEXT NOT NULL,
    message_id INT NOT NULL DEFAULT 0,
    link VARCHAR(10) NOT NULL DEFAULT ''
) CHARACTER SET latin1;
