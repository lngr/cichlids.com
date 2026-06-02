<?
  require_once("config.php");
  require_once("../libcichlids.php");

  $conn = mysql_connect($db_host, $db_username, $db_password);
  mysql_select_db($db_db);

  $uid = intval($argv[1]);
  if ($uid == 0) {
      print "Arg missing\n";
      exit(1);
  }

  cichlids_generateComment($uid);
?>
