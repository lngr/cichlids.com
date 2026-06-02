<?
    $gal = $this->getManagedUserGallery();
    $pics = $this->gallery_manager->getPicturesInGallery($gal);
?>
<?=$this->getActionForm(array("gallery" => $gal->uid)); ?>
    <? if(count($pics) > 0): ?>
	<div class="box">
	    <div class="box_header">Delete gallery</div>
	    <div class="box_content">
		Sorry, the gallery must be empty before it can be deleted.
		<?=$this->getActionButton("Back", "delete_gallery_cancel"); ?>
	    </div>
	</div>
    <? else: ?>
	<div class="box">
   	   <div class="box_header">Delete gallery?</div>
   	   <div class="box_content">
   	     Are you sure you want to delete gallery "<?=$gal->title;?>"?
   	     <?=$this->getActionButton("Yes", "delete_gallery_button"); ?>
   	     <?=$this->getActionButton("Cancel", "delete_gallery_cancel"); ?>
   	   </div>
	</div>
    <? endif; ?>


</form>
