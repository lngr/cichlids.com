<?
    require_once("/var/www/html/www-cichlids/libcichlids/libcichlids.php");
?>

<table id="cichlids_base_table">
<tr>
    <td id="cichlids_main">
	<!--
	<div id="cichlids_toprated_pictures">
	    <? if(false): 
		$pics = $this->get_toprated_pictures(5);
		$wrap = 1;
		foreach($pics as $pic) { ?>
		    <div class="pictures_list_entry">
			<a href="<?=$this->getPictureLink($pic);?>">
			<div class="pictures_list_image"><?=$this->getPictureImage($pic, 80, 60, "black");?></div>
			<div class="pictures_list_title" style="font-weight: normal"><?=$this->crop($pic->title, 10, "...");?></div>
			<div style="font-size: 8pt; "><? $user=$this->getFeUserById($pic->fe_user); print $user->name;?></div>
			<div><?=$this->showRatingStars($pic->rating);?></div>

			</a>
		    </div>
		<? }
		endif;
	    ?>
	</div>
	<br style="clear: both;">
	-->

	<h1 class="cichlids_header_home">Latest pictures</h1>
	<div id="cichlids_latest_pictures">

	    <?
		$pics = $this->get_latest_pictures(15);
		$wrap = 1;
		foreach($pics as $pic) { ?>
		    <? cichlids_includeStaticHtmlPicture($pic->uid, "startseite"); ?>
		    <? if($wrap++ % 3 == 0):?><br style="clear: both;"><? endif; ?>
		<? }
	    ?>
	</div>
	<div style="
	    clear: both;
	    text-align: right;
	    font-weight: bold;
	">
	    <a href="/browse.html">&raquo; More pictures!</a><br>
	</div>

	<!--
	<div id="cichlids_toprated_tanks">
	</div>
	-->

<? if (false && intval($GLOBALS['TSFE']->fe_user->user['uid']) == 0): ?>
	<div style="text-align: center; margin-top: 10px; margin-bottom: 10px;">
<script type="text/javascript"><!--
google_ad_client = "ca-pub-2393694403529430";
/* Frontpage LargeRect */
google_ad_slot = "3849859947";
google_ad_width = 336;
google_ad_height = 280;
//-->
</script>
<script type="text/javascript"
src="https://pagead2.googlesyndication.com/pagead/show_ads.js">
</script>
</div>
<? endif; ?>


	<h1 class="cichlids_header_home">Latest tank examples</h1>
	<div id="cichlids_latest_tanks">
	    <?
		$tanks = $this->get_latest_tanks(6);
		$wrap = 1;
		foreach($tanks as $tank) { ?>
		    <?
			$img = $this->getTankImage($tank, 160, 120, "black");
			if ($img == "")
			    continue;
		    ?>
		    <div class="tank_list_entry">
			<div class="tank_list_image"><a href="<?=$this->getTankLink($tank);?>"><?=$img;?></a></div>
			<div class="tank_list_info">
			    <div class="tank_list_title"><a href="<?=$this->getTankLink($tank);?>"><?=$this->crop($tank->title, 50, "...");?></a></div>
			    <div class="tank_list_user"><b><? $user=$this->getFeUserById($tank->fe_user); print $user->name;?></b>, <?=date("M jS, Y", $tank->tstamp); ?></div>
			    <div class="tank_list_sizeetc"><? $cat=$this->getCategoryById($tank->category); print $cat->title;?>,
				<?=intval($tank->width); ?>x<?=intval($tank->height);?>x<?=intval($tank->depth);?> (<?=intval($tank->size);?> gallons)
			    </div>
			    <div class="tank_list_description">
				    <?=$this->crop($tank->description, 280, "...");?>
			    </div>
			</div>
		    </div>
		    <!--<? if($wrap++ % 1 == 0):?><br style="clear: both;"><? endif; ?>-->
		<? }
	    ?>
	</div>
	<div style="
	    text-align: right;
	    font-weight: bold;
	">
	    <a href="/tank-examples.html">&raquo; More tank examples</a><br>
	</div>

