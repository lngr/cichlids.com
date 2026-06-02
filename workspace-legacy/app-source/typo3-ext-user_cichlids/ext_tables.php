<?php
if (!defined ("TYPO3_MODE")) 	die ("Access denied.");

t3lib_extMgm::allowTableOnStandardPages("user_cichlids_tanks");


t3lib_extMgm::addToInsertRecords("user_cichlids_tanks");

$TCA["user_cichlids_tanks"] = Array (
	"ctrl" => Array (
		"title" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks",		
		"label" => "title",	
		"label_alt" => "fe_user",
		"label_alt_force" => "1",
		"tstamp" => "tstamp",
		"crdate" => "crdate",
		"cruser_id" => "cruser_id",
		"default_sortby" => "ORDER BY crdate",	
		"delete" => "deleted",	
		"enablecolumns" => Array (		
			"disabled" => "hidden",
		),
		"dynamicConfigFile" => t3lib_extMgm::extPath($_EXTKEY)."tca.php",
		"iconfile" => t3lib_extMgm::extRelPath($_EXTKEY)."icon_user_cichlids_tanks.gif",
	),
	"feInterface" => Array (
		"fe_admin_fieldList" => "hidden, title, category, width, height, depth, unit, description, gravel, plants, more_deco, light, light_duration, filtration, more_tec, water_ph, water_kh, water_gh, water_no2, water_no3, water_po4, food",
	)
);


t3lib_extMgm::allowTableOnStandardPages("user_cichlids_category");
t3lib_extMgm::addToInsertRecords("user_cichlids_category");

$TCA["user_cichlids_category"] = Array (
	"ctrl" => Array (
		"title" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_category",		
		"label" => "title",	
		"tstamp" => "tstamp",
		"crdate" => "crdate",
		"cruser_id" => "cruser_id",
		"default_sortby" => "ORDER BY crdate",	
		"delete" => "deleted",	
		"enablecolumns" => Array (		
			"disabled" => "hidden",
		),
		"dynamicConfigFile" => t3lib_extMgm::extPath($_EXTKEY)."tca.php",
		"iconfile" => t3lib_extMgm::extRelPath($_EXTKEY)."icon_user_cichlids_category.gif",
	),
	"feInterface" => Array (
		"fe_admin_fieldList" => "hidden, title",
	)
);

$TCA["user_cichlids_species"] = Array (
	"ctrl" => Array (
		"title" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species",		
		"label" => "title",	
		"tstamp" => "tstamp",
		"crdate" => "crdate",
		"cruser_id" => "cruser_id",
		"default_sortby" => "ORDER BY crdate",	
		"delete" => "deleted",	
		"enablecolumns" => Array (		
			"disabled" => "hidden",
		),
		"dynamicConfigFile" => t3lib_extMgm::extPath($_EXTKEY)."tca.php",
		"iconfile" => t3lib_extMgm::extRelPath($_EXTKEY)."icon_user_cichlids_species.gif",
	),
	"feInterface" => Array (
		"fe_admin_fieldList" => "hidden, genus, species, common, category, ph, gh, kh, breeding, aggro, description, links, link_texts",
	)
);

$TCA["user_cichlids_common_name"] = Array (
	"ctrl" => Array (
		"title" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_common_name",		
		"label" => "title",	
		"tstamp" => "tstamp",
		"crdate" => "crdate",
		"cruser_id" => "cruser_id",
		"default_sortby" => "ORDER BY crdate",	
		"delete" => "deleted",	
		"enablecolumns" => Array (		
			"disabled" => "hidden",
		),
		"dynamicConfigFile" => t3lib_extMgm::extPath($_EXTKEY)."tca.php",
		"iconfile" => t3lib_extMgm::extRelPath($_EXTKEY)."icon_user_cichlids_common_name.gif",
	),
	"feInterface" => Array (
		"fe_admin_fieldList" => "hidden, title",
	)
);


t3lib_extMgm::allowTableOnStandardPages("user_cichlids_pictures");


t3lib_extMgm::addToInsertRecords("user_cichlids_pictures");

$TCA["user_cichlids_pictures"] = Array (
	"ctrl" => Array (
		"title" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_pictures",		
		"label" => "title",	
		"tstamp" => "tstamp",
		"crdate" => "crdate",
		"cruser_id" => "cruser_id",
		"default_sortby" => "ORDER BY crdate",	
		"delete" => "deleted",	
		"enablecolumns" => Array (		
			"disabled" => "hidden",
		),
		"thumbnail" => "image",
		"selicon_field" => "image",
		"selicon_field_path" => "uploads/tx_usercichlids",
		"dynamicConfigFile" => t3lib_extMgm::extPath($_EXTKEY)."tca.php",
		"iconfile" => t3lib_extMgm::extRelPath($_EXTKEY)."icon_user_cichlids_pictures.gif",
	),
	"feInterface" => Array (
		"fe_admin_fieldList" => "hidden, title, fe_user, species, image, description",
	)
);


