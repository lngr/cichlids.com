<?php
if (!defined ("TYPO3_MODE")) 	die ("Access denied.");

$TCA["user_cichlids_tanks"] = Array (
	"ctrl" => $TCA["user_cichlids_tanks"]["ctrl"],
	"interface" => Array (
		"showRecordFieldList" => "hidden,category,width,height,depth,description,gravel,plants,more_deco,light,light_duration,filtration,more_tec,water_ph,water_kh,water_gh,water_no2,water_no3,water_po4,food"
	),
	"feInterface" => $TCA["user_cichlids_tanks"]["feInterface"],
	"columns" => Array (
		"hidden" => Array (		
			"exclude" => 1,	
			"label" => "LLL:EXT:lang/locallang_general.php:LGL.hidden",
			"config" => Array (
				"type" => "check",
				"default" => "0"
			)
		),
		"title" => Array (		
			"exclude" => 1,		
			"label" => "Tank Title",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"fe_user" => Array (		
			"exclude" => 1,		
			"label" => "Frontend User",
			"config" => Array (
				"type" => "select",	
				"foreign_table" => "fe_users",	
				"foreign_table_where" => "ORDER BY fe_users.username",	
				"size" => 1,	
				"minitems" => 0,
				"maxitems" => 1,
				"default" => 2,
			)
		),
		"image" => Array (		
			"exclude" => 1,		
			"label" => "Tank Pictures",		
			"config" => Array (
				"type" => "group",	
				"internal_type" => "db",
				"allowed" => "user_cichlids_pictures",
				"size" => "1",
				"show_thumbs" => '1',
				"minitems" => 0,
				"maxitems" => 1,	
			)
		),
		"tank_images" => Array (		
			"exclude" => 1,		
			"label" => "Tank Pictures",		
			"config" => Array (
				"type" => "group",	
				"internal_type" => "db",
				"allowed" => "user_cichlids_pictures",
				"size" => "5",
				"show_thumbs" => '1',
				"minitems" => 0,
				"maxitems" => 20,	
			)
		),
		"category" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.category",		
			"config" => Array (
				"type" => "select",	
				"foreign_table" => "user_cichlids_category",	
				"foreign_table_where" => "ORDER BY user_cichlids_category.uid",	
				"size" => 1,	
				"minitems" => 0,
				"maxitems" => 1,
				"default" => 3,
			)
		),
		"width" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.width",		
			"config" => Array (
				"type" => "input",	
				"size" => "4",
				"max" => "4",
				"eval" => "int",
				"checkbox" => "0",
				"range" => Array (
					"upper" => "1000",
					"lower" => "10"
				),
				"default" => 0
			)
		),
		"height" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.height",		
			"config" => Array (
				"type" => "input",	
				"size" => "4",
				"max" => "4",
				"eval" => "int",
				"checkbox" => "0",
				"range" => Array (
					"upper" => "1000",
					"lower" => "10"
				),
				"default" => 0
			)
		),
		"depth" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.depth",		
			"config" => Array (
				"type" => "input",	
				"size" => "4",
				"max" => "4",
				"eval" => "int",
				"checkbox" => "0",
				"range" => Array (
					"upper" => "1000",
					"lower" => "10"
				),
				"default" => 0
			)
		),
		"description" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.description",		
			"config" => Array (
				"type" => "text",
				"cols" => "30",	
				"rows" => "5",
			)
		),
		"gravel" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.gravel",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"plants" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.plants",		
			"config" => Array (
				"type" => "text",
				"cols" => "30",	
				"rows" => "5",
			)
		),
		"more_deco" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.more_deco",		
			"config" => Array (
				"type" => "text",
				"cols" => "30",	
				"rows" => "5",
			)
		),
		"light" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.light",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"light_duration" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.light_duration",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"filtration" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.filtration",		
			"config" => Array (
				"type" => "text",
				"cols" => "30",	
				"rows" => "5",
			)
		),
		"more_tec" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.more_tec",		
			"config" => Array (
				"type" => "text",
				"cols" => "30",	
				"rows" => "5",
			)
		),
		"water_ph" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.water_ph",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"water_kh" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.water_kh",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"water_gh" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.water_gh",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"water_no2" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.water_no2",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"water_no3" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.water_no3",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"water_po4" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.water_po4",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"food" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_tanks.food",		
			"config" => Array (
				"type" => "text",
				"cols" => "30",	
				"rows" => "5",
			)
		),
		"fish" => Array (		
			"exclude" => 1,		
			"label" => "The Fish",		
			"config" => Array (
				"type" => "group",	
				"internal_type" => "db",
				"allowed" => "user_cichlids_species",
				"size" => "5",
				"show_thumbs" => '1',
				"minitems" => 0,
				"maxitems" => 40,	
			)
		),
		"fish_count" => Array (		
			"exclude" => 1,		
			"label" => "Fish Count, one per line",
			"config" => Array (
				"type" => "text",
				"cols" => "30",	
				"rows" => "5",
			)
		),
		"fish_images" => Array (		
			"exclude" => 1,		
			"label" => "Fish Pictures",		
			"config" => Array (
				"type" => "group",	
				"internal_type" => "db",
				"allowed" => "user_cichlids_pictures",
				"size" => "5",
				"show_thumbs" => '1',
				"minitems" => 0,
				"maxitems" => 20,	
			)
		),
	),
	"types" => Array (
		"0" => Array("showitem" => "hidden;;1;;1-1-1, title, fe_user, image, tank_images, category, width, height, depth, description, gravel, plants, more_deco, light, light_duration, filtration, more_tec, water_ph, water_kh, water_gh, water_no2, water_no3, water_po4, food, fish, fish_count, fish_images")
	),
	"palettes" => Array (
		"1" => Array("showitem" => "")
	)
);



