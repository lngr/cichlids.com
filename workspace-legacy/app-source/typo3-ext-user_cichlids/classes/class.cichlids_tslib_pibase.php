<?php

require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.mvc_tslib_pibase.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_picture.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_feuser.php');

require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.picture_manager.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.comments_manager.php');

class cichlids_tslib_pibase extends mvc_tslib_pibase {

	function __construct() {
	    parent::__construct();
	    $this->comments_manager = new comments_manager();
	    $this->picture_manager = new picture_manager();
	}

	function crop($str, $len, $after = "") {
	    return $this->cObj->crop($str, "$len | $after");
	}

        function getCurrentFEUser() {
            return $this->getFEUserById($GLOBALS['TSFE']->fe_user->user['uid']);
        }
        function getFeUserById($uid) {
            $uid = intval($uid);
            $res = mysql_query("SELECT * FROM fe_users WHERE uid=$uid LIMIT 1");
            if (mysql_num_rows($res) == 0)
                return new cichlids_feuser(array());
            return new cichlids_feuser(mysql_fetch_assoc($res));
        }

        function getPictureLinkFullSize($pic) {
            $filename = $pic->image;
            $conf = array(
                "file"  =>    "uploads/tx_usercichlids/$filename",
            );
            return $this->cObj->cObjGetSingle("IMG_RESOURCE", $conf);
        }
        function getPictureLink($pic, $cache=true, $overwrite=1) {
            $pid = $this->conf['cichlidPicturesView'];
            if ($pic->pid == $this->conf['tankPicturesPid'])
                $pid = $this->conf['tankPicturesView'];
            return $this->pi_linkTP_keepPIvars_url(array("picture" => $pic->uid), $cache, $overwrite, $pid);
        }

        function getTankLink($tank, $cache=true) {
            $pid = $this->conf['tanksView'];
            return $this->pi_linkTP_keepPIvars_url(array("tank" => $tank->uid), $cache, 1, $pid);
        }
        function getCommentLink($comment, $cache=true) {
            switch($comment->type) {
                case 1:
                    return $this->getPictureLink($this->picture_manager->findById($comment->item), $cache);
                case 2:
                    return $this->getTankLink($this->getTankById($comment->item), $cache);
            }
        }


        function getPictureImage($pic, $width, $height, $extent = false) {
            $filename = $pic->image;
            $conf = array(
                "file"  =>    "uploads/tx_usercichlids/$filename",
                "file." => array(
                    "maxW"  => $width,
                    "maxH"  => $height,
                    "params" => ($extent ? "-bordercolor $extent -border $width -gravity center -crop $width"."x"."$height+0+0 +repage" : ""),
                ),
            );
            return $this->cObj->cObjGetSingle("IMAGE", $conf);
        }

        function getPictureImageSmallPreview($pic) {
            $filename = $pic->image;
	    $width = 100;
	    $height = 75;
	    $prevheight = $height + 15;
	    $texttop = $height - 30;
	    $file = "uploads/tx_usercichlids/$filename";
	    $imginfo = getimagesize($file);
	    $owidth = $imginfo[0];
	    $oheight = $imginfo[1];
	    $text = $owidth . "x" . $oheight;
            $conf = array(
                "file"  =>    $file,
                "file." => array(
                    "maxW"  => $width,
                    "maxH"  => $height,
                    "params" => "-bordercolor $extent -border $width -gravity center -crop $width"."x"."$prevheight+0+10 +repage -draw \"text 0,$texttop '$text'\" -fill white",
                ),
            );
	    $val = array(
		"url" => $this->cObj->cObjGetSingle("IMG_RESOURCE", $conf),
		"width" => $width,
		"height" => $prevheight,
	    );
	    return $val;
        }


        function getTankImage($tank, $width, $height, $extent = false) {
            return $this->getPictureImage($this->getPictureById($tank->image), $width, $height, $extent);
        }

        function showRatingStars($rating) {
            $rating = round($rating);
            if ($rating < 0)
                $rating = 0;
            if ($rating > 5)
                $rating = 5;
            return '<img src="/fileadmin/smile'.$rating.'.gif" width="75" height="15" border="0">';
        }


	function getLatestComments($max = 15) {
	    return $this->comments_manager->findAll(0, 0, 0, 0, $max, "tstamp DESC", true);
	}

	/* from pi3 */


	function select_pictures($query) {
	    $res = mysql_query($query);
	    $pics = array();
	    while($row = mysql_fetch_assoc($res))
		$pics[] = new cichlids_picture($row);
	    return $pics;
	}

	function select_tanks($query) {
	    $res = mysql_query($query);
	    $tanks = array();
	    while($row = mysql_fetch_assoc($res))
		$tanks[] = new cichlids_tank($row);
	    return $tanks;
	}
	function select_categories($query) {
	    $res = mysql_query($query);
	    $all = array();
	    while($row = mysql_fetch_assoc($res))
		$all[] = new cichlids_categories($row);
	    return $all;
	}
	function select_comments($query) {
	    $res = mysql_query($query);
	    $all = array();
	    while($row = mysql_fetch_assoc($res))
		$all[] = new cichlids_comment($row);
	    return $all;
	}
	function select_galleries($query) {
	    $res = mysql_query($query);
	    $all = array();
	    while($row = mysql_fetch_assoc($res))
		$all[] = new cichlids_gallery($row);
	    return $all;
	}
      

