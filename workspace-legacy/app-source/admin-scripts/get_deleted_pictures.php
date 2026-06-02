<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);
  $time = intval($argv[1]);
  if ($time == 0) $time = 1;
  $time *= 86400;
  $query = "SELECT * FROM user_cichlids_pictures WHERE (hidden = 1 OR deleted=1) AND delete_tstamp > UNIX_TIMESTAMP(NOW()) - $time";
  $res = mysql_query($query);

  while($row = mysql_fetch_assoc($res)) {
      print_r($row);
      print '<http://www.cichlids.com/pictures.html?&user_cichlids_pi1[force]=1&user_cichlids_pi1[picture]='.$row['uid'].'>' . "\n";
      print "\n\n";
  }


?>