$TCA["user_cichlids_category"] = Array (
	"ctrl" => $TCA["user_cichlids_category"]["ctrl"],
	"interface" => Array (
		"showRecordFieldList" => "hidden,title"
	),
	"feInterface" => $TCA["user_cichlids_category"]["feInterface"],
	"columns" => Array (
		"hidden" => Array (		
			"exclude" => 1,	
			"label" => "LLL:EXT:lang/locallang_general.php:LGL.hidden",
			"config" => Array (
				"type" => "check",
				"default" => "0"
			)
		),
		"title" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_category.title",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"image" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_pictures.image",		
			"config" => Array (
				"type" => "group",
				"internal_type" => "file",
				"allowed" => $GLOBALS["TYPO3_CONF_VARS"]["GFX"]["imagefile_ext"],	
				"max_size" => 10000,	
				"uploadfolder" => "uploads/tx_usercichlids",
				"show_thumbs" => 1,	
				"size" => 1,	
				"minitems" => 0,
				"maxitems" => 1,
			)
		),
	),
	"types" => Array (
		"0" => Array("showitem" => "hidden;;1;;1-1-1, title;;;;2-2-2, image")
	),
	"palettes" => Array (
		"1" => Array("showitem" => "")
	)
);



$TCA["user_cichlids_species"] = Array (
	"ctrl" => $TCA["user_cichlids_species"]["ctrl"],
	"interface" => Array (
		"showRecordFieldList" => "hidden,genus,species,common,category,ph,gh,kh,breeding,aggro,description,links,link_texts"
	),
	"feInterface" => $TCA["user_cichlids_species"]["feInterface"],
	"columns" => Array (
		"hidden" => Array (		
			"exclude" => 1,	
			"label" => "LLL:EXT:lang/locallang_general.php:LGL.hidden",
			"config" => Array (
				"type" => "check",
				"default" => "0"
			)
		),
		"title" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.ph",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"genus" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.genus",		
			"config" => Array (
				"type" => "select",	
				"foreign_table" => "user_cichlids_genus_names",	
				"foreign_table_where" => "ORDER BY user_cichlids_genus_names.title",	
				"size" => 1,	
				"minitems" => 1,
				"maxitems" => 1,
			)
		),
		"species" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.species",		
			"config" => Array (
				"type" => "select",	
				"foreign_table" => "user_cichlids_species_names",	
				"foreign_table_where" => "ORDER BY user_cichlids_species_names.title",	
				"size" => 1,	
				"minitems" => 1,
				"maxitems" => 1,
				"default" => '0',
			)
		),
		"common" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.common",		
			"config" => Array (
				"type" => "group",	
				"internal_type" => "db",	
				"allowed" => "user_cichlids_common_name",	
				"size" => 5,	
				"minitems" => 0,
				"maxitems" => 10,	
				"MM" => "user_cichlids_species_common_mm",
			)
		),

		#"images" => Array (		
		#	"exclude" => 1,		
		#	"label" => "Pictures",
		#	"config" => Array (
		#		"type" => "group",	
		#		"internal_type" => "db",	
		#		"allowed" => "user_cichlids_pictures",
		#		"size" => 5,	
		#		"minitems" => 0,
		#		"maxitems" => 210,	
		#		"MM" => "user_cichlids_species_pictures_mm",
		#	)
		#),
		"morphs_text" => Array (		
			"exclude" => 1,		
			"label" => "Known Morphs",
			"config" => Array (
				"type" => "text",
				"cols" => "30",
				"rows" => "5",
				"wizards" => Array(
					"_PADDING" => 2,
					"RTE" => Array(
						"notNewRecords" => 1,
						"RTEonly" => 1,
						"type" => "script",
						"title" => "Full screen Rich Text Editing|Formatteret redigering i hele vinduet",
						"icon" => "wizard_rte2.gif",
						"script" => "wizard_rte.php",
					),
				),
			)
		),
		"category" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.category",		
			"config" => Array (
				"type" => "select",	
				"foreign_table" => "user_cichlids_category",	
				"foreign_table_where" => "ORDER BY user_cichlids_category.uid",	
				"size" => 1,	
				"minitems" => 0,
				"maxitems" => 1,
				"default" => 3,
			)
		),
		"ph" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.ph",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"gh" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.gh",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"temp" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.temp",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"max_size" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.max_size",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"origin" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.origin",		
			"config" => Array (
				"type" => "input",	
				"size" => "100",
			)
		),
		"habitat" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.habitat",		
			"config" => Array (
				"type" => "input",	
				"size" => "100",
			)
		),
		"diet" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.diet",		
			"config" => Array (
				"type" => "select",
				"items" => Array (
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.diet.I.0", "0"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.diet.I.1", "1"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.diet.I.2", "2"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.diet.I.3", "3"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.diet.I.4", "4"),
				),
				"size" => 1,	
				"maxitems" => 1,
			)
		),
		#"kh" => Array (		
		#	"exclude" => 1,		
		#	"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.kh",		
		#	"config" => Array (
		#		"type" => "input",	
		#		"size" => "30",
		#	)
		#),
		"breeding" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.breeding",		
			"config" => Array (
				"type" => "select",
				"items" => Array (
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.breeding.I.0", "0"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.breeding.I.1", "1"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.breeding.I.2", "2"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.breeding.I.3", "3"),
				),
				"size" => 1,	
				"maxitems" => 1,
			)
		),
		"aggro" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.aggro",		
			"config" => Array (
				"type" => "select",
				"items" => Array (
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.aggro.I.0", "0"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.aggro.I.1", "1"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.aggro.I.2", "2"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.aggro.I.3", "3"),
				),
				"size" => 1,	
				"maxitems" => 1,
			)
		),
		"inner_aggro" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.inner_aggro",		
			"config" => Array (
				"type" => "select",
				"items" => Array (
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.aggro.I.0", "0"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.aggro.I.1", "1"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.aggro.I.2", "2"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.aggro.I.3", "3"),
				),
				"size" => 1,	
				"maxitems" => 1,
			)
		),
		"description" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.description",		
			"config" => Array (
				"type" => "text",
				"cols" => "30",
				"rows" => "5",
				"wizards" => Array(
					"_PADDING" => 2,
					"RTE" => Array(
						"notNewRecords" => 1,
						"RTEonly" => 1,
						"type" => "script",
						"title" => "Full screen Rich Text Editing|Formatteret redigering i hele vinduet",
						"icon" => "wizard_rte2.gif",
						"script" => "wizard_rte.php",
					),
				),
			)
		),
		"links" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.links",		
			"config" => Array (
				"type" => "text",
				"cols" => "30",	
				"rows" => "5",
			)
		),
		"link_texts" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species.link_texts",		
			"config" => Array (
				"type" => "text",
				"cols" => "30",	
				"rows" => "5",
			)
		),
	),
	"types" => Array (
		"0" => Array("showitem" => "hidden;;1;;1-1-1, title, genus, species, common, known_morphs, category, origin, habitat, images, ph, gh, kh, temp, max_size, diet, breeding, aggro, inner_aggro, description;;;richtext[paste|bold|italic|underline|formatblock|class|left|center|right|orderedlist|unorderedlist|outdent|indent|link|image]:rte_transform[mode=ts], links, link_texts")
	),
	"palettes" => Array (
		"1" => Array("showitem" => "")
	)
);



