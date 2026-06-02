<?
  require_once("libcichlids.php");

  $imgtag = cichlids_getImageTag($picture['uid'], $picture['image'], 130, 97, "black");
  $piclink = cichlids_getPictureLink($picture['uid']);
  $username = cichlids_getUsername($picture['fe_user']);
  $stars = cichlids_getRatingStars($picture['rating']);
?>
<table border="0" style="margin-bottom: 5px; padding-bottom: 5px; border-bottom: 1px dashed #AAAAAA; width: 100%;">
<tr>
    <td valign="top" style="width: 100px;"><a href="<?=$piclink;?>"><?=$imgtag;?></a></td>
    <td class="smallText"><b><a href="<?=$piclink;?>"><?=cichlids_crop($picture['title'], 75, "...");?></a></b>
	<br><br>
	<div style="font-size: 8pt; ">
	    <? if($picture['rating_count'] > 0): ?>
		<?=$stars;?>
	    <? else: ?>
		&nbsp;
	    <? endif; ?>
	</div>
	Views: <?=$picture['views'];?>
    </td>
</tr>
</table>
