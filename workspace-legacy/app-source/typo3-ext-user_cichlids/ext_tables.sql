#
# Table structure for table 'user_cichlids_tanks'
#
CREATE TABLE user_cichlids_tanks (
	uid int(11) unsigned NOT NULL auto_increment,
	pid int(11) unsigned DEFAULT '0' NOT NULL,
	tstamp int(11) unsigned DEFAULT '0' NOT NULL,
	crdate int(11) unsigned DEFAULT '0' NOT NULL,
	cruser_id int(11) unsigned DEFAULT '0' NOT NULL,
	deleted tinyint(4) unsigned DEFAULT '0' NOT NULL,
	hidden tinyint(4) unsigned DEFAULT '0' NOT NULL,
	realurltitle tinytext NOT NULL,
	title tinytext NOT NULL,
	not_shown tinyint(4) unsigned DEFAULT '0' NOT NULL,
	fe_user int(11) unsigned DEFAULT '0' NOT NULL,
	category int(11) unsigned DEFAULT '0' NOT NULL,
	image blob NOT NULL,
	tank_images blob NOT NULL,
	width int(11) DEFAULT '0' NOT NULL,
	height int(11) DEFAULT '0' NOT NULL,
	depth int(11) DEFAULT '0' NOT NULL,
	unit tinytext NOT NULL,
	description text NOT NULL,
	gravel tinytext NOT NULL,
	plants text NOT NULL,
	more_deco text NOT NULL,
	deco_images blob NOT NULL,
	light tinytext NOT NULL,
	light_duration tinytext NOT NULL,
	filtration text NOT NULL,
	more_tec text NOT NULL,
	tec_images blob NOT NULL,
	water_ph tinytext NOT NULL,
	water_kh tinytext NOT NULL,
	water_gh tinytext NOT NULL,
	water_no2 tinytext NOT NULL,
	water_no3 tinytext NOT NULL,
	water_po4 tinytext NOT NULL,
	more_water text NOT NULL,
	food text NOT NULL,
	fish blob NOT NULL,
	fish_count text NOT NULL,
	fish_images blob NOT NULL,
	more text NOT NULL,
	PRIMARY KEY (uid),
	KEY parent (pid)
);



#
# Table structure for table 'user_cichlids_category'
#
CREATE TABLE user_cichlids_category (
	uid int(11) unsigned NOT NULL auto_increment,
	pid int(11) unsigned DEFAULT '0' NOT NULL,
	tstamp int(11) unsigned DEFAULT '0' NOT NULL,
	crdate int(11) unsigned DEFAULT '0' NOT NULL,
	cruser_id int(11) unsigned DEFAULT '0' NOT NULL,
	deleted tinyint(4) unsigned DEFAULT '0' NOT NULL,
	hidden tinyint(4) unsigned DEFAULT '0' NOT NULL,
	title tinytext NOT NULL,
	image blob NOT NULL,
	PRIMARY KEY (uid),
	KEY parent (pid)
);




#
# Table structure for table 'user_cichlids_species_common_mm'
# 
#
CREATE TABLE user_cichlids_species_common_mm (
  uid_local int(11) unsigned DEFAULT '0' NOT NULL,
  uid_foreign int(11) unsigned DEFAULT '0' NOT NULL,
  tablenames varchar(30) DEFAULT '' NOT NULL,
  sorting int(11) unsigned DEFAULT '0' NOT NULL,
  KEY uid_local (uid_local),
  KEY uid_foreign (uid_foreign)
);

#
# Table structure for table 'user_cichlids_species_pictures_mm'
# 
#
CREATE TABLE user_cichlids_species_pictures_mm (
  uid_local int(11) unsigned DEFAULT '0' NOT NULL,
  uid_foreign int(11) unsigned DEFAULT '0' NOT NULL,
  tablenames varchar(30) DEFAULT '' NOT NULL,
  sorting int(11) unsigned DEFAULT '0' NOT NULL,
  KEY uid_local (uid_local),
  KEY uid_foreign (uid_foreign)
);



