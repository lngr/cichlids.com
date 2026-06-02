<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $target = "/smb/web/cichlids/typo3.uploads/tx_usercichlids";
  $from = "/smb/web/cichlids/typo3.fileadmin/user_pics";

  $allnames = array();

  $query = "SELECT * FROM user_cichlids_pictures WHERE hidden=0 AND deleted=0";
  $res = mysql_query($query);
  while($row = mysql_fetch_assoc($res)) {
      if ($row['image'] == "")
	  continue;
      $filename = $row['image'];

      if ($row['uid'] == 115 || $row['uid'] == 81)
	  print_r($row);
      // $filename ist utnerhalb von uploads/tx_usercichlids

      $tolower = strtolower($filename);
      $user = $row['fe_user'];
      if ($user == 0) $user = "anonymous";
      $orig = "$from/$user/$filename";

      if (isset($allnames[$tolower])) {
	  if (ereg("^albums/", $tolower)) {
	      // Hier ist nichts meh rzu reparieren, biede l&ouml;schen
	      $a = $row['uid'];
	      $b = $allnames[$tolower];

	      $query = "UPDATE user_cichlids_pictures SET deleted=1 WHERE uid IN ($a,$b) LIMIT 2";
	      mysql_query($query);
	      continue;
	  }
	  print "doppelt! $tolower ... ";
	  if (file_exists($orig))  {
	      $len = strlen($tolower);
	      $basename = substr($tolower, 0, $len - 4);
	      $new = $basename . "_" . md5(time()) . substr($tolower, $len - 4);
	      // Original moven


	      copy($orig, "$target/$new");
	      rename($orig, "$from/$user/$new");
	      $query = "UPDATE user_cichlids_pictures SET image='$new' WHERE uid=".$row['uid'];
	      mysql_query($query);
	      sleep(1);
	      print " --> $new\n";
	      continue;
	  } else {
	      print "Original existiert nicht! $orig\n";
	  }
      } else
	  $allnames[$tolower] = $row['uid'];

      // hier jetzt Original-Datie wiederherstellen
      if (file_exists($orig)) {
	  // echo "Kopiere Original $orig ---> $target/$filename\n";
	  // copy($orig, "$target/$filename");
      }


  }

?>