$TCA["user_cichlids_common_name"] = Array (
	"ctrl" => $TCA["user_cichlids_common_name"]["ctrl"],
	"interface" => Array (
		"showRecordFieldList" => "hidden"
	),
	"feInterface" => $TCA["user_cichlids_common_name"]["feInterface"],
	"columns" => Array (
		"hidden" => Array (		
			"exclude" => 1,	
			"label" => "LLL:EXT:lang/locallang_general.php:LGL.hidden",
			"config" => Array (
				"type" => "check",
				"default" => "0"
			)
		),
		"title" => Array (		
			"exclude" => 1,		
			"label" => "Title",
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
	),
	"types" => Array (
		"0" => Array("showitem" => "hidden;;1;;1-1-1, title")
	),
	"palettes" => Array (
		"1" => Array("showitem" => "")
	)
);



$TCA["user_cichlids_pictures"] = Array (
	"ctrl" => $TCA["user_cichlids_pictures"]["ctrl"],
	"interface" => Array (
		"showRecordFieldList" => "hidden,title,fe_user,species,image,description"
	),
	"feInterface" => $TCA["user_cichlids_pictures"]["feInterface"],
	"columns" => Array (
		"hidden" => Array (		
			"exclude" => 1,	
			"label" => "LLL:EXT:lang/locallang_general.php:LGL.hidden",
			"config" => Array (
				"type" => "check",
				"default" => "0"
			)
		),
		"title" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_pictures.title",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"fe_user" => Array (		
			"exclude" => 1,		
			"label" => "Frontend User",
			"config" => Array (
				"type" => "select",	
				"foreign_table" => "fe_users",	
				"foreign_table_where" => "ORDER BY fe_users.username",	
				"size" => 1,	
				"minitems" => 0,
				"maxitems" => 1,
				"default" => 2,
			)
		),
		"species" => Array (		
			"exclude" => 1,		
			"label" => "Species",
			"config" => Array (
				"type" => "group",	
				"internal_type" => "db",	
				"allowed" => "user_cichlids_species",
				"size" => 1,	
				"minitems" => 0,
				"maxitems" => 1,	
				"MM" => "user_cichlids_species_pictures_mm",
			)
		),


		"image" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_pictures.image",		
			"config" => Array (
				"type" => "group",
				"internal_type" => "file",
				"allowed" => $GLOBALS["TYPO3_CONF_VARS"]["GFX"]["imagefile_ext"],	
				"max_size" => 1000,	
				"uploadfolder" => "uploads/tx_usercichlids",
				"show_thumbs" => 1,	
				"size" => 1,	
				"minitems" => 0,
				"maxitems" => 1,
			)
		),
		"description" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_pictures.description",		
			"config" => Array (
				"type" => "text",
				"cols" => "30",	
				"rows" => "5",
			)
		),
	),
	"types" => Array (
		"0" => Array("showitem" => "hidden;;1;;1-1-1, title;;;;2-2-2, fe_user;;;;3-3-3, species, image, description;;;richtext[paste|bold|italic|underline|formatblock|class|left|center|right|orderedlist|unorderedlist|outdent|indent|link|image]:rte_transform[mode=ts]")
	),
	"palettes" => Array (
		"1" => Array("showitem" => "")
	)
);



