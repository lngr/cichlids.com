<?
function cichlids_crop($str, $len, $after = "") {
      return cichlids_extCrop($str, "$len | $after");
}

/* stolen from Typo3 */
function cichlids_extCrop($content,$options)        {
	$options = explode('|',$options);
	$chars = intval($options[0]);
	$afterstring = count($options) > 1 ? trim($options[1]) : '';
	$crop2space = count($options) > 2  ? trim($options[2]) : '';
	if ($chars)     {
		if (strlen($content)>abs($chars))       {
			if ($chars<0)   {
				$content = substr($content,$chars);
				$trunc_at = strpos($content, ' ');
				$content = ($trunc_at&&$crop2space) ? $afterstring.substr($content,$trunc_at) : $afterstring.$content;
			} else {
				$content = substr($content,0,$chars);
				$trunc_at = strrpos($content, ' ');
				$content = ($trunc_at&&$crop2space) ? substr($content, 0, $trunc_at).$afterstring : $content.$afterstring;
			}
		}
	}
	return $content;
}

function cichlids_out2file($str, $outfile) {
    umask(002);
    $dir = dirname($outfile);
    if (!is_dir($dir)) {
	mkdir($dir, 0755, true);
	exec("/bin/chgrp apache $dir");
	exec("/bin/chmod 775 $dir");
    }

    // XXX mit tempfile komische "Operation not permitted" Warnung von www aus
    // vielleicht wegen selinux??
    // $tmpfile = tempnam ("/tmp", "cichlids.com-tempout");
    $tmpfile = $outfile;

    $fh = fopen($tmpfile, 'w') or die("can't open file $tmpfile");
    fwrite($fh, $str);
    fclose($fh);
    //rename($tmpfile, $outfile);
    $gid = filegroup($outfile);
    if ($gid != 48) 
    	exec("/bin/chgrp apache $outfile");
    if (substr(base_convert(fileperms($outfile), 10, 8), 3) != "664")
    	exec("/bin/chmod 664 $outfile");
}

?>
