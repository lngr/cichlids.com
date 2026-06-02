<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

	$query = "SELECT * FROM fe_users ORDER BY uid ASC";

	$res = mysql_query($query);
	$output = array();
	$count = 0;
	while($row = mysql_fetch_assoc($res)) {
		$user = new stdClass();
		$output[] = $user;

		$user->username = iconv("latin1", "utf8", $row['username']);
		$user->email = $row['email'];
		$user->email_verified = true;
		$user->name = iconv("latin1", "utf8", $row['name']);
		if ($count++ > 500) {
			$count = 0;
			print(json_encode($output));
			print "\n";
			$output = array();
		}
	}

	print(json_encode($output));
	print "\n";
?>
