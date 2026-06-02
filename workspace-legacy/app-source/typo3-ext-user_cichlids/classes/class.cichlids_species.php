<? require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_basic_object.php'); ?>
<?
    class cichlids_species extends cichlids_basic_object {
	function cichlids_species($row = array()) {
	    return parent::cichlids_basic_object($row);
	}
	var $db2fields = array(
	    "uid"	    => "uid",
	    "pid"   	    => "pid",
	    "tstamp"	    => "tstamp",
	    "crdate"	    => "crdate",
	    "deleted"	    => "deleted",
	    "hidden"	    => "hidden",
	    "genus"	    => "genus",
	    "species"	    => "species",
	    "title"	    => "title",
	    "category"	    => "category",
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

	    "genus"	    => array("type" => "int",	  "col" => "genus"),
	    "species"	    => array("type" => "int",	  "col" => "species"),
	    "category"	    => array("type" => "int",	  "col" => "category"),
	);
    }

?>
