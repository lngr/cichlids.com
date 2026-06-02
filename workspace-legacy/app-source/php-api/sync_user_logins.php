<?php
  namespace Cichlids;

  require './vendor/autoload.php';

  use \Cichlids\Db;
  use \Cichlids\Sns;

  $db = \Cichlids\Db::get();
  $sns = \Cichlids\Sns::get();

  $sql = "SELECT * FROM fe_users_auth0 WHERE dispatched_sqs = 0";
  $stmt = $db->prepare($sql);
  $stmt->execute();

  $map = $stmt->fetchAll(\PDO::FETCH_OBJ);

  foreach($map as $row) {
	\Cichlids\Sns::sync_user_login($row->user_id, $row->sub);
  }
?>
