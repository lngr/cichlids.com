<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $url = $argv[1];

  if ($url == "")
      die("Bitte URL ohne HTML angeben\n");

  $query = "SELECT * FROM tx_realurl_uniqalias WHERE value_alias='$url'";
  $res = mysql_query($query);
  $row = mysql_fetch_assoc($res);
  $uid = $row['value_id'];


  $query = "UPDATE user_cichlids_pictures SET deleted=0, hidden=0 WHERE uid=$uid";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();


?>