	function get_latest_pictures($max) {
	    $sel = array(
		"pidInList"   => $this->conf['picturesPid'],
		"orderBy"     => "tstamp DESC",
		"max"	      => 2*$max,
	    );
	    $query = $this->cObj->getQuery("user_cichlids_pictures", $sel);
	    $res = $this->select_pictures($query);
	    $out = array_slice($res, 0, $max);
	    return $out;
	}

	function get_toprated_pictures($max) {
	    $sel = array(
		"pidInList"   => $this->conf['picturesPid'],
		"orderBy"     => "rating DESC",
		"max"	      => $max,
	    );
	    $query = $this->cObj->getQuery("user_cichlids_pictures", $sel);
	    $res = mysql_query($query);
	    $pics = array();
	    while($row = mysql_fetch_assoc($res))
		$pics[] = new cichlids_picture($row);
	    return $pics;
	}

	function getCommentPicture($comment, $cache=true) {
	    switch($comment->type) {
		case 1:
		    return $this->getPictureById($comment->item);
		case 2:
		    $tank = $this->getTankById($comment->item);
		    return $this->getPictureById($tank->image);
	    }
	}

	function getPictureById($uid) {
	    $uid = intval($uid);
	    if ($uid == 0)
		return new cichlids_picture(array());
	    $res = mysql_query("SELECT * FROM user_cichlids_pictures WHERE uid=$uid LIMIT 1");
	    if (mysql_num_rows($res) == 0)
		return new cichlids_picture(array());
	    return new cichlids_picture(mysql_fetch_assoc($res));
	}
	function getTankById($uid) {
	    $uid = intval($uid);
	    if ($uid == 0)
		return new cichlids_tank(array());
	    $res = mysql_query("SELECT * FROM user_cichlids_tanks WHERE uid=$uid LIMIT 1");
	    if (mysql_num_rows($res) == 0)
		return new cichlids_tank(array());
	    return new cichlids_tank(mysql_fetch_assoc($res));
	}
	function getCategoryById($uid) {
	    $uid = intval($uid);
	    if ($uid == 0)
		return new cichlids_category(array());
	    $res = mysql_query("SELECT * FROM user_cichlids_category WHERE uid=$uid LIMIT 1");
	    if (mysql_num_rows($res) == 0)
		return new cichlids_category(array());
	    return new cichlids_category(mysql_fetch_assoc($res));
	}
	function getGalleryById($uid) {
	    $uid = intval($uid);
	    $res = mysql_query("SELECT * FROM user_cichlids_gallery WHERE uid=$uid LIMIT 1");
	    if (mysql_num_rows($res) == 0)
		return new cichlids_gallery(array());
	    return new cichlids_gallery(mysql_fetch_assoc($res));
	}

	function getUserGalleries($user) {
	    $userid = $user->uid;
	    $query = "SELECT * FROM user_cichlids_gallery WHERE fe_user=$userid";
	    return $this->select_galleries($query);
	}

	function getManagedUserGallery() {
	    if (isset($this->createdGallery))
		$galid = $this->createdGallery;
	    else
		$galid = $this->piVars['gallery'];
	    return $this->getGalleryById($galid);
	}

	function get_latest_tanks($max) {
	    $sel = array(
		"pidInList"   => $this->conf['tanksPid'],
		"orderBy"     => "tstamp DESC",
		"max"	      => $max,
		"where"	      => "(width>10 AND height>10 AND depth>10 AND description != '') AND hidden=0 and deleted=0"
	    );
	    $query = $this->cObj->getQuery("user_cichlids_tanks", $sel);
	    return $this->select_tanks($query);
	}

	function get_latest_comments($max) {
	    /* Alle Kats selecten */
	    $sel = array(
		"pidInList"	    =>	$this->conf['commentsPid'],
		"orderBy"	    =>	"tstamp DESC",
		"max"		    => $max,
		"where"		    => "note != ''"
	    );
	    $query = $this->cObj->getQuery("user_cichlids_comments", $sel);
	    return $this->select_comments($query);
	}


	
	function get_ofthehour() {
	    $tstamp = date('YmdH');
	    $query = "SELECT * FROM user_cichlids_ofthehour WHERE tstamp=$tstamp";
	    $res = mysql_query($query);
	    if (mysql_num_rows($res) > 0) {
		$row = mysql_fetch_assoc($res);
		$return = array();
		$return['picture'] = $this->getPictureById($row['picture']);
		return $return;
	    } else {
		$return = array();
		$pic = $this->get_random_picture();
		$return['picture'] = $pic;
		$query = "INSERT INTO user_cichlids_ofthehour (tstamp, picture) VALUES ($tstamp, ".$pic->uid.")";
		$res = mysql_query($query);

		return $return;
	    }
	    
	}
	function get_random_picture() {
	    $where = "deleted != 1 AND hidden != 1";
	    $query = "SELECT COUNT(*) as anzahl FROM user_cichlids_pictures WHERE $where";
	    $res = mysql_query($query);
	    $row = mysql_fetch_assoc($res);
	    $all = $row['anzahl'];
	    $which = rand(0, $all - 1);
	    $query = "SELECT * FROM user_cichlids_pictures WHERE $where LIMIT $which,1";
	    $rows = $this->select_pictures($query);
	    return $rows[0];
	}

	/* ----- OLD ------ */
	

}

?>
