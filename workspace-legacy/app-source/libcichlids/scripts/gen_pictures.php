<?
  require_once("config.php");
  require_once("lib_pictures.php");

  $conn = mysql_connect($db_host, $db_username, $db_password);
  mysql_select_db($db_db);

  $query = "SELECT * FROM user_cichlids_pictures WHERE hidden=0 AND deleted=0 ORDER BY tstamp DESC";
  $res = mysql_query($query);

  while($row = mysql_fetch_assoc($res)) {
      $uid = $row['uid'];
      $gen_pic = $GLOBALS['gen_picture_path'];
      system("/usr/bin/php $gen_pic $uid");
  }

?>
