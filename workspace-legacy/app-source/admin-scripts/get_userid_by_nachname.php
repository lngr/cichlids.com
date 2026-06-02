<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $name = $argv[1];
  if ($name == "")
      die("Bitte Nachnamen angeben\n");
  $query = "SELECT * FROM fe_users WHERE last_name LIKE '%$name%'";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();

  while($row = mysql_fetch_assoc($res)) {
      print_r($row);
  }

?>
