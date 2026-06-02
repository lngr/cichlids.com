<?
  require_once("libcichlids.php");

  $width = 450;
  $height = 600;
  $imgtag = cichlids_getImageTag($picture['uid'], $picture['image'], $width, $height, "black");
  $piclink = cichlids_getPictureLink($picture['uid']);
  $username = cichlids_getUsername($picture['fe_user']);
  $stars = cichlids_getRatingStars($picture['rating']);
  $fullsizelink = "this is a fullsize link";

?>
<div class="picture_image" style="width: <?=$width;?>px; text-align: center;"><a href="<?=$fullsizelink;?>"><?=$imgtag;?></a></div>
<div class="picture_description" style="margin-top: 5px; text-align: center;"><?=$picture['description'];?></div>
