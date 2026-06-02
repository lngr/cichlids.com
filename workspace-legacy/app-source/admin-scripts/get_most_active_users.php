<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $query = "SELECT COUNT(*) anz_comments,fe_user
	FROM user_cichlids_comments WHERE hidden=0 AND deleted=0 AND note != ''
	GROUP BY fe_user ORDER BY anz_comments  DESC LIMIT 0,20";
  $res = mysql_query($query);
  if (mysql_errno())
      print mysql_error();

  while($row = mysql_fetch_assoc($res)) {
	$uid = $row['fe_user'];
	if ($uid == 0) continue;
	$n_com = $row['anz_comments'];
	// print "User $uid hat $n_pics Bilder...\n";
	print "User $uid hat $n_com Comments...\n";
	$q2 = "SELECT COUNT(*) anz_pics, fe_user,
		fe_users.uid,fe_users.name, fe_users.username,fe_users.email,
		FROM_UNIXTIME(fe_users.is_online)
		FROM user_cichlids_pictures
		LEFT JOIN fe_users ON
		user_cichlids_pictures.fe_user = fe_users.uid
		WHERE user_cichlids_pictures.fe_user = $uid
		AND user_cichlids_pictures.hidden = 0 AND user_cichlids_pictures.deleted = 0
		-- AND user_cichlids_comments.note != ''
	";
	$r2 = mysql_query($q2);
	$data = mysql_fetch_assoc($r2);
	$data['anz_comments'] = $n_com;
	print_r($data);


  }

?>
