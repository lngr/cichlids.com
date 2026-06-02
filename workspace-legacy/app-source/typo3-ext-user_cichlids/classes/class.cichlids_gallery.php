<?

    class cichlids_gallery {
	var $db2fields = array(
	    "uid"	=> "uid",
	    "pid"   	=> "pid",
	    "tstamp"	=> "tstamp",
	    "crdate"	=> "crdate",
	    "deleted"	=> "deleted",
	    "hidden"	=> "hidden",
	    "title"	=> "title",
	    "fe_user"	=> "fe_user",
	    "num_pictures" => "num_pictures",
	    "views"	=> "views",
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
	    "num_pictures"  => array("type" => "int", 	  "col" => "num_pictures"),
	    "views"	    => array("type" => "int", 	  "col" => "views"),
	);
    }

?>
