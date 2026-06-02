<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $uid = $argv[1];
  if ($uid == "")
      die("Bitte UID angeben\n");
  $query = "UPDATE user_cichlids_comments SET deleted=1 WHERE fe_user=$uid";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();


?>