t3lib_extMgm::allowTableOnStandardPages("user_cichlids_comments");


t3lib_extMgm::addToInsertRecords("user_cichlids_comments");

$TCA["user_cichlids_comments"] = Array (
	"ctrl" => Array (
		"title" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_comments",		
		"label" => "uid",	
		"tstamp" => "tstamp",
		"crdate" => "crdate",
		"cruser_id" => "cruser_id",
		"default_sortby" => "ORDER BY crdate",	
		"delete" => "deleted",	
		"enablecolumns" => Array (		
			"disabled" => "hidden",
		),
		"dynamicConfigFile" => t3lib_extMgm::extPath($_EXTKEY)."tca.php",
		"iconfile" => t3lib_extMgm::extRelPath($_EXTKEY)."icon_user_cichlids_comments.gif",
	),
	"feInterface" => Array (
		"fe_admin_fieldList" => "hidden, type, item, parent, rating",
	)
);

$TCA["user_cichlids_genus_names"] = Array (
	"ctrl" => Array (
		"title" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_genus_names",		
		"label" => "title",	
		"tstamp" => "tstamp",
		"crdate" => "crdate",
		"cruser_id" => "cruser_id",
		"default_sortby" => "ORDER BY crdate",	
		"delete" => "deleted",	
		"enablecolumns" => Array (		
			"disabled" => "hidden",
		),
		"dynamicConfigFile" => t3lib_extMgm::extPath($_EXTKEY)."tca.php",
		"iconfile" => t3lib_extMgm::extRelPath($_EXTKEY)."icon_user_cichlids_genus_names.gif",
	),
	"feInterface" => Array (
		"fe_admin_fieldList" => "hidden, title",
	)
);

$TCA["user_cichlids_species_names"] = Array (
	"ctrl" => Array (
		"title" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species_names",		
		"label" => "title",	
		"tstamp" => "tstamp",
		"crdate" => "crdate",
		"cruser_id" => "cruser_id",
		"default_sortby" => "ORDER BY crdate",	
		"delete" => "deleted",	
		"enablecolumns" => Array (		
			"disabled" => "hidden",
		),
		"dynamicConfigFile" => t3lib_extMgm::extPath($_EXTKEY)."tca.php",
		"iconfile" => t3lib_extMgm::extRelPath($_EXTKEY)."icon_user_cichlids_species_names.gif",
	),
	"feInterface" => Array (
		"fe_admin_fieldList" => "hidden, title",
	)
);

$TCA["user_cichlids_morph_names"] = Array (
	"ctrl" => Array (
		"title" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_morph_names",		
		"label" => "title",	
		"tstamp" => "tstamp",
		"crdate" => "crdate",
		"cruser_id" => "cruser_id",
		"default_sortby" => "ORDER BY crdate",	
		"delete" => "deleted",	
		"enablecolumns" => Array (		
			"disabled" => "hidden",
		),
		"dynamicConfigFile" => t3lib_extMgm::extPath($_EXTKEY)."tca.php",
		"iconfile" => t3lib_extMgm::extRelPath($_EXTKEY)."icon_user_cichlids_morph_names.gif",
	),
	"feInterface" => Array (
		"fe_admin_fieldList" => "hidden, title",
	)
);


t3lib_div::loadTCA("tt_content");
$TCA["tt_content"]["types"]["list"]["subtypes_excludelist"][$_EXTKEY."_pi1"]="layout";


t3lib_extMgm::addPlugin(Array("LLL:EXT:user_cichlids/locallang_db.php:tt_content.list_type_pi1", $_EXTKEY."_pi1"),"list_type");


t3lib_div::loadTCA("tt_content");
$TCA["tt_content"]["types"]["list"]["subtypes_excludelist"][$_EXTKEY."_pi2"]="layout";


t3lib_extMgm::addPlugin(Array("LLL:EXT:user_cichlids/locallang_db.php:tt_content.list_type_pi2", $_EXTKEY."_pi2"),"list_type");
t3lib_extMgm::addPlugin(Array("LLL:EXT:user_cichlids/locallang_db.php:tt_content.list_type_pi3", $_EXTKEY."_pi3"),"list_type");

t3lib_extMgm::addStaticFile($_EXTKEY,"pi2/static/","Cichlids.com User Upload");
?>
