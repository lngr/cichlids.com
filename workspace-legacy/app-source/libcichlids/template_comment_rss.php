<?
  require_once("libcichlids.php");

  $username = cichlids_getCommentPosterName($comment);
  $commentlink = cichlids_getCommentLink($comment);

  $picture = cichlids_getCommentPicture($comment);
  $imgtag = cichlids_getImageTag($picture['uid'], $picture['image'], 100, 75, "black");
  $foo =  cichlids_getImageFilename($picture['uid'], $picture['image'], 100, 75, "black");
?>
<item>
  <title><?=cichlids_crop($comment['note'], 75, " ");?></title>
  <link>http://www.cichlids.com<?=$commentlink;?>#comment-<?=$comment['uid'];?></link>
  <description><![CDATA[
  <img src="http://www.cichlids.com/p/<?=$foo;?>"><br>
  <b><?=htmlspecialchars($username);?>: </b><br>
  <?=htmlspecialchars($comment['note']);?>
  ]]></description>
  <pubdate><?=strftime("%Y-%m-%d %H:%M", $comment['tstamp']); ?></pubdate>
</item>
