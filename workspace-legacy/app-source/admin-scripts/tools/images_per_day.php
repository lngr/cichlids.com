<?
  require_once("../config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $query = "SELECT COUNT(*) c, DATE_FORMAT(FROM_UNIXTIME(tstamp), '%Y-%m') as date FROM user_cichlids_pictures WHERE hidden=0 AND deleted=0 GROUP BY date order by date desc";

  $res = mysql_query($query);
  while($row = mysql_fetch_assoc($res)) {
    $c = $row['c'];
    $d = $row['date'];
    print "$c - $d\n";
  }

?>
