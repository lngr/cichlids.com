<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $uid = $argv[1];
  if ($uid == "")
      die("Bitte UID angeben\n");
  $query = "SELECT * FROM user_cichlids_comments WHERE fe_user=$uid ORDER BY uid DESC";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();

  while($row = mysql_fetch_assoc($res)) {
      print "--------------------------------------------------------\n";
      $rat = $row['rating'];
	$ip = $row['ip'];
      print strftime('%c', $row['tstamp']) . " -- Rating: $rat\tIP: $ip\n";;
      print $row['note'] . "\n";
      if($row['type'] == 1)
	  print 'http://www.cichlids.com/pictures.html?user_cichlids_pi1[picture]='.$row['item']."\n";
      else
	  print 'http://www.cichlids.com/tank-examples.html?no_cache=1&user_cichlids_pi1[tank]='.$row['item']."\n";
  }

?>
