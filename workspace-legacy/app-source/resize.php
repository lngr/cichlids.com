<?

    $width = intval($_GET['width']);
    $height = intval($_GET['height']);
    $file = $_GET['file'];
    if(isset($_GET['frame'])) $frame = intval($_GET['frame']);
    $geom = $width."x".$height;

    $convert = "/usr/local/bin/convert";
    $origpath = "/mnt/web1/userpics/user_pics/";
    $targetpath = "/mnt/web1/userpics/".($frame ? "frame" : "resize")."/";

    $allowed_sizes = array(
	"800x600",
	"450x600",
	"300x250",
	"167x123",
	"130x97",
	"100x75",
    );

    $origfile = $origpath . $file;
    if(!in_array($geom, $allowed_sizes) ||
      !file_exists($origfile)) {
	header("HTTP/1.0 404 Not Found");
	print("<h1>HTTP/1.0 404 Document Not Found</h1>\n");
	exit(404);
    }

    $targetfilename = $targetpath.$geom."/".$file;
    $dirname = dirname($targetfilename);
    if (!is_dir($dirname)) {
	mkdir($dirname);
    }

    $cmd = "$convert -geometry $geom ";
    if($frame) 
	$cmd .= " -bordercolor black -border 200 -gravity center -crop $geom+0+0 +repage";
    $cmd .= " " . escapeshellarg($origfile.'[0]') . " " . escapeshellarg($targetfilename);
    $ret = exec($cmd);
    if(!file_exists($targetfilename)) {
	header("HTTP/1.0 505 Internal Server Error");
	print "An error occured, I'm sorry";
	return;
    }

    header("Content-type: image/jpeg");
    readfile($targetfilename);
?>
