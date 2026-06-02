<? require_once(PATH_t3lib."class.t3lib_div.php"); ?>
<? $GLOBALS['TSFE']->set_no_cache(); ?>

<table id="cichlids_base_table" border=0 style="margin-top: 20px;">
<tr>
    <td id="cichlids_left" style="width: 160px;">
	<div style="font-size: 8pt;">
	<?  function make_minilink($obj, $title, $overwrite) {
		global $toptitle;
		$keys = array_keys($overwrite);
		if ($obj->piVars[$keys[0]] == $overwrite[$keys[0]]) {
		    $toptitle .= $title;
		    return '<b>'.$title.'</b>';
		}
		$link = $obj->pi_linkTP_keepPIvars_url($overwrite, 1, 0);
		return '<a href="'.$link.'">'.$title.'</a>';
	    }
	?>
	<h1 class="cichlids_header_links" style="font-size: 9pt;">Browse:</h1> 
	<?=make_minilink($this, "Most Recent", array("sort"=>"tstamp")); ?><br>
	<?=make_minilink($this, "Most Viewed", array("sort"=>"views")); ?><br>
	<?=make_minilink($this, "Top Rated", array("sort"=>"rating")); ?><br>
	<!--<?//=make_minilink($this, "Most Discussed", array("sort"=>"num_comments")); ?><br>-->

	<h1 class="cichlids_header_links" style="font-size: 9pt;">Type:</h1> 
	<?=make_minilink($this, "All", array("type"=>"", "page"=>"")); ?><br>
	<?=make_minilink($this, "Cichlid Pictures", array("type"=>"cichlids", "page"=>"")); ?><br>
	<?=make_minilink($this, "Tank Pictures", array("type"=>"tanks", "page"=>"")); ?><br>
	<?=make_minilink($this, "Photo Contest", array("type"=>"contest", "page"=>"")); ?><br>

	<h1 class="cichlids_header_links" style="font-size: 9pt;">Category:</h1> 
	<?=make_minilink($this, "All", array("category"=>"", "page"=>"")); ?><br>
	<?=make_minilink($this, "American", array("category"=>"3", "page"=>"")); ?><br>
	<?=make_minilink($this, "African", array("category"=>"6", "page"=>"")); ?><br>
	<?=make_minilink($this, "Lake Malawi", array("category"=>"2", "page"=>"")); ?><br>
	<?=make_minilink($this, "Lake Tanganyika", array("category"=>"1", "page"=>"")); ?><br>

	<h1 class="cichlids_header_links" style="font-size: 9pt;">View:</h1> 
	<a href="/pictures/by-species.html">by species</a><br>

	</div>	

    </td>
    <td id="cichlids_main" style="";>

    <? if(isset($this->piVars['species']) && intval($this->piVars['species'] > 0)): ?>
      <?
	$species = $this->species_manager->getSpeciesById(intval($this->piVars['species']));
      ?>
      <h2><?=$species->title; ?></h2>
    <? endif; ?>

    <?
	$pagebrowser_count = $this->get_picture_count();
	$pagebrowser_perpage = 16;
	$pagebrowser_currentpage = intval($this->piVars['page']);
	if ($pagebrowser_currentpage == 0) $pagebrowser_currentpage++;
	include("pagebrowser.php");
    ?>

    <?=$this->getActionForm(); ?>
        <?
	$perpage = 16;
    	$pics = $this->get_picture_list($perpage);
    	$wrap = 1;
    	foreach($pics as $pic) {
	    cichlids_includeStaticHtmlPicture($pic->uid, "listing");
	if($wrap++ % 4 == 0):?><br style="clear: both;"><? endif; ?>
    	<? }
    ?>
    </form>

    <?
	$pagebrowser_dopages = true;
	$pagebrowser_count = $this->get_picture_count();
	$pagebrowser_perpage = 16;
	$pagebrowser_currentpage = intval($this->piVars['page']);
	if ($pagebrowser_currentpage == 0) $pagebrowser_currentpage++;
	include("pagebrowser.php");
    ?>

    </td>
    <td style="width: 100px; padding-left: 5px; overflow: hidden;">
	<? include("/var/www/html/www-cichlids/cichlids.extra/static/html/latest_comments.html"); ?>
    </td>
</tr>
</table>


