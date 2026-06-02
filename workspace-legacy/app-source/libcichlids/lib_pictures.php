<?
require_once("libcichlids.php");

function cichlids_generatePicture($uid, $force = false) {

    if(!$force) {
	$load = sys_getloadavg();
	if ($load[0] > 13) return;
    }

    $query = "SELECT * FROM user_cichlids_pictures WHERE uid=$uid";
    $res = mysql_query($query);
    if (mysql_num_rows($res) == 0) {
	die("Picture $uid nonexistent.\n");
    }
    $row = mysql_fetch_assoc($res);

    if ($row['image'] == "") {
	mysql_query("UPDATE user_cichlids_pictures SET deleted=1 WHERE uid=".$row['uid']);
	return;
    }
    cichlids_createImage($row['uid'], $row['image'], 100, 75, "black", $row['tstamp']);    // mini-previews
    cichlids_createImage($row['uid'], $row['image'], 450, 600, false, $row['tstamp']);	    // single view
    cichlids_createImage($row['uid'], $row['image'], 130, 97, "black", $row['tstamp']);    // pics listing next generation
    cichlids_createImage($row['uid'], $row['image'], 167, 123, "black", $row['tstamp']);   // startseite und pics_listing old

    cichlids_createHtmlForPicture($row, "startseite");
    cichlids_createHtmlForPicture($row, "listing_old");
    cichlids_createHtmlForPicture($row, "listing");
    cichlids_createHtmlForPicture($row, "related");
    cichlids_createHtmlForPicture($row, "rss");

}

function cichlids_createImage($uid, $origfile, $width, $height, $border, $lastupdate) {
    $imgpath = $GLOBALS['cichlids_imgpath'];
    $origpath = $GLOBALS['cichlids_origpath'];
    $convert = $GLOBALS['cichlids_convert'];

    $filename = cichlids_getImageFilename($uid, $origfile, $width, $height, $border);

    $targetfilename = "$imgpath/$filename";
    $infile = "$origpath/$origfile";

    if (file_exists($targetfilename)) {
	// print "$targetfilename existiert\n";
	$mtime = filemtime($targetfilename);
	if ($mtime > $lastupdate && $mtime > filemtime($infile)) {
	    return;
	}
    }

    $geometry = $width . "x" . $height;
    $outdir = dirname($targetfilename);
    if (!is_dir($outdir)) {
	mkdir($outdir, 0777, true);
    }

    $cmd = "$convert -geometry $geometry ";
    if ($border) {
	$cmd .= " -bordercolor black -border 200 -gravity center -crop $geometry+0+0 +repage";
    } else {
    }
    $cmd .= " " . escapeshellarg($infile.'[0]') . " -auto-orient '$targetfilename'";

    umask(002);
    if(file_exists($targetfilename))
	unlink($targetfilename);
    $ret = exec($cmd);
    return $ret;
}

function cichlids_createHtmlForPicture($picture, $tmplname) {
    $template = "template_picture_$tmplname.php";
    ob_start();
    include($template);
    $out = ob_get_contents();
    ob_end_clean();

    $outfile = cichlids_getStaticHtmlPicture($picture['uid'], $tmplname);
    cichlids_out2file($out, $outfile);
}

?>
