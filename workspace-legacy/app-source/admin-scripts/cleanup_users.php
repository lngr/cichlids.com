<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

	$query = "SELECT email,COUNT(uid) anzahl FROM fe_users
			WHERE deleted=0 AND disable=0 AND email != '' 
			GROUP BY email
			HAVING anzahl > 1 
			ORDER BY anzahl ASC
			";
	$res = mysql_query($query);
	while($row = mysql_fetch_assoc($res)) {
		print "----------------------------------------------------\n";
		print $row['anzahl'] . "\t" . $row['email'] . "\n";

		$query = "SELECT users.*, COUNT(pics.uid) as anzahl from fe_users users
			left join user_cichlids_pictures pics ON users.uid = pics.fe_user
			where users.email='".$row['email']."' and users.disable=0 and users.deleted=0
			GROUP BY users.uid
			order by anzahl desc
			";
		$first = null;
		$userres = mysql_query($query);
		if (mysql_errno()) print mysql_error();
		while($user = mysql_fetch_assoc($userres)) {
			printf("\t%-5s\t %-5s\t %s %s\n", $user['anzahl'], $user['uid'], $user['username'], $user['name']);
			if ($first == null) {
				$first = $user;
			} else {
				$cmd = "php merge_users.php ${user['uid']} ${first['uid']}";
				exec($cmd);
				$upd = "UPDATE fe_users SET deleted=1 WHERE uid=${user['uid']}";
				mysql_query($upd);
				if (mysql_errno()) print mysql_error();
			}
		}

	}
?>
