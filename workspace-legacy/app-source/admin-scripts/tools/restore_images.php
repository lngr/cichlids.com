<?
  require_once("../config.php");

  require_once("/data1/www/www-cichlids/libcichlids/lib_common.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $from =   "/data3/cichlids/typo3.fileadmin/user_pics";
  $target = "/data3/cichlids/typo3.uploads/tx_usercichlids";

  $allnames = array();

  $query = "SELECT * FROM user_cichlids_pictures WHERE deleted=0 ORDER BY tstamp DESC";
  $res = mysql_query($query);

  $i = 0;
  $j = 0;
  while($row = mysql_fetch_assoc($res)) {
      $uid = $row['uid'];
      $j++;
      if ($row['image'] == "") {
	  mysql_query("UPDATE user_cichlids_pictures SET deleted=1 WHERE uid=$uid");
	  continue;
      }

      $filename = $row['image'];
      $staticpath = "/data2/cichlids/static/pics";
      $staticfile = cichlids_getImageFilename($uid, $filename, 450, 600, false);


      $user = $row['fe_user'];
      if ($user == 0) $user = "anonymous";
      $orig = "$from/$user/$filename";
      $new = "$target/$filename";

      if (!file_exists($new)) {
	  print "Fehlt: $new ";
	  if (file_exists($orig)) {
	      print " Restore: $orig\n");
	      copy($orig, $new);
	      continue;
	  }

	  $pattern = "typo3.fileadmin/user_pics/$user/$filename";
	  #$pattern = "typo3.uploads/tx_usercichlids/$filename";
	  $test = "/data3/restore/$pattern";
	  if (file_exists($test)) {
	      print " Restore: $orig\n");
	      copy($test, $new);
	      copy($test, $orig);
	      unlink($test);
	      continue;
	  }


	  $lookup = "$staticpath/$staticfile";
	  if (file_exists($lookup)) {
	      copy($lookup, $new);
	      copy($lookup, $orig);
	      continue;
	  }


	  $i++;

	  
	  //mysql_query("UPDATE user_cichlids_pictures SET deleted=1 WHERE uid=$uid");
	  //print_r($row);
      }

      continue;


      if (preg_match("/^[0-9]+____file.*/", $filename)) continue;
      if (preg_match("@^\D@i", $filename)) continue;

      $num = intval(substr($filename, 0, 2));
      if ($num <= 1) continue;

      $realfilename = substr($filename, 3);

      // wenn wir hier sind UND wir haben den gleichen uer, der die ricthige
      // datei hat, dann haben wir schon die richtige datei kopiert
      if (file_exists($orig)){
	  continue;
      }

      $i = $num - 1;
      do {
	  $newfilename = sprintf("%02d_%s", $i, $realfilename);
	  $neworig = "$from/$user/$newfilename";
	  $i--;
      } while($i > 0 && !file_exists($neworig));

      if ($i == 0 && !file_exists($neworig)) {
	  print "ACHTUNG: Kein Glueck bei $realfilename User $user statt $filename\n";
	  continue;
      }

      print "Kopiere fuer User $user $neworig --> $new\n";
      
      //copy($neworig, $new);
      //copy($neworig, $orig);

  }

  print "Bidler fehlen: $i von $j\n";

?>
