<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $uid = $argv[1];
  if(intval($uid) == 0)
      die("Bitte URL ohne HTML angeben\n");



  $query = "UPDATE user_cichlids_pictures SET deleted=1 WHERE uid=$uid";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();


  $query = "SELECT * FROM user_cichlids_pictures WHERE uid=$uid";
  $res = mysql_query($query);
  $row = mysql_fetch_assoc($res);
  print_r($row);


?>