$TCA["user_cichlids_comments"] = Array (
	"ctrl" => $TCA["user_cichlids_comments"]["ctrl"],
	"interface" => Array (
		"showRecordFieldList" => "hidden,type,item,parent,rating"
	),
	"feInterface" => $TCA["user_cichlids_comments"]["feInterface"],
	"columns" => Array (
		"hidden" => Array (		
			"exclude" => 1,	
			"label" => "LLL:EXT:lang/locallang_general.php:LGL.hidden",
			"config" => Array (
				"type" => "check",
				"default" => "0"
			)
		),
		"crdate" => Array (
                        "exclude" => 1,
                        "label" => "Created:",
                        "config" => Array (
                                "type" => "input",
                                "size" => "8",
                                "max" => "20",
                                "eval" => "datetime",
                                "default" => time(),
                                "checkbox" => "0"
                        )
                ),

		"fe_user" => Array (		
			"exclude" => 1,		
			"label" => "Frontend User",
			"config" => Array (
				"type" => "select",	
				"foreign_table" => "fe_users",	
				"foreign_table_where" => "ORDER BY fe_users.username",	
				"size" => 1,	
				"minitems" => 0,
				"maxitems" => 1,
				"default" => 2,
			)
		),
		"poster" => Array (		
			"exclude" => 1,		
			"label" => "Posted by, if anonymous",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"ip" => Array (		
			"exclude" => 1,		
			"label" => "IP address",
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
		"type" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_comments.type",		
			"config" => Array (
				"type" => "select",
				"items" => Array (
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_comments.type.I.0", "0"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_comments.type.I.1", "1"),
					Array("LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_comments.type.I.2", "2"),
				),
				"size" => 1,	
				"maxitems" => 1,
			)
		),
		"item" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_comments.item",		
			"config" => Array (
				"type" => "group",	
				"internal_type" => "db",	
				"allowed" => "*",	
				"prepend_tname" => 1,	
				"size" => 1,	
				"minitems" => 0,
				"maxitems" => 1,
			)
		),
		"parent" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_comments.parent",		
			"config" => Array (
				"type" => "group",	
				"internal_type" => "db",	
				"allowed" => "user_cichlids_comments",	
				"size" => 1,	
				"minitems" => 0,
				"maxitems" => 1,
			)
		),
		"note" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_pictures.description",		
			"config" => Array (
				"type" => "text",
				"cols" => "30",	
				"rows" => "5",
			)
		),
		"rating" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_comments.rating",		
			"config" => Array (
				"type" => "input",	
				"size" => "4",
				"max" => "4",
				"eval" => "int",
				"range" => Array (
					"upper" => "5",
					"lower" => "0"
				),
			)
		),
	),
	"types" => Array (
		"0" => Array("showitem" => "hidden;;1;;1-1-1, crdate, type, fe_user, poster, ip, item, rating, note")
	),
	"palettes" => Array (
		"1" => Array("showitem" => "")
	)
);



