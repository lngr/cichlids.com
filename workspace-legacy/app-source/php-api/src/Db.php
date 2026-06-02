<?php

namespace Cichlids;

$dbh = null;

class Db {

	static function get() {
		global $dbh;

		$dbhost = "176.9.120.235";
    		$dbuser = "cichlids_api";
    		$dbpass = "FL3r7fwInOQePEW3";
    		$dbname = "cichlids_typo3";

		if ($dbh == null) {
    			$dbh = new \PDO("mysql:host=$dbhost;dbname=$dbname", $dbuser, $dbpass,
				array(\PDO::MYSQL_ATTR_INIT_COMMAND => "SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci"));
    			$dbh->setAttribute(\PDO::ATTR_ERRMODE, \PDO::ERRMODE_EXCEPTION);
		}

    		return $dbh;
	}

}
