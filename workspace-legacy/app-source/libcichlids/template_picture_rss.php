<?
  require_once("libcichlids.php");

  //$imgtag = cichlids_getImageTag($picture['uid'], $picture['image'], 167, 123, "black");
  $foo =  cichlids_getImageFilename($picture['uid'], $picture['image'], 167, 123, "black");
  $piclink = cichlids_getPictureLink($picture['uid']);
  $username = cichlids_getUsername($picture['fe_user']);

?>
<item>
  <title><?=htmlentities(cichlids_crop($picture['title'], 150, "..."));?></title>
  <link>http://www.cichlids.com<?=$piclink;?></link>
  <description><![CDATA[
  <a href="http://www.cichlids.com<?=$piclink;?>"><img src="http://www.cichlids.com/p/<?=$foo;?>"></a> <br>
  posted by <?=htmlspecialchars($username);?> <br>at <?=strftime("%Y-%m-%d %H:%M", $picture['tstamp']); ?>.<br><br>
  <?=htmlspecialchars($picture['description']); ?>
  ]]></description>
  <pubdate><?=strftime("%Y-%m-%d %H:%M", $picture['tstamp']); ?></pubdate>
</item>
