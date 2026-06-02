<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);



  $uid = $argv[1];
  if ($uid == "")
      die("Bitte UID angeben\n");

  
  $t = time();
  $r = "delete_pics_from_user.php";
  $u = 1;

  $query = "UPDATE user_cichlids_pictures SET deleted=1, delete_tstamp=$t, delete_reason='$r', delete_user=$u
	    WHERE fe_user=$uid";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();


?>
