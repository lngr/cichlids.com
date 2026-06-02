<i>Note:  This feature is coming soon.  You can already create your picture galleries now, but
they won't be displayed on the homepage yet.</i>

<?=$this->getActionForm(); ?>

    <? $galleries = $this->gallery_manager->getUserGalleries($this->getCurrentFEUser()); ?>

   <div class="box">
      <div class="box_header">Listing:</div>
      <div class="box_content">

    <table width="100%" rules="rows">
    <? foreach($galleries as $gal) { ?>
	<tr>
	    <td>
		<b><a href="<?=$this->getActionLinkUrl("edit_gallery", array("gallery" => $gal->uid));?>"><?=$gal->title;?></a></b>
	    </td>
	    <td width="50">
		<a href="<?=$this->getActionLinkUrl("edit_gallery", array("gallery" => $gal->uid));?>">edit</a>
	    </td>
	    <td width="50">
		<a href="<?=$this->getActionLinkUrl("rename_gallery", array("gallery" => $gal->uid));?>">rename</a>
	    </td>
	</tr>
    <? } ?>
    </table>

      </div>
  </div>

   <div class="box">
      <div class="box_header">Create a new gallery:</div>
      <div class="box_content">
	Title of the new gallery: <?=$this->getError("gallery_title"); ?>
	<input type="text" size="50" name="<?=$this->getInputName("gallery_title"); ?>" value="<?=$this->piVars["gallery_title"];?>">
	<?=$this->getActionButton("Add", "create_gallery"); ?>
      </div>
  </div>


</form>
