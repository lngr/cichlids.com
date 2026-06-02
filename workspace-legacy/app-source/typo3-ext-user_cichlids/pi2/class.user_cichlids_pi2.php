<?php

require_once("/var/www/html/www-cichlids/libcichlids/libcichlids.php");

require_once(PATH_tslib."class.tslib_pibase.php");

require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_tslib_pibase.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_picture.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_feuser.php');

require_once(t3lib_extMgm::extPath('user_cichlids').'banlist.php');

class user_cichlids_pi2 extends cichlids_tslib_pibase {
	var $prefixId = "user_cichlids_pi2";		// Same as class name
	var $scriptRelPath = "pi2/class.user_cichlids_pi2.php";	// Path to this script relative to the extension dir.
	var $extKey = "user_cichlids";	// The extension key.

	function main($content,$conf)	{
		$this->conf=$conf;
		$this->pi_setPiVarDefaults();
		$this->pi_loadLL();

		switch ($this->cObj->data['select_key']) {
		    case "edit_or_post_pic":
			return $this->edit_or_post_pic($content, $conf['edit_or_post_pic.']);
		    case "edit_or_post_tank":
			return $this->edit_or_post_tank($content, $conf['edit_or_post_tank.']);
		    case "galleries":
			return $this->manage_galleries();
		}

		if ($this->piVars['action'] == "")
		    $this->piVars['action'] = "listing";

		switch ($this->piVars['action']) {
		    case "listing":
			return $this->tanks_listing($content, $conf) . $this->pics_listing($content, $conf);
		}
	}


	/* ------------------------- */

        function templating($file) {
            ob_start();
            include(t3lib_extMgm::extPath('user_cichlids').'scripts/'.$file);
            $out = ob_get_contents();
            ob_end_clean();
            return $out;
        }

	function linkView($view, $cache = false, $overwrite = false) {
	    return $this->pi_linkTP_keepPIvars_url(array("view" => $view), $cache, $overwrite);
	}

	function controller() {
	    if ($this->piVars['controllerFrom'] == "")
		$this->piVars['controllerFrom'] == 'default';
	    $from = $this->piVars['controllerFrom'];

	    $view = $this->execute($this->action[$from]);

	    $file = $this->flow[$from][$to];
	    if ($file != "")
		return $this->templating($file);
	    else
		return $this->templating($this->flow['default']);
	}

	function manage_galleries() {
	    return $this->controller();
	}

	function get_galleries_of_FEUser() {
	    $user = $GLOBALS['TSFE']->fe_user->user;
	    return $user;
	}



	/* ------------ Alte Funktionen ---------------- */



	/*
	 * Hier kommt er immer rein, sei es, ob gerade ein Tank bearbeitet wird oder ob ein Bild ausgew&auml;hlt wird
	 */
	function edit_or_post_tank($content, $conf) {
	    $obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use

	    $userid = $GLOBALS['TSFE']->fe_user->user['uid'];
	    $pid = 15;

	    // erstmal testen, ob das so erlaubt f&uuml;r diesen User ist, nicht, dass er sich einfach eine andere TankID erschleicht
	    if ($this->piVars['tankUid'] && $this->piVars['tankUid'] != "NEW") {
		$query = "SELECT * FROM user_cichlids_tanks WHERE fe_user=$userid AND uid=".intval($this->piVars['tankUid']);
		$res = mysql_query($query);
		$row = mysql_fetch_assoc($res);
		mysql_free_result($res);
		if ($row['uid'] != $this->piVars['tankUid'] || $row['fe_user'] != $userid)
		    return "not your pic, don't try this again.  IP and fe_user name logged";
	    }

	    // eine neue UID zuweisen falls es noch keine gibt.
	    if ($this->piVars['action'] == "save" && $this->piVars['tankUid'] == "NEW") {
		$query = "INSERT INTO user_cichlids_tanks (pid, tstamp, crdate, fe_user) VALUES (
		    $pid,
		    UNIX_TIMESTAMP(NOW()),
		    UNIX_TIMESTAMP(NOW()),
		    $userid )
		";
		mysql_query($query);
		if (mysql_errno())
		    print mysql_error();

		$this->piVars['tankUid'] = mysql_insert_id();
		$_POST['user_cichlids_pi2']['tankUid'] = $this->piVars['tankUid'];
	    }


