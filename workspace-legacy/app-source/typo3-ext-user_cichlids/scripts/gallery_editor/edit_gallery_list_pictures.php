<div id="gallery_editor_user_pictures" class="box" style="width: 120px; margin-left: 5px;">
<div class="box_header">All pictures:</div>
<div class="box_content" style="
    overflow: scroll;
    text-align: center;
    scroll: auto;
    height: 600px;
    "
>
<?
    $user = $this->getCurrentFEUser();
    $gal = $this->getManagedUserGallery();
    $pics = $this->picture_manager->findByUser($user);

?>
    <? foreach($pics as $pic) { ?>
	<?=$this->getPictureImage($pic, 80, 80, true); ?>
	<div style="font-size: 7pt; "><?=$pic->title; ?></div>
	<?=$this->getAjaxButton("&lt; add", "addPicture", array("picture" => $pic->uid, "gallery" => $gal->uid), 'style="border: 1px solid #AAAAAA; height: 20px; width: 50px;""');?>
	<br><br>
    <? } ?>
</div>
</div>