#
# Table structure for table 'user_cichlids_species'
#
CREATE TABLE user_cichlids_species (
	uid int(11) unsigned NOT NULL auto_increment,
	pid int(11) unsigned DEFAULT '0' NOT NULL,
	tstamp int(11) unsigned DEFAULT '0' NOT NULL,
	crdate int(11) unsigned DEFAULT '0' NOT NULL,
	cruser_id int(11) unsigned DEFAULT '0' NOT NULL,
	deleted tinyint(4) unsigned DEFAULT '0' NOT NULL,
	hidden tinyint(4) unsigned DEFAULT '0' NOT NULL,
	title tinytext NOT NULL,
	genus int(11) unsigned DEFAULT '0' NOT NULL,
	species int(11) unsigned DEFAULT '0' NOT NULL,
	morphs_text text NOT NULL,
	morphs blob NOT NULL,
	common int(11) unsigned DEFAULT '0' NOT NULL,
	category int(11) unsigned DEFAULT '0' NOT NULL,
	gh tinytext NOT NULL,
	kh tinytext NOT NULL,
	breeding int(11) unsigned DEFAULT '0' NOT NULL,
	aggro int(11) unsigned DEFAULT '0' NOT NULL,
	description text NOT NULL,
	links text NOT NULL,
	link_texts text NOT NULL,
	images blob NOT NULL,
	ph varchar(50) NOT NULL,
	temp varchar(50) NOT NULL,
	max_size varchar(50) NOT NULL,
	origin text NOT NULL,
	habitat text NOT NULL,
	diet int(11) unsigned DEFAULT '0' NOT NULL,
	inner_aggro int(11) unsigned DEFAULT '0' NOT NULL,
	
	
	PRIMARY KEY (uid),
	KEY parent (pid),
	UNIQUE `genus_species` ( `genus` , `species` ) 
);


#
# Table structure for table 'user_cichlids_gallery'
#
CREATE TABLE user_cichlids_gallery (
	uid int(11) unsigned NOT NULL auto_increment,
	pid int(11) unsigned DEFAULT '0' NOT NULL,
	tstamp int(11) unsigned DEFAULT '0' NOT NULL,
	crdate int(11) unsigned DEFAULT '0' NOT NULL,
	cruser_id int(11) unsigned DEFAULT '0' NOT NULL,
	deleted tinyint(4) unsigned DEFAULT '0' NOT NULL,
	hidden tinyint(4) unsigned DEFAULT '0' NOT NULL,
	title text NOT NULL,
	fe_user int(11) unsigned DEFAULT '0' NOT NULL,
	num_pictures int(11) unsigned DEFAULT '0' NOT NULL,
	views int(11) unsigned DEFAULT '0' NOT NULL,
	
	PRIMARY KEY (uid),
	KEY parent (pid)
);



#
# Table structure for table 'user_cichlids_gallery_pictures_mm'
#
CREATE TABLE user_cichlids_gallery_pictures_mm (
  uid_gallery int(11) unsigned DEFAULT '0' NOT NULL,
  uid_picture int(11) unsigned DEFAULT '0' NOT NULL,
  sorting int(11) unsigned DEFAULT '0' NOT NULL,
  KEY uid_gallery (uid_gallery),
  KEY uid_picture (uid_picture)
);


#
# Table structure for table 'user_cichlids_common_name'
#
CREATE TABLE user_cichlids_common_name (
	uid int(11) unsigned NOT NULL auto_increment,
	pid int(11) unsigned DEFAULT '0' NOT NULL,
	tstamp int(11) unsigned DEFAULT '0' NOT NULL,
	crdate int(11) unsigned DEFAULT '0' NOT NULL,
	cruser_id int(11) unsigned DEFAULT '0' NOT NULL,
	deleted tinyint(4) unsigned DEFAULT '0' NOT NULL,
	hidden tinyint(4) unsigned DEFAULT '0' NOT NULL,
	title tinytext NOT NULL,
	PRIMARY KEY (uid),
	KEY parent (pid)
);