$TCA["user_cichlids_genus_names"] = Array (
	"ctrl" => $TCA["user_cichlids_genus_names"]["ctrl"],
	"interface" => Array (
		"showRecordFieldList" => "hidden,title"
	),
	"feInterface" => $TCA["user_cichlids_genus_names"]["feInterface"],
	"columns" => Array (
		"hidden" => Array (		
			"exclude" => 1,	
			"label" => "LLL:EXT:lang/locallang_general.php:LGL.hidden",
			"config" => Array (
				"type" => "check",
				"default" => "0"
			)
		),
		"title" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_genus_names.title",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",
			)
		),
	),
	"types" => Array (
		"0" => Array("showitem" => "hidden;;1;;1-1-1, title;;;;2-2-2")
	),
	"palettes" => Array (
		"1" => Array("showitem" => "")
	)
);



$TCA["user_cichlids_species_names"] = Array (
	"ctrl" => $TCA["user_cichlids_species_names"]["ctrl"],
	"interface" => Array (
		"showRecordFieldList" => "hidden,title"
	),
	"feInterface" => $TCA["user_cichlids_species_names"]["feInterface"],
	"columns" => Array (
		"hidden" => Array (		
			"exclude" => 1,	
			"label" => "LLL:EXT:lang/locallang_general.php:LGL.hidden",
			"config" => Array (
				"type" => "check",
				"default" => "0"
			)
		),
		"title" => Array (		
			"exclude" => 1,		
			"label" => "LLL:EXT:user_cichlids/locallang_db.php:user_cichlids_species_names.title",		
			"config" => Array (
				"type" => "input",	
				"size" => "30",	
				"eval" => "required",
			)
		),
	),
	"types" => Array (
		"0" => Array("showitem" => "hidden;;1;;1-1-1, title;;;;2-2-2")
	),
	"palettes" => Array (
		"1" => Array("showitem" => "")
	)
);
?>
