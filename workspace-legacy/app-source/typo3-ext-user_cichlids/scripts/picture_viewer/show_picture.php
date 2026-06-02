<? $GLOBALS['TSFE']->set_no_cache(); ?>
<div id="google">
<script type="text/javascript"><!--
google_ad_client = "pub-2393694403529430";
google_ad_width = 728;
google_ad_height = 90;
google_ad_format = "728x90_as";
google_ad_type = "text_image";
google_ad_channel = "";
google_color_border = "DDDDDD";
google_color_bg = "FFFFFF";
google_color_link = "000000";
google_color_text = "000000";
google_color_url = "0000FF";
//-->
</script>
<script type="text/javascript"
  src="http://pagead2.googlesyndication.com/pagead/show_ads.js">
</script>
</div>


<?=$this->getActionForm(); ?>
<?
    $pic = $this->get_picture(); 
    $GLOBALS['TSFE']->page['title'] = $pic->title;
    $GLOBALS['TSFE']->indexedDocTitle = $pic->title;

?>
<h1 style="font-size: 12pt; text-align: left; margin-top: 20px; margin-bottom: 5px;"><?=$pic->title;?></h1>
<table id="cichlids_base_table" border=0 style="">
<tr>
    <td style="width: 450px; padding-right: 10px;";>

    <? cichlids_includeStaticHtmlPicture($pic->uid, "single_big_part1", true, true); ?>

	Comments
	    hier alle comments

Would you like to comment?
Join cichlids.com for a free account, or Login if you are already a member.
   </td>
   <td id="cichlids_right" style="width: 280px; padding-right: 10px;">
	<?
	    /* User, Views, BBCode, usw. */
	    cichlids_includeStaticHtmlPicture($pic->uid, "single_big_part2", true, true);
	?>
	<?
	    function createRelatedTab(&$tabs, $title, $content) {
		$tabs[] = array("title" => $title, "content" => $content);
	    }

	    $tabs = array();
	    $rel_user = $this->templating("show_picture_rel_user.php");
	    // $rel_related = $this->templating("show_picture_rel_related.php"); 
	    $rel_species = $this->templating("show_picture_rel_species.php"); 
	    $rel_category = $this->templating("show_picture_rel_category.php"); 
	    if ($rel_user != "") createRelatedTab($tabs, "User", $rel_user);
	    if ($rel_species != "") createRelatedTab($tabs, "Species", $rel_species);
	    if ($rel_related != "") createRelatedTab($tabs, "Related", $rel_related);
	    if ($rel_category != "") createRelatedTab($tabs, "Category", $rel_category);

	    if (count($tabs) > 0) {
	?>
	<script language="JavaScript"><!--

	    function showTab(num) {
		content = document.getElementById("cichlids-related-content");
		tab = document.getElementById("cichlids-related-tab-" + num);
		content.innerHTML = tab.innerHTML;
		for (var n = 0; n < <?=count($tabs); ?>; n++) {
		    header = document.getElementById("cichlids-related-header-" + n);
		    header.className = "cichlids-related-header-norm";
		      
		}
		header = document.getElementById("cichlids-related-header-" + num);
		header.className = "cichlids-related-header-act";
	    }

	--></script>

	<div class="box">
	<div class="box_header">
	<div style="font-weight: bold; font-size: 10pt;">More from this...</div>
	</div>
	<div class="box_subheader" style="padding-top: 10px; padding-bottom: 0px;">
	<?

	    // title printen
	    foreach(array_keys($tabs) as $key) {
		  $tab = $tabs[$key];
		  $classname = ($key == 0 ? "act" : "norm");
		  ?>
		  <a id="cichlids-related-header-<?=$key;?>" onFocus="this.blur();" class="cichlids-related-header-<?=$classname;?>" href="javascript:showTab(<?=$key;?>);"><?=$tab['title'];?></a>
		
	    <? }

	?>
	</div><!-- box_header -->
	<div id="cichlids-related-content" class="box_content" style="height: 600px; overflow: scroll;"><?=$tabs[0]['content'];?></div>
	</div><!-- box -->
	<?

	    foreach(array_keys($tabs) as $key) {
		  $tab = $tabs[$key];
		  ?>
		  <div id="cichlids-related-tab-<?=$key;?>" style="display: none;"><?=$tab['content'];?></div>
	    <? }
	?>
	<? } // count($tabs) > 0 ?>
    </td>
    <td>
        <div>
            <div class="box" style="margin-top: 0px;">
                <div class="box_header">Latest comments:</div>
                <div class="box_content">
                <? $comments = $this->getLatestComments(6); foreach($comments as $comment) {
		    cichlids_includeStaticHtmlComment($comment->uid, "latest");
                }
		?>
                </div>
            </div>
        </div>



    </td>
</tr>
</table>

    </form>

