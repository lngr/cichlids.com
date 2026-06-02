#!/usr/bin/php
<?
    include("/var/www/html/www-cichlids/cichlids/typo3conf/localconf.php");
    require_once("config.php");
    require_once("../libcichlids.php");

    $link = mysql_connect($typo_db_host, $typo_db_username, $typo_db_password);
    mysql_select_db($typo_db);

    $query = "SELECT *,
		    (SELECT AVG(rating) FROM user_cichlids_comments
		    WHERE item=pictures.uid and type=1 and rating>0) rating_foo
		
		FROM user_cichlids_pictures pictures

		GROUP BY uid
		LIMIT 0,10";

    $query = "SELECT AVG(comments.rating) the_rating, COUNT(comments.rating) the_count, pictures.*
		
		FROM user_cichlids_comments comments, user_cichlids_pictures pictures

		WHERE comments.item=pictures.uid and comments.type=1 and comments.rating > 0 and comments.hidden=0 and comments.deleted=0

		GROUP BY uid
		HAVING count(comments.rating) >= 1
		-- ORDER BY count(comments.rating) DESC
		ORDER BY tstamp DESC
		";

    $res = mysql_query($query);
    if (mysql_errno()) die(mysql_error());

    while($row = mysql_fetch_assoc($res)) {
	  $rat = $row['the_rating'];
	  $num = $row['the_count'];
	  $uid = $row['uid'];

	if ($uid == 190341) {
	 	//print "$uid hat $rat bei $num vs. " . $row['rating_count'] . "\n";
	}

	  if ($row['rating_count'] == $row['the_count']) continue;

	$q = "UPDATE user_cichlids_pictures SET rating=$rat, rating_count=$num WHERE uid=$uid";
	# print $q . "\n";
	$bres = mysql_query($q);
	if (mysql_errno()) {
		print $q . "\n";
		die(mysql_error());
	}
	cichlids_generatePicture($uid);
    }

?>
