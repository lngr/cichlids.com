<?php
  namespace Cichlids;

  use Aws\Sns\SnsClient;
  use Aws\Credentials\Credentials;

class Sns {
	static function get() {
  		$credentials = new Credentials('REDACTED_AWS_ACCESS_KEY_ID', 'REDACTED_AWS_SECRET_ACCESS_KEY');

  		$sns = SnsClient::factory(array(
    			'credentials' => $credentials,
    			'region'  => 'us-east-1',
    			'version' => '2010-03-31',
  		));

    		return $sns;
	}

	static function sync_user_login($uid, $subject) {
  		$db = \Cichlids\Db::get();
  		$sns = \Cichlids\Sns::get();

		$dto = array(
			"uid" => $uid,
			"subject" => $subject,
		);

		$msg = array(
          		'TopicArn' => 'arn:aws:sns:us-east-1:894559175927:cichlids_user_logins',
          		'Message' => json_encode($dto, JSON_PRETTY_PRINT),
  		);

  		$sns->publish($msg);

        	$sql = "UPDATE fe_users_auth0 SET dispatched_sqs=1 WHERE user_id=:uid";
		$stmt = $db->prepare($sql);
		$stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
		$stmt->execute();
  	}
}
?>
