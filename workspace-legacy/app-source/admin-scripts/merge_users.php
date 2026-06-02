<?
  require_once("config.php");

das funktioniert so nich tmehr, es muss auch auth0 ding gemacht werden

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);

  $fuid = $argv[1];
  $tuid = $argv[2];
  if ($fuid == "" || $tuid == "")
      die("Bitte UIDs angeben\n");


  function doquery($q) {
        print $q . "\n";
  	$res = mysql_query($q);
  	if (mysql_errno())
      		die(mysql_error() . "\n");
  }

  $tables = array(
 	"user_cichlids_comments",
 	"user_cichlids_comments_rated",
 	"user_cichlids_gallery ",
 	"user_cichlids_pictures",
 	"user_cichlids_tanks",
  );
  foreach ($tables as $t)
  	doquery("UPDATE $t SET fe_user=$tuid WHERE fe_user=$fuid");

?>
