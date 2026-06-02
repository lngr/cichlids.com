<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $uid = $argv[1];

  if ($uid == "")
      die("Bitte ID angeben\n");

  $query = "UPDATE user_cichlids_pictures SET deleted=0, hidden=0 WHERE uid=$uid";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();


?>
