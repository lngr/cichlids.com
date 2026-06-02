<?php
  namespace Cichlids;

  require './vendor/autoload.php';

  use Aws\Sns\SnsClient;
  use Aws\Credentials\Credentials;

  use \Cichlids\Db;
  use \Cichlids\Users;
  use \Cichlids\Pictures;

  $credentials = new Credentials('REDACTED_AWS_ACCESS_KEY_ID', 'REDACTED_AWS_SECRET_ACCESS_KEY');

  $sns = SnsClient::factory(array(
    'credentials' => $credentials,
    'region'  => 'us-east-1',
    'version' => '2010-03-31',
  ));


  $db = \Cichlids\Db::get();

  $sql = "SELECT * FROM user_cichlids_comments WHERE 
		hidden = 0 AND
		deleted = 0 AND
		dispatched_sqs = 0 AND
		tstamp > UNIX_TIMESTAMP('2017-06-30')
		ORDER BY uid ASC
	";
  $stmt = $db->prepare($sql);
  $stmt->execute();

  $comments = $stmt->fetchAll(\PDO::FETCH_OBJ);

  $res = array_map(__NAMESPACE__.'\Comments::convertInternal', $comments);

  $sent = array();

  foreach($res as $row) {
	$msg = array(
          'TopicArn' => 'arn:aws:sns:us-east-1:894559175927:cichlids_comment_posted',
          'Message' => json_encode($row, JSON_PRETTY_PRINT),
  	);

	$ignore = false;
	if (isset($row['parentPicture']) && $row['parentPicture'] != null) {
		$pic = $row['parentPicture']['id'];
		if (in_array($pic, $sent)) {
			$ignore = true;
		}
		$sent[] = $pic;
	}

	//print $msg['Message'] . "\n";

	if ($ignore) {
		print "Ignore wg. dup $pic ... ";
	} else {
		print "Sending " . $row['id'] . " ... ";
  		$sns->publish($msg);
	}

	print "mark dispatched... ";
        $sql = "UPDATE user_cichlids_comments SET dispatched_sqs=1 WHERE uid=:uid";
	$stmt = $db->prepare($sql);
	$stmt->bindParam("uid", $row['id'], \PDO::PARAM_INT);
	$stmt->execute();

	print " .. done.\n";
  }
?>