	    /*
	      Submit has been pressed on the "edit tank" page, so "save" is set.   WE don't know which submit button has been set, but
	      we need to save the changes of this user.  We also set "shown" = false, so the user can modify the tank before people can see it.
	     */
	    if ($this->piVars['action'] == "save") {
		$query = "UPDATE user_cichlids_tanks SET not_shown = 1,
				tstamp		= UNIX_TIMESTAMP(NOW()),
				title		= '".mysql_escape_string($this->piVars['title'])."',
				category	= ".intval($this->piVars['category']).",
				width	  	= ".intval($this->piVars['width']).",
				height	  	= ".intval($this->piVars['height']).",
				depth	  	= ".intval($this->piVars['depth']).",
				unit		= '".mysql_escape_string($this->piVars['unit'])."',
				description  	= '".mysql_escape_string($this->piVars['description'])."',
				gravel	  	= '".mysql_escape_string($this->piVars['gravel'])."',
				plants	  	= '".mysql_escape_string($this->piVars['plants'])."',
				more_deco  	= '".mysql_escape_string($this->piVars['more_deco'])."',
				light		= '".mysql_escape_string($this->piVars['light'])."',
				light_duration	= '".mysql_escape_string($this->piVars['light_duration'])."',
				filtration	= '".mysql_escape_string($this->piVars['filtration'])."',
				more_tec	= '".mysql_escape_string($this->piVars['more_tec'])."',
				water_ph	= '".mysql_escape_string($this->piVars['water_ph'])."',
				water_kh	= '".mysql_escape_string($this->piVars['water_kh'])."',
				water_gh	= '".mysql_escape_string($this->piVars['water_gh'])."',
				water_no2	= '".mysql_escape_string($this->piVars['water_no2'])."',
				water_no3	= '".mysql_escape_string($this->piVars['water_no3'])."',
				water_po4	= '".mysql_escape_string($this->piVars['water_po4'])."',
				more_water	= '".mysql_escape_string($this->piVars['more_water'])."',
				food		= '".mysql_escape_string($this->piVars['food'])."',
				more		= '".mysql_escape_string($this->piVars['more'])."'

				WHERE uid=".intval($this->piVars['tankUid'])."
				LIMIT 1";
		mysql_query($query);

                $query = "UPDATE    user_cichlids_tanks
		             LEFT JOIN fe_users ON user_cichlids_tanks.fe_user = fe_users.uid
			     SET       user_cichlids_tanks.realurltitle = CONCAT(CONCAT(fe_users.name, ' '), user_cichlids_tanks.title)";
		mysql_query($query);
	    }

	    $reload = false;
	    if ($this->piVars['action'] == "upload_pic") {
		$pic_type = $GLOBALS['TSFE']->fe_user->getKey("ses", "select_pic_type");
		switch ($pic_type) {
		    case 'main':
		    case 'tank_images':
			$pid = 29;
			break;
		    case 'deco_images':
			$pid = 63;
			break;
		    case 'tec_images':
			$pid = 62;
			break;
		    default:
			$pid = 0;
		}

		$pic_uid = $this->save_picture_from_request($content, $conf, $pid);
		$this->piVars['action'] = 'select_pic';
		$this->piVars['select_pic_uid'] = $pic_uid;
		$reload = true;
	    }


	    /*
	      If action == select_pid, we come from the selct_pic screen, so we need to assign this picture to this tank
	     */
	    if ($this->piVars['action'] == "select_pic") {
		  $col = $GLOBALS['TSFE']->fe_user->getKey("ses", "select_pic_type");
		  switch($col) {
		      case "main":
			  $query = "UPDATE user_cichlids_tanks SET image=".intval($this->piVars['select_pic_uid'])." WHERE uid=".intval($this->piVars['tankUid'])." LIMIT 1";
			  mysql_query($query);
			  if (mysql_errno())
			      print mysql_error();
			  break;
		      case "tank_images":
		      case "deco_images":
		      case "tec_images":
			  $query = "SELECT $col FROM user_cichlids_tanks WHERE uid=".intval($this->piVars['tankUid'])." LIMIT 1";
			  $res = mysql_query($query);
			  if (mysql_errno())
			      print mysql_error();
			  $row = mysql_fetch_assoc($res);
			  mysql_free_result($res);

			  //insert
			  $list = split(",", $row[$col]);
			  $new = intval($this->piVars['select_pic_uid']);
			  $list[] = $new;
			  $list = array_unique($list);
			  $val = join(",", $list);
			  $query = "UPDATE user_cichlids_tanks SET $col='$val' WHERE uid=".intval($this->piVars['tankUid'])." LIMIT 1";
			  mysql_query($query);
			  if (mysql_errno())
			      print mysql_error();
			  break;
		      default:
			  print "drollig";
		  }
	    }

