<?
  require_once("../../config.php");

  require_once("/data1/www/www-cichlids/libcichlids/lib_common.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);


  $uploadsdir = "/data3/cichlids/typo3.uploads/tx_usercichlids";
  $fileadmindir = "/data3/cichlids/typo3.fileadmin/user_pics";
  $staticpath = "/data2/cichlids/static/pics";

  $query = "SELECT * FROM user_cichlids_pictures WHERE deleted=0 ORDER BY tstamp DESC";
  $res = mysql_query($query);

  while($row = mysql_fetch_assoc($res)) {
      $uid = $row['uid'];
      $filename = $row['image'];


      $user = $row['fe_user'];
      if ($user == 0) $user = "anonymous";

      $file_in_uploads = "$uploadsdir/$filename";
      $file_in_fileadmin = "$fileadmindir/$user/$filename";

      if (!file_exists($file_in_uploads)) {

	  if (file_exists($file_in_fileadmin)) {
	      print "Restore: $file_in_uploads from $file_in_fileadmin\n";
	      copy($file_in_fileadmin, $file_in_uploads);
	      continue;
	  }


	  $staticfile = cichlids_getImageFilename($uid, $filename, 450, 600, false);

	  $lookup = "$staticpath/$staticfile";
	  if (file_exists($lookup)) {
	      if (!is_dir(dirname($file_in_fileadmin)))
		  mkdir(dirname($file_in_fileadmin));
	      print "Restore: $file_in_uploads from $lookup\n";
	      print "Restore: $file_in_fileadmin from $lookup\n";
	      copy($lookup, $file_in_uploads);
	      copy($lookup, $file_in_fileadmin);
	      continue;
	  }

	  print "Fehlt: $uid (User $user): $file_in_uploads\n";
	  continue;
	  //mysql_query("UPDATE user_cichlids_pictures SET deleted=1 WHERE uid=$uid");

      }
  }

?>
