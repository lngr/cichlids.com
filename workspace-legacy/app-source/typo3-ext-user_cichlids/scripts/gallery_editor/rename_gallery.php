<?
    $gal = $this->getManagedUserGallery();
?>
<?=$this->getActionForm(array("gallery" => $gal->uid)); ?>
   <div class="box">
      <div class="box_header">Rename gallery</div>
      <div class="box_content">
	New title: <?=$this->getError("gallery_title"); ?>
	<input type="text" size="45" maxlen="100" name="<?=$this->getInputName("gallery_title"); ?>" value="<?=$gal->title;?>">
	<?=$this->getActionButton("Rename", "rename_gallery_button"); ?>
      </div>
  </div>


</form>
