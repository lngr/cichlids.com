<?
  require_once("libcichlids.php");

  $imgtag = cichlids_getImageTag($picture['uid'], $picture['image'], 167, 123, "black");
  $piclink = cichlids_getPictureLink($picture['uid']);
  $username = cichlids_getUsername($picture['fe_user']);

?>
    <div class="pictures_list_entry" style="width: 167px;">
	<div class="pictures_list_image"><a href="<?=$piclink;?>"><?=$imgtag;?></a></div>
	<div class="pictures_list_title"><a href="<?=$piclink;?>"><?=cichlids_crop($picture['title'], 17, "...");?></a></div>
	<div style="font-size: 8pt; "><?=date("M jS, Y", $picture['tstamp']); ?></div>
	<div style="font-size: 8pt; "><?=$username;?></div>
	</a>
    </div>
