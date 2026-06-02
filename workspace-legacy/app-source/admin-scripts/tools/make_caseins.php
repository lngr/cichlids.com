<?
  require_once("/home/alex/scripts/cichlids/config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $from =   "/data3/cichlids/typo3.fileadmin/user_pics";
  $target = "/data3/cichlids/typo3.uploads/tx_usercichlids";

  $filename = $argv[1];

  $query = "SELECT * FROM user_cichlids_pictures WHERE  image like '$filename'";
  $res = mysql_query($query);
  while($row = mysql_fetch_assoc($res)) {
      $uid = $row['uid'];
      $filename = $row['image'];
      $user = $row['fe_user'];
      if ($user == 0) $user = "anonymous";

      $orig = "$from/$user/$filename";
      $new = "$target/$filename";

      $format = "%d____file_%02d_%s";
      $dbfile = sprintf($format, $user, 9999, $filename);

      if (file_exists($orig)) {
	  $origloc = "$from/$user/$dbfile";
	  $newloc = "$target/$dbfile";

	  print "Save $uid: $orig\n";
	  copy($orig, $origloc);
	  copy($orig, $newloc);
	  $q  = "UPDATE user_cichlids_pictures SET image='$dbfile' where uid=$uid";
	  print "$q\n";
	  mysql_query($q);
	  if (mysql_errno()) die(mysql_error());
	  unlink($orig);
	  continue;
      }

      continue;


      if (preg_match("/^[0-9]+____file.*/", $filename)) continue;
      if (preg_match("@^\D@i", $filename)) continue;


      $num = intval(substr($filename, 0, 2));

      if ($num < 1) continue;

      # hier gucken, ob eine datei gleichen namens dort existiert.
      # XXX wichtig: mysql query datei dann auch umbenennen in beiden faellen.
      # wenn ja --> kopieren und ende (naechstes bild).
      # wenn nein, muessen wir raten.  

      if ($num > 1)
	print "YEAH: $uid --> $filename\n";

      # erstmal gucken, ob ueberhautp welche mit 02 oder so da sind. wenn nicht, amcht
      # das das leben deutlich einfacher.
      continue;
      exit(1);

      $realfilename = substr($filename, 3);


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

?>
