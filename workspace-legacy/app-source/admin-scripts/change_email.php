<?php
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $uid = $argv[1];
  $email = $argv[2];

  if ($uid == "")
      die("Bitte UID angeben\n");

  if ($email == "")
      die("Bitte Email angeben\n");

  $query = "UPDATE fe_users SET email='$email' WHERE uid=$uid";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();


?>
