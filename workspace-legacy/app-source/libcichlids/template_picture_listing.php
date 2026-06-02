<?
  require_once("libcichlids.php");

  $imgtag = cichlids_getImageTag($picture['uid'], $picture['image'], 130, 97, "black");
  $piclink = cichlids_getPictureLink($picture['uid']);
  $username = cichlids_getUsername($picture['fe_user']);
  $stars = cichlids_getRatingStars($picture['rating']);
?>
<div class="pictures_list_entry" style="width: 130px; text-align: left; overflow: hidden;">
	<div class="pictures_list_image"><a href="<?=$piclink;?>"><?=$imgtag;?></a></div>
	<div class="pictures_list_title" style="font-size: 9pt;"><a href="<?=$piclink;?>"><?=cichlids_crop($picture['title'], 50, "...");?></a></div>
	<div style="font-size: 8pt; "><?=$username;?></div>
        <div style="font-size: 8pt; "><span style="color: #999999;">Updated:</span> <?=date("Y-m-d", $picture['tstamp']); ?></div>
	<div style="font-size: 8pt; "><span style="color: #999999;">Views:</span> <?=$picture['views'];?> </div>

	<div style="font-size: 8pt; ">
	    <? if($picture['rating_count'] > 0): ?>
		<?=$stars;?>
	    <? else: ?>
		&nbsp;
	    <? endif; ?>
	</div>
</div>
