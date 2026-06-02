<?
    $gal = $this->getManagedUserGallery();
    $pics = $this->gallery_manager->getPicturesInGallery($gal);
?>
<div class="box">
    <div class="box_header">Gallery: <?=$gal->title;?><span style="font-size: 8pt;"></div>
    <div class="box_content">
	<? if($gal->hidden): ?>
	    <div style="font-size: 8pt; width: 200px; float: right;">This gallery is currently hidden and not visibly by other visitors.</div>
	<? endif; ?>
	<a href="<?=$this->getActionLinkUrl("rename_gallery", array("gallery" => $gal->uid));?>">rename</a> - 
	<? if(count($pics) == 0): ?>
	    <a href="<?=$this->getActionLinkUrl("delete_gallery", array("gallery" => $gal->uid));?>">delete</a> -
	<? endif; ?>
	<? if($gal->hidden): ?>
	    <a href="<?=$this->getActionLinkUrl("unhide_gallery", array("gallery" => $gal->uid));?>">unhide</a>
	<? else: ?>
	    <a href="<?=$this->getActionLinkUrl("hide_gallery", array("gallery" => $gal->uid));?>">hide</a>
	<? endif; ?>
	<br style="clear: both;">
    </div>
</div>
