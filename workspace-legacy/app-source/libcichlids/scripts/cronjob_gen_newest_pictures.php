<?
  require_once("../libcichlids.php");
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $query = "SELECT * FROM user_cichlids_pictures WHERE hidden=0 AND deleted=0 AND tstamp > UNIX_TIMESTAMP(NOW()) - 7200
	      ORDER BY tstamp DESC";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();

  while($row = mysql_fetch_assoc($res)) {
      $uid = $row['uid'];
	echo "Converting $uid\n";
	cichlids_generatePicture($uid);
  }

?>
