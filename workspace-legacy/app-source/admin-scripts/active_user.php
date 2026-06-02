<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $uid = $argv[1];
  if ($uid == "")
      die("Bitte UID angeben\n");
  $query = "UPDATE fe_users SET usergroup='1',deleted=0,disable=0 WHERE uid=$uid";
  //$query = "UPDATE fe_users SET deleted=0,disable=0 WHERE uid=$uid";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();


?>
