<?
  require_once("../config.php");

  require_once("/data1/www/www-cichlids/libcichlids/lib_common.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);


  $uploadsdir = "/data3/cichlids/typo3.uploads/tx_usercichlids";
  $fileadmindir = "/data3/cichlids/typo3.fileadmin/user_pics";
  $staticpath = "/data2/cichlids/static/pics";

  $query = "SELECT image,COUNT( image ) AS NumOccurrences FROM user_cichlids_pictures
		GROUP BY image HAVING COUNT(image) >1";
  $res = mysql_query($query);
  $n = 0;
  while($imgrow = mysql_fetch_assoc($res)) {
      $image = $imgrow['image'];

      $q = "SELECT * FROM user_cichlids_pictures WHERE image='".mysql_escape_string($image)."'";
      $imgres = mysql_query($q);
      print "Image $image: " . mysql_num_rows($imgres) . " images:\n";
      while($row = mysql_fetch_assoc($imgres)) {
	  $n++;
	  $uid = $row['uid'];
	  $user = $row['fe_user'];
	  print "\tuid $uid user $user ";

	  $filename = $image;
          if ($user == 0) $user = "anonymous";
	  $file_in_uploads = "$uploadsdir/$filename";
	  $file_in_fileadmin = "$fileadmindir/$user/$filename";

	  if ($row['hidden'] == 1) {
	      mysql_query("DELETE FROM user_cichlids_pictures WHERE uid=$uid");
	      print "(hidden)\n";
	      continue;
	  }
	  if ($row['deleted'] == 1) {
	      mysql_query("DELETE FROM user_cichlids_pictures WHERE uid=$uid");
	      print "(deleted)\n";
	      continue;
	  }
	  if ($row['image'] == "") {
	      mysql_query("DELETE FROM user_cichlids_pictures WHERE uid=$uid");
	      print "(empty)\n";
	      continue;
	  }
	  print "\n";
	  if(file_exists($file_in_fileadmin)) {
	      print "\t\tExistiert als $file_in_fileadmin\n";
	  } else {
	      print "\t\tFEHLT in $file_in_fileadmin\n";
	      mysql_query("DELETE FROM user_cichlids_pictures WHERE uid=$uid");
	      continue;
	  }

	  $poof = rand();
	  $path_parts = pathinfo($image);
	  $newimage = $path_parts['filename'] . "_dup_$poof.".$path_parts['extension'];
	  $new_file_in_fileadmin = "$fileadmindir/$user/$newimage";
	  $new_file_in_uploads = "$uploadsdir/$newimage";

	  // WICHTIG!  ALS ROOT DANN AUSFUEHREN
	  //copy($file_in_fileadmin, $new_file_in_fileadmin);
	  //copy($file_in_fileadmin, $new_file_in_uploads);
	  //$renq = "UPDATE user_cichlids_pictures SET image='".mysql_escape_string($newimage)."' WHERE uid=$uid";
	  ////print $renq . "\n";
	  //mysql_query($renq);


      }
      continue;





  }

  print "Doppelt: $n Bilder\n";

?>

