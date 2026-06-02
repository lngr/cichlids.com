<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $uid = $argv[1];
  if ($uid == "")
      die("Bitte UID angeben\n");

  $query = "
      SELECT * FROM (
	  SELECT COUNT(*) anzahl,item,note,uid FROM (
	      SELECT * FROM user_cichlids_comments WHERE deleted=0 AND hidden=0
		  AND fe_user=$uid AND LENGTH(note) > 0
	  ) subq1
	  GROUP BY note,item,type
      ) subq2
      WHERE anzahl > 1
      ";

  do {
      $found = false;
      $res = mysql_query($query);
      if (mysql_errno())
	  die(mysql_error());
      while ($row = mysql_fetch_assoc($res)) {
	  $found = true;
	  $id = $row['uid'];
	  $item = $row['item'];
	  print "Found .. deleting $id for item $item\n";
	  mysql_query("UPDATE user_cichlids_comments
	      SET hidden=1,
                  delete_tstamp=UNIX_TIMESTAMP(NOW()),
		  delete_reason='Duplicate comments',
		  delete_user=23 WHERE uid=$id");
      }
  } while ($found);


?>
