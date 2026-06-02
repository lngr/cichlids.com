<?
require_once("libcichlids.php");

function cichlids_getImageFilename($uid, $filename, $width, $height, $border) {
    $tohash = "$uid-$filename-$width-$height-$border";
    $hash = md5($tohash);
    $sub1 = substr($hash, 0, 2);
    $sub2 = substr($hash, 2, 2);
    return "$width" . "x" . "$height" . "/$sub1/$sub2/" . md5($tohash) . ".jpg";
}

function cichlids_getImageUrl($uid, $filename, $width, $height, $border) {
    $webimgpath = $GLOBALS['cichlids_webimgpath'];
    return $webimgpath . "/" . cichlids_getImageFilename($uid, $filename, $width, $height, $border);
}

function cichlids_getImageTag($uid, $filename, $width, $height, $border, $extra="") {
    $imgfile = cichlids_getImageUrl($uid, $filename, $width, $height, $border);
    return '<img src="'.$imgfile.'" width="'.$width.'" height="'.$height.'" border="0" '.$extra.'/>';
}

function cichlids_getStaticHtmlPicture($uid, $template) {
    return $GLOBALS['cichlids_staticpath'] . "/pictures/"  . substr($uid, 0, 3) . "/" . $uid . "/$template.html";
}
function cichlids_getStaticHtmlComment($uid, $template) {
    return $GLOBALS['cichlids_staticpath'] . "/comments/" . substr($uid, 0, 3) . "/" .  $uid . "/$template.html";
}

function cichlids_includeStaticHtmlPicture($uid, $template, $generate=true, $force_generate=false) {
    $staticfile = cichlids_getStaticHtmlPicture($uid, $template);
    if ($template == "rss")
	print $staticfile;
    if ($force_generate || !file_exists($staticfile) && $generate) {
	cichlids_generatePicture($uid);
    }
    if (file_exists($staticfile))
	include($staticfile);
}
function cichlids_includeStaticHtmlComment($uid, $template, $generate=true, $force_generate=false) {
    $staticfile = cichlids_getStaticHtmlComment($uid, $template);
    if ($force_generate || $generate && !file_exists($staticfile)) {
	cichlids_generateComment($uid);
    }
    if (file_exists($staticfile)) {
	include($staticfile);
    }
}

function cichlids_getUsername($uid) {
    $uid = intval($uid);
    if (intval($uid) == 0)
	return "anonymous";

    $query = "SELECT * FROM fe_users WHERE uid=$uid";

    $res = mysql_query($query);
    $row = mysql_fetch_assoc($res);
    return $row['name'];
}
function cichlids_getCommentPosterName($comment) {
    if ($comment['fe_user'] == 0) {
	$username = $comment['poster'] . " (anonymous)";
    } else
	$username = cichlids_getUsername($comment['fe_user']);
    return $username;
}

function cichlids_getRatingStars($rating) {
    $rating = round($rating);
    if ($rating < 0)
	$rating = 0;
    if ($rating > 5)
        $rating = 5;
    return '<img src="/fileadmin/smile'.$rating.'.gif" width="75" height="15" border="0">';
}

function cichlids_getPictureLink($uid) {
    if (intval($uid) == 0)
	return "/unknown";


    $query = "SELECT * FROM user_cichlids_pictures WHERE uid=$uid";
    $res = mysql_query($query);
    $row = mysql_fetch_assoc($res);

    switch(intval($row['pid'])) {
	case 29:
	    $url = "/tank-pictures";
	    $pid = 13;
	    break;
	default:
	    $url = "/pictures";
	    $pid = 18;
    }

    $query = "SELECT * FROM tx_realurl_uniqalias WHERE tablename='user_cichlids_pictures' AND field_id='uid' AND value_id=$uid";
    
    $res = mysql_query($query);
    if (mysql_num_rows($res) == 0) {
	$val = "$url.html?no_cache=1&user_cichlids_pi1[picture]=".$uid;
    } else {
	$row = mysql_fetch_assoc($res);
	$alias = $row['value_alias'];
	$val = "$url/pic/$alias.html";
    }

    return $val;
}

function cichlids_getTankLink($uid) {
    return "/tank-examples.html?no_cache=1&user_cichlids_pi1[tank]=$uid";
}

function cichlids_getCommentLink($comment) {
    $type = $comment['type'];
    $uid = $comment['item'];
    
    switch($type) {
	case 1: // picture
	    return cichlids_getPictureLink($uid);
	case 2:
	    return cichlids_getTankLink($uid);
    }

    return "";
}

function cichlids_getCommentPicture($comment) {
    $type = $comment['type'];
    $uid = $comment['item'];

    switch($type) {
	case 1:
	    $res = mysql_query("SELECT * FROM user_cichlids_pictures WHERE uid=".$uid);
            $row = mysql_fetch_assoc($res);
	    return $row;
	case 2:

	    $res = mysql_query("SELECT * FROM user_cichlids_tanks WHERE uid=$uid");
	    $tank = mysql_fetch_assoc($res);
	    $res = mysql_query("SELECT * FROM user_cichlids_pictures WHERE uid=".$tank['image']);
	    $row = mysql_fetch_assoc($res);
	    return $row;
    }

    return array();
}


?>
