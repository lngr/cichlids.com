<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $query = "SELECT * FROM user_cichlids_comments WHERE note like '%http://%' and fe_user=0";
  $query = "SELECT * FROM user_cichlids_comments WHERE note like '%http://%http://%'"; //  and fe_user=0";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();
  $nums = mysql_num_rows($res);
  print "$nums rows\n";

  while($row = mysql_fetch_assoc($res)) {
	print date("Y-m-d", $row['tstamp']) . ": ";
      print $row['note'] . "\n";
	$id = $row['uid'];
  	//$q = "DELETE FROM user_cichlids_comments WHERE uid=$id";
	//mysql_query($q);

	$item = $id;
	$type = $row['type'];
      if ($type == 1)
          print '<http://www.cichlids.com/pictures.html?&user_cichlids_pi1[force]=1&user_cichlids_pi1[picture]='.$item.'>' . "\n";
      else
          print '<http://www.cichlids.com/tank-examples.html?&user_cichlids_pi1[force]=1&user_cichlids_pi1[tank]='.$item.'>' . "\n";

	print "---------------------------------------------------------------------------------\n";
  }

?>
