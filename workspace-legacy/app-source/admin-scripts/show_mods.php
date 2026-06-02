<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $query = "SELECT * FROM fe_users WHERE usergroup LIKE '%3%'";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();

  while($row = mysql_fetch_assoc($res)) {
      print_r($row);
  }

?>