#
# Table structure for table 'user_cichlids_pictures'
#
CREATE TABLE user_cichlids_pictures (
	uid int(11) unsigned NOT NULL auto_increment,
	pid int(11) unsigned DEFAULT '0' NOT NULL,
	tstamp int(11) unsigned DEFAULT '0' NOT NULL,
	crdate int(11) unsigned DEFAULT '0' NOT NULL,
	cruser_id int(11) unsigned DEFAULT '0' NOT NULL,
	deleted tinyint(4) unsigned DEFAULT '0' NOT NULL,
	hidden tinyint(4) unsigned DEFAULT '0' NOT NULL,
	title tinytext NOT NULL,
	fe_user int(11) unsigned DEFAULT '0' NOT NULL,
	species blob NOT NULL,
	image blob NOT NULL,
	description text NOT NULL,
	rating float DEFAULT 0 NOT NULL,
	rating_count int(11) unsigned DEFAULT '0' NOT NULL,
	views int(11) unsigned DEFAULT '0' NOT NULL,
	PRIMARY KEY (uid),
	KEY parent (pid)
);



#
# Table structure for table 'user_cichlids_comments'
#
CREATE TABLE user_cichlids_comments (
	uid int(11) unsigned NOT NULL auto_increment,
	pid int(11) unsigned DEFAULT '0' NOT NULL,
	tstamp int(11) unsigned DEFAULT '0' NOT NULL,
	crdate int(11) unsigned DEFAULT '0' NOT NULL,
	cruser_id int(11) unsigned DEFAULT '0' NOT NULL,
	deleted tinyint(4) unsigned DEFAULT '0' NOT NULL,
	hidden tinyint(4) unsigned DEFAULT '0' NOT NULL,
	type int(11) unsigned DEFAULT '0' NOT NULL,
	title tinytext NOT NULL,
	item int(11) NOT NULL,
	parent blob NOT NULL,
	rating int(11) DEFAULT '0' NOT NULL,
	posted int(11) unsigned DEFAULT '0' NOT NULL,
	poster tinytext NOT NULL,
	ip tinytext NOT NULL,
	note text NOT NULL,
	fe_user int(11) unsigned DEFAULT '0' NOT NULL,
	
	PRIMARY KEY (uid),
	KEY parent (pid)
);



#
# Table structure for table 'user_cichlids_genus_names'
#
CREATE TABLE user_cichlids_genus_names (
	uid int(11) unsigned NOT NULL auto_increment,
	pid int(11) unsigned DEFAULT '0' NOT NULL,
	tstamp int(11) unsigned DEFAULT '0' NOT NULL,
	crdate int(11) unsigned DEFAULT '0' NOT NULL,
	cruser_id int(11) unsigned DEFAULT '0' NOT NULL,
	deleted tinyint(4) unsigned DEFAULT '0' NOT NULL,
	hidden tinyint(4) unsigned DEFAULT '0' NOT NULL,
	title tinytext NOT NULL,
	
	PRIMARY KEY (uid),
	KEY parent (pid)
);



#
# Table structure for table 'user_cichlids_species_names'
#
CREATE TABLE user_cichlids_species_names (
	uid int(11) unsigned NOT NULL auto_increment,
	pid int(11) unsigned DEFAULT '0' NOT NULL,
	tstamp int(11) unsigned DEFAULT '0' NOT NULL,
	crdate int(11) unsigned DEFAULT '0' NOT NULL,
	cruser_id int(11) unsigned DEFAULT '0' NOT NULL,
	deleted tinyint(4) unsigned DEFAULT '0' NOT NULL,
	hidden tinyint(4) unsigned DEFAULT '0' NOT NULL,
	title tinytext NOT NULL,
	
	PRIMARY KEY (uid),
	KEY parent (pid)
);

#
# Table structure for table 'user_cichlids_morph_names'
#
CREATE TABLE user_cichlids_morph_names (
	uid int(11) unsigned NOT NULL auto_increment,
	pid int(11) unsigned DEFAULT '0' NOT NULL,
	tstamp int(11) unsigned DEFAULT '0' NOT NULL,
	crdate int(11) unsigned DEFAULT '0' NOT NULL,
	cruser_id int(11) unsigned DEFAULT '0' NOT NULL,
	deleted tinyint(4) unsigned DEFAULT '0' NOT NULL,
	hidden tinyint(4) unsigned DEFAULT '0' NOT NULL,
	title tinytext NOT NULL,
	
	PRIMARY KEY (uid),
	KEY parent (pid)
);
