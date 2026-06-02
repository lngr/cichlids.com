<?
require_once("libcichlids.php");

function cichlids_createHtmlForComment($comment, $tmplname) {
    $template = "template_comment_$tmplname.php";
    ob_start();
    include($template);
    $out = ob_get_contents();
    ob_end_clean();

    $outfile = cichlids_getStaticHtmlComment($comment['uid'], $tmplname);
    cichlids_out2file($out, $outfile);
}

function cichlids_generateComment($uid) {
  $query = "SELECT * FROM user_cichlids_comments WHERE hidden=0 AND deleted=0 AND uid=$uid";
  $res = mysql_query($query);
  if (mysql_errno())
      die(mysql_error());
  if (mysql_num_rows($res) == 0) {
      print "Comment $uid nonexistent. \n";
      exit(1);
  }
  $row = mysql_fetch_assoc($res);
  cichlids_createHtmlForComment($row, "latest");
  cichlids_createHtmlForComment($row, "latest_wide");
  cichlids_createHtmlForComment($row, "rss");
}

?>