	    if ($reload) {
		header("Location: " . $_SERVER['REQUEST_URI']);
		return "..";
	    }

	    /*
		more or less the same for the delete_pic screen
	     */
	    if (isset($this->piVars['delete_pic'])) {
		$del_list = $this->piVars['delete_pic'];
		foreach (array_keys($del_list) as $where) {
		    if (
			!in_array($where, array("tank_images")) &&
			!in_array($where, array("deco_images")) &&
			!in_array($where, array("tec_images"))
			)
			return "cannot remove from $where";

		    $query = "SELECT $where FROM user_cichlids_tanks WHERE uid=".intval($this->piVars['tankUid'])." LIMIT 1";
		    $res = mysql_query($query);
		    if (mysql_errno())
		        print mysql_error();
		    $row = mysql_fetch_assoc($res);
		    mysql_free_result($res);

		    $list = split(",", $row[$where]);
		    foreach(array_keys($list) as $key) {
			if (in_array($list[$key], array_keys($del_list[$where])))
			    unset($list[$key]);
		    }
		    $val = join(",", $list);
		    $query = "UPDATE user_cichlids_tanks SET $where='$val' WHERE uid=".intval($this->piVars['tankUid'])." LIMIT 1";
		    mysql_query($query);
		    if (mysql_errno())
		        print mysql_error();
		}
	    }




	    /*
	      This is where we are about to SHOW stuff.
	      If a tankUid is set, we fetch the values from the db.
	      If not, the user wants to post a new tank
	     */
	    if (isset($this->piVars['tankUid'])) {
		$sel = array(
		    "where"     => "fe_user = " . $userid,
		    "max"               => "1",
		);
		$sel['uidInList'] = $this->piVars['tankUid'];
		$sel['pidInList'] = $pid;
		$query = $this->cObj->getQuery("user_cichlids_tanks", $sel);
		$res = mysql_query($query);
		if (mysql_errno())
		    print mysql_error();
		$row = mysql_fetch_assoc($res);
		$obj->start($row);
		$GLOBALS['TSFE']->register['current_tank_category'] = intval($row['category']);

	    } else {
		$newrow = array();
		$newrow['uid'] = 'NEW';
		$obj->start($newrow);
	    }

	    // we now might to display a set of pictures or even the "upload picture" therad.
	    if (isset($this->piVars['select_main_picture'])) {
		$GLOBALS['TSFE']->fe_user->setKey("ses", "select_pic_type", "main");
		return $obj->cObjGetSingle($conf['list_pictures'], $conf['list_pictures.']);
	    }
	    if (isset($this->piVars['select_more_picture'])) {
		$GLOBALS['TSFE']->fe_user->setKey("ses", "select_pic_type", "tank_images");
		return $obj->cObjGetSingle($conf['list_pictures'], $conf['list_pictures.']);
	    }
	    if (isset($this->piVars['select_deco_picture'])) {
		$GLOBALS['TSFE']->fe_user->setKey("ses", "select_pic_type", "deco_images");
		return $obj->cObjGetSingle($conf['list_pictures'], $conf['list_pictures.']);
	    }
	    if (isset($this->piVars['select_tec_picture'])) {
		$GLOBALS['TSFE']->fe_user->setKey("ses", "select_pic_type", "tec_images");
		return $obj->cObjGetSingle($conf['list_pictures'], $conf['list_pictures.']);
	    }

	    if (isset($this->piVars['add_main_picture'])) {
		$GLOBALS['TSFE']->fe_user->setKey("ses", "select_pic_type", "main");
		return $this->edit_or_post_pic($content, $conf['edit_tank_picture.']);
	    }
	    if (isset($this->piVars['add_more_picture'])) {
		$GLOBALS['TSFE']->fe_user->setKey("ses", "select_pic_type", "tank_images");
		return $this->edit_or_post_pic($content, $conf['edit_tank_picture.']);
	    }
	    if (isset($this->piVars['add_deco_picture'])) {
		$GLOBALS['TSFE']->fe_user->setKey("ses", "select_pic_type", "deco_images");
		return $this->edit_or_post_pic($content, $conf['edit_tank_picture.']);
	    }
	    if (isset($this->piVars['add_tec_picture'])) {
		$GLOBALS['TSFE']->fe_user->setKey("ses", "select_pic_type", "tec_images");
		return $this->edit_or_post_pic($content, $conf['edit_tank_picture.']);
	    }


