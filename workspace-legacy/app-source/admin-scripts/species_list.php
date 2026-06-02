<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

            $query = "SELECT * FROM user_cichlids_species WHERE deleted=0 AND hidden=0 ORDER BY title ASC";
            $res = mysql_query($query);
            if (mysql_errno())
                print mysql_error();
            while ($row = mysql_fetch_assoc($res)) {
		print $row['title'] . "\n";
            }


?>
