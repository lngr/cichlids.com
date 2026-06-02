<?
    $gal = $this->getManagedUserGallery();
    $pics = $this->gallery_manager->getPicturesInGallery($gal);
?>
<div class="box">
    <div class="box_header">Pictures in this gallery (<?=count($pics);?>)</div>
    <div class="box_content">
    <? if(count($pics) == 0): ?>
      <div style="height: 100px; vertical-align: middle; text-align: right;">
	  <img src="/fileadmin/rechts.gif" valign="middle"><br>
	  Choose pictures from here:<br>
	  <img src="/fileadmin/rechts.gif" valign="middle">
      </div>
    <? endif; ?>
    <?	$n = 0;
	foreach($pics as $pic) { ?>
	<div style="padding-bottom: 5px; margin-bottom: 5px; border-bottom: 1px dashed #AAAAAA;">
	    <div style="float: left; margin-right: 10px; " >
		<?=$this->getPictureImage($pic, 80, 80, true); ?><br>
		    <?=$this->getAjaxButton("", "removePicture",
			array("picture" => $pic->uid, "gallery" => $gal->uid),
			'title="delete" style="border: none; height: 20px; width: 20px; background: url(/fileadmin/delete.gif);"');?>
		    <? if($n > 0): ?>
			<?=$this->getAjaxButton("", "moveUpPicture",
			    array("picture" => $pic->uid, "gallery" => $gal->uid),
			    'title="move up" style="border: none; height: 20px; width: 20px; background: url(/fileadmin/arrowup.gif); "');?>
		    <? else: ?>
			<img src="/clear.gif" width="20" height="20">
		    <? endif; ?>
		    <? if($n < count($pics) - 1): ?>
			<?=$this->getAjaxButton("", "moveDownPicture",
			    array("picture" => $pic->uid, "gallery" => $gal->uid),
			    'title="move down" style="border: none; height: 20px; width: 20px; background: url(/fileadmin/arrowdown.gif);"');?>
		    <? else: ?>
			<img src="/clear.gif" width="20" height="20">
		    <? endif; ?>
	    </div>
	    <div style="height: 100px; overflow: hidden;">
		<div>
		</div>
		<div><b><?=$pic->title; ?></b></div>
		<div style="font-size: 8pt;"><?=$pic->description;?></div>
	    </div>
	</div>
    <?	$n++;
	} ?>
    </div>