	    if ($userid == 0 && $this->piVars['action'] == "save") {
		// this means we just saved a pic, just generate a link to the picture
		$content .= $obj->cObjGetSingle($conf['uploaded_tank'], $conf['uploaded_tank.']);
	    } else
		$content .= $obj->cObjGetSingle($conf['edit_tank'], $conf['edit_tank.']);
	    return $content;
	}




	function save_picture_from_request($content, $conf, $pid = 0) {
	        if ($GLOBALS['TSFE']->fe_user->user['uid'])
		    $userid = $GLOBALS['TSFE']->fe_user->user['uid'];
	        else
		    $userid = 0;

		$upload = false;

		if ($_FILES['user_cichlids_pi2']['name'][$this->piVars['pictureUid']]) {
		    $upload = true;

		    $path = "user_pics/" . ($userid ? $userid : "anonymous") . "/";
		    if (!is_dir($path))
			mkdir($path);

		    $filename = $_FILES['user_cichlids_pi2']['name'][$this->piVars['pictureUid']];
		    $filename = str_replace(" ", "_", $filename);
		    $filename = str_replace("&", "_and_", $filename);
		    $filename = str_replace("#", "_No.", $filename);
		    $filename = str_replace("[", "-", $filename);
		    $filename = str_replace("]", "-", $filename);
		    $filename = str_replace("'", "", $filename);
        		$filename = trim($filename);
        		$filename = preg_replace('/[^A-Za-z0-9_-]/', '_', $filename);
        		$filename = preg_replace('/[ _][ _]*/', '_', $filename);
		    $filename = strtolower($filename);

		    $i = 0;
		    do {
			$i++;
			$dbfile = $path . sprintf("%s_%s.jpg",
			    substr($filename, 0, 6),
			    substr(md5(sprintf("%d, %d, %s", $userid, $i, $filename)), 0, 10)
			);
			    
		    } while (is_file($dbfile));

		    $origname = $_FILES['user_cichlids_pi2']['name'][$this->piVars['pictureUid']];
		    $tmpfile = $_FILES['user_cichlids_pi2']['tmp_name'][$this->piVars['pictureUid']];

		    $result = 0;
		    $tmp;
		    exec("/usr/bin/identify '$tmpfile'", $tmp, $result);
		    if ($tmp[0] == "") {
			$this->upload_error = "'$origname' is not a valid image file";
			return false;
		    }

		    $result = move_uploaded_file($tmpfile, $dbfile);
		    if (!$result) {
			$this->upload_error = "'$origname' could not be saved due to an internal error.  Please try again later or contact the webmaster";
			return false;
		    }
		    $upload = true;
		}


		// Wenn "NEW", ersetzen, sonst einfach alles speichern
		if ($pid == 0) {
		    switch($this->piVars['type']) {
		        case 'tank':
			    $pid = 29;
		    	break;
			case 'offtopic':
			    $pid = 109;
			break;
			case 'contest':
			    $pid = 131;
			break;
		        default;
			    $pid = 21;
		    }
		}
		if ($this->piVars['pictureUid'] == "NEW") {
		    $query = "INSERT INTO user_cichlids_pictures (pid, tstamp, fe_user, title, image, description) VALUES(
		    		      $pid,
				      UNIX_TIMESTAMP(NOW()),
				      $userid,
		    		      '".mysql_escape_string($this->piVars['title'])."',
		    		      '".mysql_escape_string($dbfile)."',
				      '".mysql_escape_string($this->piVars['descr'])."'
		    		      )
		    ";
		} else {
		    $query = "UPDATE user_cichlids_pictures SET
				      pid=$pid,
				      tstamp = UNIX_TIMESTAMP(NOW()),
		    		      title='".mysql_escape_string($this->piVars['title'])."',
		    		      ". ($upload ? 
					  "image='".mysql_escape_string($dbfile)."',"	:
					  "")
				      ."
				      description='".mysql_escape_string($this->piVars['descr'])."'
				      WHERE fe_user=$userid AND uid=".intval($this->piVars['pictureUid'])."
		    ";
		}

		mysql_query($query);
		if (mysql_errno())
		    print mysql_error();

		if ($this->piVars['pictureUid'] == "NEW")
		    $this->piVars['pictureUid'] = mysql_insert_id();

		// we now need to set the spcies pictures mm relation database; first, we need to delete it
		$query = "DELETE FROM user_cichlids_species_pictures_mm WHERE uid_local = ".$this->piVars['pictureUid'];
		mysql_query($query);
		if (mysql_errno())
		    print mysql_error();
		$query = "INSERT INTO user_cichlids_species_pictures_mm (uid_local, uid_foreign) VALUES (".$this->piVars['pictureUid'].", ".intval($this->piVars['species']).")";
		mysql_query($query);
		if (mysql_errno())
		    print mysql_error();

		$newuid = $this->piVars['pictureUid'];
		// einmal statische url genereiren
		$link = $this->pi_getPageLink(3, '', array('user_cichlids_pi1[picture]' => $newuid));
		cichlids_generatePicture($newuid);
		
		return $this->piVars['pictureUid'];

	}

	function edit_or_post_pic($content, $conf) {

	    $obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use

	    if ($GLOBALS['TSFE']->fe_user->user['uid'])
		$userid = $GLOBALS['TSFE']->fe_user->user['uid'];
	    else
		$userid = 0;

	    $error = false;
	    /* first we might need to save a new pic */
	    if ($this->piVars['pictureUid'] && $this->piVars['action'] == "save") {
		$picuid = $this->save_picture_from_request($content, $conf);
		if (!$picuid) {
		    $error = $this->upload_error;
		}
	    }

	    if ($this->piVars['pictureUid'] && ($this->piVars['pictureUid'] != "NEW")) {

		header("Location: /members/$userid/$userid/photos");
		return;

		$sel = array(
		    "where"     => "fe_user = " . $userid,
		    "max"               => "1",
		);
		$sel['uidInList'] = $this->piVars['pictureUid'];
		$sel['pidInList'] = $conf['picturesPid'];
		$query = $this->cObj->getQuery("user_cichlids_pictures", $sel);
		$res = mysql_query($query);
		if (mysql_errno())
		    print mysql_error();
		$row = mysql_fetch_assoc($res);
		if ($error)
		    $row['error'] = $error;
		$obj->start($row);
	    } else {

		header("Location: /pictures/upload");
		return;

		$newrow = array();
		$newrow['uid'] = 'NEW';
		if ($error) {
		    $newrow['error'] = $error;
		    $newrow['title'] = $this->piVars['title'];
		    $newrow['description'] = $this->piVars['descr'];
		}
		$obj->start($newrow);
	    }

	    if (!$error && $userid == 0 && $this->piVars['action'] == "save") {
		// this means we just saved a pic, just generate a link to the picture
		$content .= $obj->cObjGetSingle($conf['uploaded_picture'], $conf['uploaded_picture.']);
	    } else
		$content .= $obj->cObjGetSingle($conf['edit_picture'], $conf['edit_picture.']);
	    return $content;
	}





	function tanks_listing($content, $conf) {

		$userid = $GLOBALS['TSFE']->fe_user->user['uid'];
		header("Location: /members/$userid/$userid/tanks");
		return;

	    return $this->cObj->cObjGetSingle($conf['tanks_listing'], $conf['tanks_listing.']);
	}

	function pics_listing($content, $conf) {
		$userid = $GLOBALS['TSFE']->fe_user->user['uid'];
		header("Location: /members/$userid/$userid/photos");
		return;

	    return $this->cObj->cObjGetSingle($conf['pics_listing'], $conf['pics_listing.']);
	}

	function species_selectbox($content, $conf) {
	    if ($this->cObj->data['uid'] && $this->cObj->data['uid'] != "NEW") {
		$query = "SELECT  uid_foreign FROM user_cichlids_species_pictures_mm WHERE	user_cichlids_species_pictures_mm.uid_local = ".$this->cObj->data['uid'];
		$res = mysql_query($query);
		$row = mysql_fetch_assoc($res);
		$act = $row['uid_foreign'];
		mysql_free_result($res);
	    }

	    if ($this->cObj->data['uid'] == "NEW" && isset($this->piVars['species']) && $this->piVars['species'] != "") {
		$act = intval($this->piVars['species']);
	    }

	    $query = "SELECT * FROM user_cichlids_species WHERE deleted=0 AND hidden=0 ORDER BY title ASC";
	    $res = mysql_query($query);
	    if (mysql_errno())
		print mysql_error();
	    while ($row = mysql_fetch_assoc($res)) {
		$content .= '<option value="' . $row['uid'] . '" ' . ($act  == $row['uid'] ? 'selected' : '') . '>'.$row['title'].'</option>' . "\n";
	    }
	    
	    return $content;
	}
}



if (defined("TYPO3_MODE") && $TYPO3_CONF_VARS[TYPO3_MODE]["XCLASS"]["ext/user_cichlids/pi2/class.user_cichlids_pi2.php"])	{
	include_once($TYPO3_CONF_VARS[TYPO3_MODE]["XCLASS"]["ext/user_cichlids/pi2/class.user_cichlids_pi2.php"]);
}

?>
