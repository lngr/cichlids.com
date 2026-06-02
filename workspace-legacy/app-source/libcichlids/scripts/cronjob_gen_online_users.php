<?

  require_once("config.php");
  require_once("../libcichlids.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  ob_start();

?>
We know <b>
<?
  $res = mysql_query("SELECT COUNT(*) as c FROM fe_users WHERE disable = 0 AND deleted = 0");
  $row = mysql_fetch_assoc($res);
  echo $row['c'];
?></b> registered cichlid fans (<b><?
  $res = mysql_query("SELECT COUNT(*) as c FROM fe_users WHERE is_online >= UNIX_TIMESTAMP(NOW()) - 3600");
  $row = mysql_fetch_assoc($res);
  echo $row['c'];
?></b> online), who have posted
<b><?
  $res = mysql_query("SELECT COUNT(*) as c FROM user_cichlids_pictures WHERE hidden=0 AND deleted = 0");
  $row = mysql_fetch_assoc($res);
  echo $row['c'];
?></b> pictures and wrote
<b><? 
  $res = mysql_query("SELECT COUNT(*) as c FROM user_cichlids_comments WHERE hidden=0 AND deleted = 0");
  $row = mysql_fetch_assoc($res);
  echo $row['c']; 
?>
</b> comments.
<?
  $out = ob_get_contents();
  ob_end_clean();
  cichlids_out2file($out, "/var/www/html/www-cichlids/cichlids.extra/static/html/online_users.html");

?>
