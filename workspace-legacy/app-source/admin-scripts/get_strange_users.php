<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  #$query = "SELECT uid,username,name,email,city,static_info_country,lastlogin,disable FROM fe_users WHERE deleted=0 AND username=first_name AND uid>9000 ORDER BY uid DESC LIMIT 0,200";
  $query = "SELECT uid,username,name,email,city,static_info_country,lastlogin,disable FROM fe_users WHERE deleted=0 AND uid>9000 ORDER BY uid DESC LIMIT 0,200";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();

  while($row = mysql_fetch_assoc($res)) {
      $foo = join("\t", $row);
      print $foo . "\n";
  }

?>
