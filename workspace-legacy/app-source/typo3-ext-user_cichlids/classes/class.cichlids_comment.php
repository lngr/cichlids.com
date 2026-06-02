<? require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_basic_object.php'); ?>
<?
    class cichlids_comment extends cichlids_basic_object {
	function cichlids_comment($row = array()) {
	    return parent::cichlids_basic_object($row);
	}
	var $db2fields = array(
	    "uid"	    => "uid",
	    "pid"   	    => "pid",
	    "tstamp"	    => "tstamp",
	    "crdate"	    => "crdate",
	    "deleted"	    => "deleted",
	    "hidden"	    => "hidden",

	    "type"	    => "type",
	    "title"	    => "title",
	    "item"	    => "item",
	    "parent"	    => "parent",
	    "rating"	    => "rating",
	    "posted"	    => "posted",
	    "poster"	    => "poster",
	    "note"	    => "note",
	    "fe_user"	    => "fe_user",
	    "ip"	    => "ip",
	);

	var $fields2db = array(
	    "uid"	    => array("type" => "int",	  "col" => "uid"),
	    "pid"   	    => array("type" => "int", 	  "col" => "pid"),
	    "tstamp"	    => array("type" => "int", 	  "col" => "tstamp"),
	    "crdate"	    => array("type" => "int", 	  "col" => "crdate"),
	    "deleted"	    => array("type" => "int", 	  "col" => "deleted"),
	    "hidden"	    => array("type" => "int", 	  "col" => "hidden"),

	    "type"	    => array("type" => "int",	  "col" => "type"),
	    "title"	    => array("type" => "string",  "col" => "title"),
	    "item"	    => array("type" => "int",	  "col" => "item"),
	    "parent"	    => array("type" => "int",	  "col" => "parent"),
	    "rating"	    => array("type" => "int",	  "col" => "rating"),
	    "posted"	    => array("type" => "int",	  "col" => "posted"),
	    "poster"	    => array("type" => "string",  "col" => "poster"),
	    "note"	    => array("type" => "string",  "col" => "note"),
	    "fe_user"	    => array("type" => "int",	  "col" => "fe_user"),
	    "ip"	    => array("type" => "string",  "col" => "ip"),
	);
    }

?>
