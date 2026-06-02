<? require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_basic_object.php'); ?>
<?

    class cichlids_feuser extends cichlids_basic_object {
	var $username = "anonymous";
	function cichlids_feuser($row = array()) {
	    return parent::cichlids_basic_object($row);
	}

	var $db2fields = array(
	    "uid"	    => "uid",
	    "pid"   	    => "pid",
	    "tstamp"	    => "tstamp",
	    "crdate"	    => "crdate",
	    "deleted"	    => "deleted",
	    "hidden"	    => "hidden",
	    "title"	    => "title",
	    "fe_user"	    => "fe_user",
	    "species"	    => "species",
	    "image"	    => "image",
	    "description"   => "description",
	    "rating"	    => "rating",
	    "rating_count"  => "rating_count",
	    "views"	    => "views",
	);
	var $fields2db = array(
	    "uid"	    => array("type" => "int",	  "col" => "uid"),
	    "pid"   	    => array("type" => "int", 	  "col" => "pid"),
	    "tstamp"	    => array("type" => "int", 	  "col" => "tstamp"),
	    "crdate"	    => array("type" => "int", 	  "col" => "crdate"),
	    "deleted"	    => array("type" => "int", 	  "col" => "deleted"),
	    "hidden"	    => array("type" => "int", 	  "col" => "hidden"),
	    "title"	    => array("type" => "string",  "col" => "title"),
	    "fe_user"	    => array("type" => "int",	  "col" => "fe_user"),

	    "species"	    => array("type" => "int",	  "col" => "species"),
	    "image"	    => array("type" => "string",  "col" => "image"),
	    "description"   => array("type" => "string",  "col" => "description"),
	    "rating"	    => array("type" => "float",   "col" => "rating"),
	    "rating_count"  => array("type" => "int", 	  "col" => "rating_cunt"),
	    "views"	    => array("type" => "int", 	  "col" => "views"),
	);
    }

?>
