<?
  require_once("libcichlids.php");

  $imgtag = cichlids_getImageTag($picture['uid'], $picture['image'], 130, 97, "black");
  $piclink = cichlids_getPictureLink($picture['uid']);
  $username = cichlids_getUsername($picture['fe_user']);
  $stars = cichlids_getRatingStars($picture['rating']);
return "";
?>


        <table style="border: 1px solid #AAAAAA; width: 100%">
            </td></tr>
            <tr><td>
                <table border="0" width="100%">
                    <tr>
                        <td class="smallText" style="text-align: center;"><b>Posted by:</b><br>
                            <b><? $user=$this->getFeUserById($pic->fe_user); print $user->name;?></b>

                            <h1>Link User</h1>
                        </td>
                        <td class="smallText" style="text-align: center;"><b>Last updated:</b><br>
                            <?=date("M jS, Y", $pic->tstamp); ?>
                        </td>
                    </tr>
                </table>
            </td></tr>
            <tr><td style="border-top: 1px solid #AAAAAA;">
                <table border="0" width="100%">
                    <tr>
                        <td style="text-align: center;">
                            <? if($pic->rating_count > 0): ?>
                                <?=$this->showRatingStars($pic->rating); ?><br><?=$pic->rating_count;?> votes
                            <? else: ?>
                                (not enough votes)
                            <? endif; ?>
                        </td>
                        <td style="text-align: center;">Views: <b><?=$pic->views;?></b></td>
                    </tr>
                </table>
            </td></tr>
            <tr><td style="border-top: 1px solid #AAAAAA;">

                Wenn eine Categroy (Lake Tang. usw, dann hier hin)

                Wenn Tags, dann hier hin


        <?
            $preview = $this->getPictureImageSmallPreview($pic);
            $preview_url = $preview['url'];
            $url = 'http://www.cichlids.com/'.$link;
            $img_code = '<a href="http://www.cichlids.com'.$link.'"><img width="'.$preview['width'].'" height="'.$preview['height'].'" border="0" src="http://www.cichlids.com/'.$preview['url'].'"></a>';
            $bb_code = '[url=http://www.cichlids.com/'.$link.'][img]http://www.cichlids.com/' . $preview_url . '[/img][/url]';
        ?>


     <table border="0" width="0">
            <tr>
                <td style="font-weight: bold; ">URL</td>
                <td><input style="width: 200px; " name="url_code" type="text" value='<?=$url;?>' onClick="javascript:this.form.url_code.focus();this.form.url_code.select();" readonly="true"></td>
            </tr>
            <tr>
                <td style="font-weight: bold; ">Website</td>
                <td><input style="width: 200px; " name="img_code" type="text" value='<?=$img_code;?>' onClick="javascript:this.form.img_code.focus();this.form.img_code.select();" readonly="true"></td>
            </tr>
            <tr>
                <td style="font-weight: bold; ">BBCode</td>
                <td>
                    <input style="width: 200px; " name="bb_code" type="text" value='<?=$bb_code;?>' onClick="javascript:this.form.bb_code.focus();this.form.bb_code.select();" readonly="true"><br>
                    <span style="font-size: 7pt; color: #888888;">For usage in forums like <a href="/disc/">ours</a> or <a href="http://www.cichlid-forum.com/phpBB/">cichlid-forum.com</a></span>
                    </td></tr>
        </table>
