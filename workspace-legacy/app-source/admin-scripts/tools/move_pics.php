<?
  require_once("../config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $frompath = "/data3/cichlids/typo3.uploads/tx_usercichlids/";
  $topath = "/data3/cichlids/userpics/";
  $dbpath = "user_pics/";

  $query = "SELECT * FROM user_cichlids_pictures WHERE deleted=0 ORDER BY uid ASC";
  $res = mysql_query($query);
  while($row = mysql_fetch_assoc($res)) {
      $uid = $row['uid'];
      $user = $row['fe_user'];
      $image = $row['image'];
      if ($user == 0) $user = 'anonymous';

      $dbfile = sprintf("%s%s/%s", $dbpath, $user, $image);
      $source = $frompath.$image;
      $target = $topath.$dbfile;
      $dir = dirname($target);

      # cichlids rausloeschen
      $cut = substr($image, 0, 19);
      if("cichlids/user_pics/" == $cut) {
	  $new = substr($image, 9);
	  mysql_query("UPDATE user_cichlids_pictures SET image='$new' WHERE uid=$uid");
	  continue;
      }
      $cut = substr($image, 0, 10);
      if("user_pics/" == $cut) {
	  # schon gemacht
	  continue;
      }

      $orig = $target;
      if(file_exists($target)) {
	  $i = 0;
	  do {
	      $target = sprintf("%s.copy-%d.jpg", $orig, $i++);
	  } while(file_exists($target));
      }

      if(!file_exists($source)) {
	  if($row['hidden'] == 1) continue;
	  print "ACHTUNG!  Fehlt: $source\n";
      }

      print "Bild $uid User $user:\n\t$image\n\t$source\n\t$target\n\t$dbfile\n";

      if(!is_dir($dir)) {
	  $mkdircmd = sprintf("mkdir -p %s", escapeshellarg($dir));
	  system($mkdircmd);
      }

      copy($source, $target);
      if(!file_exists($target)) {
	  print "Fehler beim kopieren von $source nach $target\n";
	  exit(1);
      }
      mysql_query("UPDATE user_cichlids_pictures SET image='$dbfile' WHERE uid=$uid");
      system("sleep 1");
  }

?>
