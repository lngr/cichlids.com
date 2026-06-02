<?
  require_once("../config.php");

  $topath = "/data3/cichlids/userpics/";

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $query = "SELECT * FROM user_cichlids_pictures WHERE deleted=0 ORDER BY uid ASC";
  $res = mysql_query($query);
  while($row = mysql_fetch_assoc($res)) {
      $uid = $row['uid'];
      $user = $row['fe_user'];
      $image = $row['image'];
      if ($user == 0) $user = 'anonymous';

      if(!file_exists($topath.$image)) {
	print "Fehlt: Bild $uid User $user: $image\n";
      }
  }

?>