<div style="text-align: center; margin-top: 20px;
">
<script src="https://connect.facebook.net/en_US/all.js#xfbml=1"></script><fb:like-box href="https://www.facebook.com/pages/cichlidscom/197171650304770" width="500" show_faces="true" stream="false" header="true"></fb:like-box>
</div>

    </td>
    <td id="cichlids_right">

	<? if ($GLOBALS['TSFE']->fe_user->user['uid'] == 0):?>
		<div class="home_box">
		    <div class="home_box_header">Member Login</div>

		    <div class="home_box_content">
	    <div id="login_form">
		<form action="/control/login.html" method="POST">
			<table border="0">
			    <tr><td><b>User Name:</b></td><td><input type="text" name="user" value="" id="tx-newloginbox-pi1-user" style="width: 200px;" /></td></tr>
			    <tr><td><b>Password:</b></td><td><input type="password" name="pass" value="" id="tx-newloginbox-pi1-user" style="width: 200px;" /></td></tr>
			    <tr><td><b>Remember?</b></td><td><input name="permalogin" value="0" type="hidden" disabled="disabled" id="permaloginHiddenField" />
			<input name="permalogin" value="1" type="checkbox" checked="checked" id="permalogin"  onclick="document.getElementById('permaloginHiddenField').disabled = this.checked;" />
		
				</td></tr>
			    <tr><td>&nbsp;</td><td>
				<div style="float: right;"><a href="/control/login/sign-up.html"><b>Sign Up</b></a></div> 
				<input type="submit" name="submit" value="Login"/>
			    </td></tr>
			    <tr><td>&nbsp;</td><td><div style="font-size: 8pt; font-weight: bold;">Forgot: <a href="/nc/control/login/loginpassword/forgot.html?tx_felogin_pi1%5Bforgot%5D=1">Username/Password</a>?</td></tr>
			</table>
		<input type="hidden" name="logintype" value="login" />
		<input type="hidden" name="pid" value="4" />
		</form>

<!--
Do you already have an account on one of these sites? Click the logo to log in with it here:
-->

<?
	$recs = array(138);
        $cObj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
        $conf = array(
                "source" => join(",", $recs),
                "tables" => "tt_content",
        ); 
        print $cObj->cObjGetSingle("RECORDS", $conf);
?>

		    </div>
		</div>
	    </div>
	<? endif; ?>

<? if (false && intval($GLOBALS['TSFE']->fe_user->user['uid']) == 0): ?>
<script type="text/javascript"><!--
google_ad_client = "ca-pub-2393694403529430";
/* Frontpage sidebar */
google_ad_slot = "3164689627";
google_ad_width = 300;
google_ad_height = 250;
//-->
</script>
<script type="text/javascript"
src="https://pagead2.googlesyndication.com/pagead/show_ads.js">
</script>
<? endif; ?>

	<? if(true): ?>
    	<? $ofthehour = $this->get_ofthehour();  ?>
	<div id="random_picture">

	    <h1 class="cichlids_header_home">Picture of the hour</h1>
	    <center>
	    <? $pic = $ofthehour['picture']; ?>
		<a href="<?=$this->getPictureLink($pic);?>"><?=$this->getPictureImage($pic, 300, 222, "black");?></a>
		<b><a href="<?=$this->getPictureLink($pic);?>"><?=$this->crop($pic->title, 30, "...");?></a></b>
		    <div style="font-size: 8pt; "><?=date("M jS, Y", $pic->tstamp); ?></div>
		    <div style="font-size: 8pt; "><? $user=$this->getFeUserById($pic->fe_user); print $user->name;?></div>
	    </center>
	</div>
	<? endif; ?>

<!--
	<br><br>
-->
	<div id="latest_comments">
	    <div class="home_box">
		<div class="home_box_header">Latest comments:</div>
		<div class="home_box_conent">
		<?  $comments = $this->get_latest_comments(10); foreach($comments as $comment) {
		    cichlids_includeStaticHtmlComment($comment->uid, "latest_wide");
		} ?>
		</div>
	    </div>
	</div>

<? if (false && intval($GLOBALS['TSFE']->fe_user->user['uid']) == 0): ?>
<? endif; ?>



	<div id="random_species">
	</div>
	<div id="popular_pictures">
	</div>
	<div id="popular_tanks">
	</div>
	<div id="random_user_galleries">
	</div>
    </td>
</tr>
</table>
