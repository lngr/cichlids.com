<?php
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $uid = $argv[1];
  $pass = $argv[2];

  if ($uid == "")
      die("Bitte UID angeben\n");

  if ($pass == "")
      die("Bitte Passwort angeben\n");

  $query = "UPDATE fe_users SET password='$pass' WHERE uid=$uid";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();


?>
