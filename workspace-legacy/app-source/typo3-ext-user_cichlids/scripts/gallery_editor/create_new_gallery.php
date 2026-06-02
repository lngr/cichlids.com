<h1>Create new gallery/h1>
<?=$this->getActionForm(); ?>

    <div class="box">
	<div class="box_header">Title of the new gallery</div>
	<div class="box_content">
	    <input type="text" size="100" name="<?=$this->getInputName("gallery_title"); ?>" value="<?=$this->piVars["gallery_title"];?>">
	</div>

	</tr>
    <? } ?>
    </table>

    <a href="<?=$this->getActionLinkUrl("create_new_gallery");?>">Add a gallery</a>

</form>
