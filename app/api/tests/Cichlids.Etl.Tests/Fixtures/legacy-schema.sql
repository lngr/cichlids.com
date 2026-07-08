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
    disable TINYINT NOT NULL DEFAULT 0
);

CREATE TABLE fe_users_auth0 (
    sub VARCHAR(255) PRIMARY KEY,
    user_id INT
);

CREATE TABLE user_cichlids_pictures (
    uid INT PRIMARY KEY,
    fe_user INT NOT NULL
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
    fish_count TEXT
);

CREATE TABLE user_cichlids_comments (
    uid INT PRIMARY KEY,
    fe_user INT NOT NULL
);
