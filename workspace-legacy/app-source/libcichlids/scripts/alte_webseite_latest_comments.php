<?
  require_once("config.php");
  require_once("../libcichlids.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);
      if (mysql_errno())
	die(mysql_error());


  ob_start();

  function output($num) {
      $query = "SELECT * FROM user_cichlids_comments WHERE note!='' AND hidden=0 AND deleted=0 ORDER BY tstamp DESC LIMIT 0,$num";
      $res = mysql_query($query);
      if (mysql_errno())
	die(mysql_error());

      while($row = mysql_fetch_assoc($res)) {
	print_r($row);
          $uid = $row['uid'];
          cichlids_includeStaticHtmlComment($uid, "latest");
      }
  }

?>
<b>Latest comments:</b>
<div style="border: 1px #AAAAAA;">
<? output(7); ?>
</div>
<?

  $out = ob_get_contents();
  ob_end_clean();
  cichlids_out2file($out, "/var/www/html/www-cichlids/cichlids.extra/static/html/latest_comments.html");

  ob_start();
  output(400);
  $out = ob_get_contents();
  ob_end_clean();
  cichlids_out2file($out, "/var/www/html/www-cichlids/cichlids.extra/static/html/latest200_comments.html");

?>
