<?
  require_once("libcichlids.php");

  $username = cichlids_getCommentPosterName($comment);
  $commentlink = cichlids_getCommentLink($comment);

  $picture = cichlids_getCommentPicture($comment);
  $imgtag = cichlids_getImageTag($picture['uid'], $picture['image'], 100, 75, "black");
?>
<div class="latest_comments_entry"
      style="width: 300px; margin-bottom: 5px; margin-top: 5px; padding-bottom: 5px; border-bottom: 1px dashed #AAAAAA;">
      
      
      <div style="float: left; margin-right: 5px; "><a href="<?=$commentlink;?>"><?=$imgtag;?></a></div>
      <div style="font-size: 8pt; height: 75px; overflow: hidden;">
	  <b><?=$username;?></b>, <?=strftime("%Y-%m-%d %H:%M", $comment['tstamp']); ?><br><br>

	  <?=cichlids_crop($comment['note'], 75, " ");?>
      </div>
</div>
