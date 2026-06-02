<?php

require_once("/var/www/html/www-cichlids/libcichlids/libcichlids.php");

require_once(PATH_tslib."class.tslib_pibase.php");
require_once(PATH_t3lib."class.t3lib_div.php");


require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_tslib_pibase.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.gallery_editor.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.gallery_browser.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.picture_viewer.php');

require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_picture.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_feuser.php');

require_once(t3lib_extMgm::extPath('user_cichlids').'banlist.php');

class cichlids_tank extends cichlids_basic_object {
    function cichlids_tank($row) {
	  switch ($row['unit']) {
	      case 'inches':
		  $row['size'] = intval($row['width'] * $row['height'] * $row['depth'] *0.0164*0.264);
		  $row['width_m'] = intval($row['width'] * 2.54);
		  $row['height_m'] = intval($row['height'] * 2.54);
		  $row['depth_m'] = intval($row['depth'] * 2.54);
		  $row['size_m'] = intval($row['size'] * 3.7854);
		  break;
	      case 'centimeters':
		  $row['width_m'] = $row['width'];
		  $row['height_m'] = $row['height'];
		  $row['depth_m'] = $row['depth'];
		  $row['size_m'] = ($row['width_m'] * $row['height_m'] * $row['depth_m']) / 1000;

		  $row['width'] = intval($row['width_m'] / 2.54);
		  $row['height'] = intval($row['height_m'] / 2.54);
		  $row['depth'] = intval($row['depth_m'] / 2.54);
		  $row['size'] = intval($row['size_m'] / 3.7854);
		  break;
	  }
	  parent::cichlids_basic_object($row);
    }
}
class cichlids_profile {
}
class cichlids_genusname {
}
class cichlids_speciesname {
}
class cichlids_category extends cichlids_basic_object {
}


class user_cichlids_pi3 extends cichlids_tslib_pibase {
	var $prefixId = "user_cichlids_pi1";		// Same as class name XXX extra auf pi1 fuer compat mit anderem ding
	var $scriptRelPath = "pi3/class.user_cichlids_pi3.php";	// Path to this script relative to the extension dir.
	var $extKey = "user_cichlids";	// The extension key.
	var $allowCaching = true;
	var $conf = array();


	/* Actions for Web Layer ---------------------------------------------  */

        var $flow = array(
              // from ist 'default', wenn from nicht gesetzt ist.
              'default' => array(
                    // outcome
                    "defaultview"	      =>  "default.php",
              ),
        );
        var $actions = array(
              // actionname => function
        );

	function main($content, $conf) {
	    $classname = $this->cObj->data['select_key'];
	    if ($classname == "")
		$classname = $conf['classname'];
	    if ($classname == "")
		  return "specify classname";
	    $cls = t3lib_div::makeInstanceClassName($classname);
	    if (class_exists ($cls))        {
		$classObj = new $cls;
		if (method_exists($classObj, "main")) {
		    $classObj->cObj = &$this->cObj;
		    return call_user_func(array($classObj, "main"), $content, $conf);
		}
	    }
	    return "not found";
	}

	function controllerDefaultAction() {
	      return "defaultview";
	}

	/* Standard Function (this one is called on the Homepage) ---------- */

	function dispatch($content, $conf) {
	    // Wenn piVars picture gesetzt, ein Picture anzeigen
	    $this->pi_setPiVarDefaults();
	    $this->pi_loadLL();
	    $this->conf = $conf;

	    if ($this->piVars['picture']) {
	          if ($this->piVars['picture'] == -1)
	    	  return $this->show_picture_list();
	          else
	    	  return $this->show_single_picture();
	    }
	    return $this->templating("startseite.php");
	}

	function output_google($content, $conf) {
	    return $this->templating("google.php");
	}

	function logo_stuff($content, $conf) {
	    return $this->templating("logostuff.php");
	}




	/* ----- OLD ------ */
	

	function show_tanks($content, $conf) {
	    /*
	     * Zuerst nachsheen, ob wir gerade schon einen Tank ansehen (showUid).
	     * Wenn ja, einfach anzeigen, wenn nein, nachsehen, ob wir eine Kategorie selektiert haben
	     * Wenn nien, Kateogrien anzeigen,
	     * wenn ja, alle Bilder dieser Kateogrien laden und mit Pagebrowser anzeigen
	     */

	     if ($this->piVars['tank']) {
		  return $this->list_tanks($content, $conf, intval($this->piVars['tank']));
	     } else {
		  /* Hier no cache, weil sonst die unten immer gleich angezeigt werden */
		  if ($this->piVars['user'] == -1)
		      return $this->cObj->cObjGetSingle($conf['list_users'], $conf['list_users.']);
		  if ($this->piVars['category'] == -1)
		      return $this->cObj->cObjGetSingle($conf['list_categories'], $conf['list_categories.']);
		  if ($this->piVars['species'] == -1)
		      return $this->cObj->cObjGetSingle($conf['list_profiles'], $conf['list_profiles.']);
		  return $this->cObj->cObjGetSingle($conf['list_tanks'], $conf['list_tanks.']);
	     }
	}

	function show_pictures($content, $conf) {
	    /*
	     * Zuerst nachsheen, ob wir gerade schon ein Bild anshen (showUid).
	     * Wenn ja, einfach anzeigen, wenn nein, nachsehen, ob wir eine Kategorie selektiert haben
	     * Wenn nien, Kateogrien anzeigen,
	     * wenn ja, alle Bilder dieser Kateogrien laden und mit Pagebrowser anzeigen
	     */
	     if ($this->piVars['picture']) {
		  return $this->list_pictures($content, $conf, intval($this->piVars['picture']));
	     }
	     else {
		  if ($this->piVars['user'] == -1)
		      return $this->cObj->cObjGetSingle($conf['list_users'], $conf['list_users.']);
		  if ($this->piVars['category'] == -1)
		      return $this->cObj->cObjGetSingle($conf['list_categories'], $conf['list_categories.']);
		  if ($this->piVars['species'] == -1)
		      return $this->cObj->cObjGetSingle($conf['list_profiles'], $conf['list_profiles.']);
		  return $this->cObj->cObjGetSingle($conf['list_pictures'], $conf['list_pictures.']);
	     }
	}

	function show_profiles($content, $conf) {
	    /*
	     * Zuerst nachsheen, ob wir gerade schon ein Profile anshen (showUid).
	     * Wenn ja, einfach anzeigen, wenn nein, nachsehen, ob wir eine Kategorie selektiert haben
	     * Wenn nien, Kateogrien anzeigen,
	     * wenn ja, alle Bilder dieser Kateogrien laden und mit Pagebrowser anzeigen
	     */
	     if ($this->piVars['species']) {
		  return $this->list_profiles($content, $conf, intval($this->piVars['species']));
	     } else {
		  if ($this->piVars['genus']) {
		      return $this->cObj->cObjGetSingle($conf['list_profiles'], $conf['list_profiles.']);
		  }
		  if ($this->piVars['category']) {
			    return $this->cObj->cObjGetSingle($conf['list_genus'], $conf['list_genus.']);
		  }
		  return $this->cObj->cObjGetSingle($conf['list_categories'], $conf['list_categories.']);
	     }
	}


	function list_categories($content, $conf) {
	    // if we are showing pictures, we first add a default category for a ll the non-classified pictures
	    if ($this->cObj->data['select_key'] == "pictures") {
		$row = array(
		    "uid"	  => 0,
		    "title"	  => "Unknown or not classified",
		);
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$obj->start($row);
		$content .= $obj->cObjGetSingle($conf['category'], $conf['category.']);
	    }

	    /* Alle Kats selecten */
	    $sel = array(
		"pidInList"   => $conf['catPid'],
		"where"	    => "",
		"orderBy"	    => "title ASC",
	    );
	    $query = $this->cObj->getQuery("user_cichlids_category", $sel);
	    $res = mysql_query($query);
	    while ($row = mysql_fetch_assoc($res)) {
		// keeping "Community" out
		if ($this->cObj->data['select_key'] == "pictures" && $row['uid'] == 7)
		    continue;
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$obj->start($row);
		$content .= $obj->cObjGetSingle($conf['category'], $conf['category.']);
	    }
	    return $content;
	}

	function build_pagebrowser_browse($allrows, $max, $conf, $confwhich) {
	    	$row = array();
	    	$row['results'] = count($allrows);
	    	$row['first'] = 1;
	    	$row['last'] = ceil($row['results'] / $conf['max']);
	    	$row['max'] = $max;
	    	$row['prev'] = intval($this->piVars['page']) > 1 ? intval($this->piVars['page']) - 1 : 1;
	    	$row['next'] = intval($this->piVars['page']) < $row['last'] ? intval($this->piVars['page']) + 1 : $row['last'];
	    	$row['current_page'] = intval($this->piVars['page']) ? intval($this->piVars['page']) : 1;

		$pobj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$pobj->start($row);
		return $pobj->cObjGetSingle($conf[$confwhich], $conf[$confwhich.'.']);
	}	    

	function list_users($content, $conf) {
	    $allrows = array();
	    // if we are showing pictures, we first add a default category for a ll the non-classified pictures
	    if ($this->cObj->data['select_key'] == "pictures") {
		$row = array(
		    "uid"	  => 0,
		    "first_name"  => 'Anonymous',
		);
		$allrows[] = $row;
	    }
	    $sel = array(
		"pidInList"   => $conf['usersPid'],
		"where"	    => "",
		"orderBy"	    => "name ASC",
	    );
	    $query = $this->cObj->getQuery("fe_users", $sel);
	    $res = mysql_query($query);
	    while ($row = mysql_fetch_assoc($res))
		$allrows[] = $row;

	    $displayrows = array();
	    // first defaults
	    if (intval($this->piVars['page']) == 0)
		$this->piVars['page'] = 1;
	    if (intval($conf['max']) == 0)
		$conf['max'] = 50;

	    // error checking
	    if (($this->piVars['page'] - 1) * $conf['max'] > count($allrows))
		$this->piVars['page'] = floor(count($allrows) / $conf['max']);

	    // we need to go through all the pics
	    if ($uid != 0) {
		// if we look at a given pic, we need to find its position
		$tostart = 0;
	    } else {
		$tostart = ($this->piVars['page'] - 1) * $conf['max'];
	    }
	    $shown = 0;
	    $all = count($allrows);
	    $last_shown = 0;
	    for ($n = $tostart; $shown < $conf['max']; $n++) {
		if ($n >= count($allrows))
		    break;
	    
		if ($uid != 0) {
		    // ok, we are looking for exactly ONE pic here
		    if ($allrows[$n]['uid'] == $uid) {
			$displayrows[] = $allrows[$n];
			$shown++;
			$last_shown = $n;
			break;
		    }
		    continue;
		}
		// otherwise we can go further
		$displayrows[] = $allrows[$n];
		$shown++;
	    }

#	    if ($uid == 0) {
#		// now also gneerate the pagebrowser
#	    	$row = array();
#	    	$row['results'] = count($allrows);
#	    	$row['first'] = 1;
#	    	$row['last'] = ceil($row['results'] / $conf['max']);
#	    	$row['max'] = $conf['max'];
#	    	$row['prev'] = intval($this->piVars['page']) > 1 ? intval($this->piVars['page']) - 1 : 1;
#	    	$row['next'] = intval($this->piVars['page']) < $row['last'] ? intval($this->piVars['page']) + 1 : $row['last'];
#	    	$row['current_page'] = intval($this->piVars['page']) ? intval($this->piVars['page']) : 1;
#	    } else {
#		$row = array();
#		$row['current_page'] = $uid;
#		if ($last_shown != 0)
#		    $row['prev'] = $allrows[$last_shown - 1]['uid'];
#		else 
#		    $row['prev'] = $row['current_page'];
#		if ($allrows[$last_shown + 1]['uid'])
#		    $row['next'] = $allrows[$last_shown + 1]['uid'];
#		else 
#		    $row['next'] = $row['current_page'];
#	    }
#	    $pobj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
#	    $pobj->start($row);
#	    $content = $pobj->cObjGetSingle($conf['pagebrowser_top'], $conf['pagebrowser_top.']) . $content;
#	    $content .= $pobj->cObjGetSingle($conf['pagebrowser_bottom'], $conf['pagebrowser_bottom.']);
#	    return $content;
#	}
#
#
#

	    foreach($displayrows as $row) {
		// for each user we fetch the amount of pics
		$picsel = array(
		    "pidInList"	      => $conf['picturesPid'],
		    "selectFields"    => "COUNT(*) as anzahl",
		    "where"	      => "fe_user=".$row['uid'],
		);
		$query = $this->cObj->getQuery("user_cichlids_pictures", $picsel);
		$res = mysql_query($query);
		$countrow = mysql_fetch_assoc($res);
		if ($this->cObj->data['select_key'] == "pictures" && $countrow['anzahl'] == 0)
		    continue;
		$row['number_of_pics'] = $countrow['anzahl'];
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$obj->start($row);
		$content .= $obj->cObjGetSingle($conf['user'], $conf['user.']);
	    }
	    $content = $this->build_pagebrowser_browse($allrows, $conf['max'], $conf, 'pagebrowser_top')
			. $content;
	    return $content;
	}


	function render_user($content, $conf) {
	    $obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
	    $obj->start($GLOBALS['TSFE']->fe_user->user);
	    return $obj->cObjGetSingle($conf['renderObj'], $conf['renderObj.']);
	}


	function list_tanks($content, $conf, $uid = 0) {
	    /* Alle Kats selecten */

	    $sel = array(
		"pidInList"   => $conf['tankPid'],
		"where"	    => "",
		"orderBy"	    => "tstamp DESC",
	    );
	    if ($uid)
		$sel['uidInList'] = $uid;

	    $showall = intval($conf['showall']);
	    if ($conf['fe_user']) {
		if ($conf['fe_user'] == "current")
		    $fe_user = $GLOBALS['TSFE']->fe_user->user['uid'];
		else
		    $fe_user = $conf['fe_user'];
		$sel['where'] = "fe_user = $fe_user";
	    }


	    $allrows = array();
	    $query = $this->cObj->getQuery("user_cichlids_tanks", $sel);
	    $res = mysql_query($query);
	    while ($row = mysql_fetch_assoc($res)) {
		if ($showall == 0 && ($row['title'] == "" || intval($row['image']) == 0))
		    continue;
		$allrows[] = $row;
	    }

	    $displayrows = array();
	    // first defaults
	    if (intval($this->piVars['page']) == 0)
		$this->piVars['page'] = 1;
	    if (intval($conf['max']) == 0)
		$conf['max'] = 10;

	    if ($uid != 0)
		// we are showing a pic, adjust max = 1
		$conf['max'] = 1;

	    // error checking
	    if (($this->piVars['page'] - 1) * $conf['max'] > count($allrows))
		$this->piVars['page'] = floor(count($allrows) / $conf['max']);

	    // we need to go through all the pics
	    if ($uid != 0) {
		// if we look at a given pic, we need to find its position
		$tostart = 0;
	    } else {
		$tostart = ($this->piVars['page'] - 1) * $conf['max'];
	    }
	    $shown = 0;
	    $all = count($allrows);
	    $last_shown = 0;
	    for ($n = $tostart; $shown < $conf['max']; $n++) {
		if ($n >= count($allrows))
		    break;
	    
		if ($uid != 0) {
		    // ok, we are looking for exactly ONE pic here
		    if ($allrows[$n]['uid'] == $uid) {
			$displayrows[] = $allrows[$n];
			$shown++;
			$last_shown = $n;
			break;
		    }
		    continue;
		}
		// otherwise we can go further
		$displayrows[] = $allrows[$n];
		$shown++;
	    }
	    $sel['selectFields'] = "user_cichlids_pictures.*";

	    foreach($displayrows as $row) {

//
//		$sel['uidInList'] = $row['uid'];
//		$query = $this->cObj->getQuery("user_cichlids_pictures", $sel);
//		$res = mysql_query($query);
//		if (mysql_errno())
//		    return $query . "<br>" . mysql_error();
//		$row = mysql_fetch_assoc($res);
//		mysql_free_result($res);
//
//		$GLOBALS['TSFE']->register['current_picture_uid'] = $row['uid'];
//		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
//		$obj->start($row, 'user_cichlids_pictures');
//		$content .= $obj->cObjGetSingle($conf['picture'], $conf['picture.']);
//	    }

//	    return $content;
//	}
//

		switch ($row['unit']) {
		    case 'inches':
			$row['size'] = intval($row['width'] * $row['height'] * $row['depth'] *0.0164*0.264);
			$row['width_m'] = intval($row['width'] * 2.54);
			$row['height_m'] = intval($row['height'] * 2.54);
			$row['depth_m'] = intval($row['depth'] * 2.54);
			$row['size_m'] = intval($row['size'] * 3.7854);
			break;
		    case 'centimeters':
			$row['width_m'] = $row['width'];
			$row['height_m'] = $row['height'];
			$row['depth_m'] = $row['depth'];
			$row['size_m'] = ($row['width_m'] * $row['height_m'] * $row['depth_m']) / 1000;

			$row['width'] = intval($row['width_m'] / 2.54);
			$row['height'] = intval($row['height_m'] / 2.54);
			$row['depth'] = intval($row['depth_m'] / 2.54);
			$row['size'] = intval($row['size_m'] / 3.7854);
			break;
		}
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$obj->start($row);
		$GLOBALS['TSFE']->register['current_tank_uid'] = $row['uid'];
		$content .= $obj->cObjGetSingle($conf['tank'], $conf['tank.']);
		if ($uid != 0) {
		    $this->set_title($row['title'], true);
		}
	    }

	    if ($uid == 0) {
		// now also gneerate the pagebrowser
	    	$row = array();
	    	$row['results'] = count($allrows);
	    	$row['first'] = 1;
	    	$row['last'] = ceil($row['results'] / $conf['max']);
	    	$row['max'] = $conf['max'];
	    	$row['prev'] = intval($this->piVars['page']) > 1 ? intval($this->piVars['page']) - 1 : 1;
	    	$row['next'] = intval($this->piVars['page']) < $row['last'] ? intval($this->piVars['page']) + 1 : $row['last'];
	    	$row['current_page'] = intval($this->piVars['page']) ? intval($this->piVars['page']) : 1;
	    } else {
		$row = array();
		$row['current_page'] = $uid;
		if ($last_shown != 0)
		    $row['prev'] = $allrows[$last_shown - 1]['uid'];
		else 
		    $row['prev'] = $row['current_page'];
		if ($allrows[$last_shown + 1]['uid'])
		    $row['next'] = $allrows[$last_shown + 1]['uid'];
		else 
		    $row['next'] = $row['current_page'];
	    }

	    $pobj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
	    $pobj->start($row);
	    $content = $pobj->cObjGetSingle($conf['pagebrowser_top'], $conf['pagebrowser_top.']) . $content;
	    $content .= $pobj->cObjGetSingle($conf['pagebrowser_bottom'], $conf['pagebrowser_bottom.']);

	    return $content;
	}

	function list_picture_comments($content, $conf) {
	    return $this->list_comments($content, $conf, 1);
	}
	function list_tank_comments($content, $conf) {
	    return $this->list_comments($content, $conf, 2);
	}

	function list_comments($content, $conf, $type) {
	    /* Alle Kats selecten */
	    $sel = array(
		"pidInList"	    =>	$conf['commentsPid'],
		"orderBy"	    =>	"tstamp ASC",
		"where"		    =>  "type = $type AND item=".intval($this->cObj->data['uid']),
	    );
	    $query = $this->cObj->getQuery("user_cichlids_comments", $sel);
	    $res = mysql_query($query);
	    while ($row = mysql_fetch_assoc($res)) {
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$obj->start($row, 'user_cichlids_comments');
		$content .= $obj->cObjGetSingle($conf['comment'], $conf['comment.']);
	    }
	    return $content;
	}

	function post_comment($content, $conf) {
	    switch ($this->piVars['comment_action']) {
		case "post": 

		    if (preg_match('/(shit|fuck|gay|suck|wank)/i', $this->piVars['note'])) {
			die("how lame");
		    }

		    if (! $GLOBALS['TSFE']->fe_user->user['uid']) {
			die("Must be logged in to post comments");
		    }

		    $query = "INSERT INTO user_cichlids_comments (pid, tstamp, crdate, type, item, rating, poster, ip, note, fe_user)
			VALUES	  (
			    ".$conf['commentsPid'].",
			    UNIX_TIMESTAMP(NOW()),
			    UNIX_TIMESTAMP(NOW()),
			    ".($conf['commentsType'] ? $conf['commentsType'] : 0).",
			    ".intval($this->cObj->data['uid']).",
			    ".intval($this->piVars['rating']).",
			    '".mysql_escape_string($this->piVars['poster'])."',
			    '".mysql_escape_string($_SERVER['HTTP_X_FORWARDED_FOR'])."',
			    '".mysql_escape_string($this->piVars['note'])."',
			    ".($GLOBALS['TSFE']->fe_user->user['uid'] ? $GLOBALS['TSFE']->fe_user->user['uid'] : 0)."
			)";
		    mysql_query($query);
		    if (mysql_errno())
			print mysql_error();
		    header("Location: " . $_SERVER['REQUEST_URI']);
		    $uid = mysql_insert_id();
		    $query = "SELECT * from user_cichlids_comments WHERE uid=$uid";
		    $row = mysql_fetch_assoc(mysql_query($query));
		    $obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		    $obj->start($row);
		    return $obj->cObjGetSingle($conf['posted'], $conf['posted.']);
		default:
		    $obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		    $obj->start($row);
		    return $obj->cObjGetSingle($conf['input'], $conf['input.']);
	    }
	}



	function list_genus($content, $conf) {
	    $allrows = array();
	    $sel = array(
		"pidInList"	    => $conf['genusPid'],
		"where"		    => "1 = 1",
		"orderBy"	    => "user_cichlids_genus_names.title ASC",
		"selectFields"	    => "user_cichlids_genus_names.*, user_cichlids_species.uid AS species_uid",
		"leftjoin"	    => "user_cichlids_species ON user_cichlids_genus_names.uid = user_cichlids_species.genus",
	    );
	    // overwrite fe_user and other variables by piVars
	    if ($this->piVars['category'])
		$sel['where'] .= " AND user_cichlids_species.category =" . intval($this->piVars['category']);
	    $query = $this->cObj->getQuery("user_cichlids_genus_names", $sel);
	    $res = mysql_query($query);
	    while ($row = mysql_fetch_assoc($res)) {
		if (!isset($allrows[$row['title']]))
		    $allrows[$row['title']] = $row;
		else
		    $allrows[$row['title']]['species_uid'] .= "," . $row['species_uid'];
	    }


	    // we need to cut off allrows more
	    $tmprows = $allrows;
	    $allrows = array();
	    foreach ($tmprows as $row) {
	        $allrows[] = $row;
	    }

	    $displayrows = array();
	    // first defaults
	    if (intval($this->piVars['page']) == 0)
		$this->piVars['page'] = 1;
	    if (intval($conf['max']) == 0)
		$conf['max'] = 10;

	    if ($uid != 0) // we are showing a pic, adjust max = 1
		$conf['max'] = 1;

	    // error checking
	    if (($this->piVars['page'] - 1) * $conf['max'] > count($allrows))
		$this->piVars['page'] = floor(count($allrows) / $conf['max']);

	    // we need to go through all the pics
	    if ($uid != 0) {
		// if we look at a given pic, we need to find its position
		$tostart = 0;
	    } else {
		$tostart = ($this->piVars['page'] - 1) * $conf['max'];
	    }
	    $shown = 0;
	    $all = count($allrows);
	    $last_shown = 0;
	    for ($n = $tostart; $shown < $conf['max']; $n++) {
		if ($n >= count($allrows))
		    break;
	    
		if ($uid != 0) {
		    // ok, we are looking for exactly ONE pic here
		    if ($allrows[$n]['uid'] == $uid) {
			$displayrows[] = $allrows[$n];
			$shown++;
			$last_shown = $n;
			break;
		    }
		    continue;
		}
		// otherwise we can go further
		$displayrows[] = $allrows[$n];
		$shown++;
	    }

	    //return count($displayrows);

	    foreach($displayrows as $row) {
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$obj->start($row);
		$GLOBALS['TSFE']->register['current_species_uid'] = $row['uid'];
		$content .= $obj->cObjGetSingle($conf['genus'], $conf['genus.']);
	    }


	    if ($uid == 0) {
		// now also gneerate the pagebrowser
	    	$row = array();
	    	$row['results'] = count($allrows);
	    	$row['first'] = 1;
	    	$row['last'] = ceil($row['results'] / $conf['max']);
	    	$row['max'] = $conf['max'];
	    	$row['prev'] = intval($this->piVars['page']) > 1 ? intval($this->piVars['page']) - 1 : 1;
	    	$row['next'] = intval($this->piVars['page']) < $row['last'] ? intval($this->piVars['page']) + 1 : $row['last'];
	    	$row['current_page'] = intval($this->piVars['page']) ? intval($this->piVars['page']) : 1;
	    } else {
		$row = array();
		$row['current_page'] = $uid;
		$row['prev'] = $allrows[$last_shown - 1]['uid'];
		$row['next'] = $allrows[$last_shown + 1]['uid'];
	    }
	    if (!isset($row['first']) || $row['first'] != $row['last']) {
		$pobj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$pobj->start($row);
		$content = $pobj->cObjGetSingle($conf['pagebrowser_top'], $conf['pagebrowser_top.']) . $content;
		$content .= $pobj->cObjGetSingle($conf['pagebrowser_bottom'], $conf['pagebrowser_bottom.']);
	    }
	    return $content;
	}



	function list_pictures($content, $conf, $uid = 0) {

	    /*
	     * First we select ALL images that match category, user, species (for the pagebrowser)
	     * Then we filter out the items we want to display, using max/page
	     * finally, we display them ($displayrows) and generate the pagebrowser
	     */
	    $sel = array(
		"selectFields"	    => "user_cichlids_pictures.uid",
		"pidInList"	    => $conf['picturesPid'],
		"orderBy"	    => "user_cichlids_pictures.tstamp DESC",
		"where"		    => "1=1",
		"leftjoin"	    => "user_cichlids_species_pictures_mm ON user_cichlids_species_pictures_mm.uid_local = user_cichlids_pictures.uid
				        LEFT JOIN user_cichlids_species ON user_cichlids_species.uid = user_cichlids_species_pictures_mm.uid_foreign",
	    );
	    if ($conf['fe_user'] && !$this->piVars['user']) {
		if ($conf['fe_user'] == "current")
		    $fe_user = $GLOBALS['TSFE']->fe_user->user['uid'];
		else
		    $fe_user = $conf['fe_user'];
		$sel['where'] .= " AND fe_user = $fe_user";
	    }
	    // overwrite fe_user and other variables by piVars
	    if (isset($this->piVars['category'])) {
		if (intval($this->piVars['category']))
		    $sel['where'] .= " AND user_cichlids_species.category =" . intval($this->piVars['category']);
		else
		    $sel['where'] .= " AND user_cichlids_species.uid IS NULL";
	    }
	    if (isset($this->piVars['user'])) {
		$sel['where'] .= " AND fe_user=" . intval($this->piVars['user']);
	    }
	    if (isset($this->piVars['species'])) {
		if (intval($this->piVars['species']))
		    $sel['where'] .= " AND user_cichlids_species.uid =" . intval($this->piVars['species']);
		else
		    $sel['where'] .= " AND user_cichlids_species.uid IS NULL";
	    }
	    $query = $this->cObj->getQuery("user_cichlids_pictures", $sel);
	    $res = mysql_query($query);
	    if (mysql_errno())
		return $query . "<br>" . mysql_error();

	    $allrows = array();
	    while ($row = mysql_fetch_assoc($res)) {
		$allrows[] = $row;
	    }

	    $displayrows = array();
	    // first defaults
	    if (intval($this->piVars['page']) == 0)
		$this->piVars['page'] = 1;
	    if (intval($conf['max']) == 0)
		$conf['max'] = 10;

	    if ($uid != 0)
		// we are showing a pic, adjust max = 1
		$conf['max'] = 1;

	    // error checking
	    if (($this->piVars['page'] - 1) * $conf['max'] > count($allrows))
		$this->piVars['page'] = floor(count($allrows) / $conf['max']);

	    // we need to go through all the pics
	    if ($uid != 0) {
		// if we look at a given pic, we need to find its position
		$tostart = 0;
	    } else {
		$tostart = ($this->piVars['page'] - 1) * $conf['max'];
	    }
	    $shown = 0;
	    $all = count($allrows);
	    $last_shown = 0;
	    for ($n = $tostart; $shown < $conf['max']; $n++) {
		if ($n >= count($allrows))
		    break;
	    
		if ($uid != 0) {
		    // ok, we are looking for exactly ONE pic here
		    if ($allrows[$n]['uid'] == $uid) {
			$displayrows[] = $allrows[$n];
			$shown++;
			$last_shown = $n;
			break;
		    }
		    continue;
		}
		// otherwise we can go further
		$displayrows[] = $allrows[$n];
		$shown++;
	    }
	    if (count($displayrows) == 0 && $uid != 0)
		return "Sorry, the requested picture is no longer available.";
	    $sel['selectFields'] = "user_cichlids_pictures.*";
	    $n = 1;
	    foreach($displayrows as $row) {
		$sel['uidInList'] = $row['uid'];
		$query = $this->cObj->getQuery("user_cichlids_pictures", $sel);
		$res = mysql_query($query);
		if (mysql_errno())
		    return $query . "<br>" . mysql_error();
		$row = mysql_fetch_assoc($res);
		mysql_free_result($res);

		$GLOBALS['TSFE']->register['current_picture_uid'] = $row['uid'];
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$obj->start($row, 'user_cichlids_pictures');
		$content .= $obj->cObjGetSingle($conf['picture'], $conf['picture.']);
		if ($uid != 0) {
		    $this->set_title($row['title'], true);
		}

		//
		if ($n++ % $conf['clear'] == 0) {
		    $content .= '<br style="clear: both;"/>';
		}
	    }

	    if ($uid == 0) {
		// now also gneerate the pagebrowser
	    	$row = array();
	    	$row['results'] = count($allrows);
	    	$row['first'] = 1;
	    	$row['last'] = ceil($row['results'] / $conf['max']);
	    	$row['max'] = $conf['max'];
	    	$row['prev'] = intval($this->piVars['page']) > 1 ? intval($this->piVars['page']) - 1 : 1;
	    	$row['next'] = intval($this->piVars['page']) < $row['last'] ? intval($this->piVars['page']) + 1 : $row['last'];
	    	$row['current_page'] = intval($this->piVars['page']) ? intval($this->piVars['page']) : 1;
	    } else {
		$row = array();
		$row['current_page'] = $uid;
		if ($last_shown != 0)
		    $row['prev'] = $allrows[$last_shown - 1]['uid'];
		else 
		    $row['prev'] = $row['current_page'];
		if ($allrows[$last_shown + 1]['uid'])
		    $row['next'] = $allrows[$last_shown + 1]['uid'];
		else 
		    $row['next'] = $row['current_page'];
	    }
	    $pobj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
	    $pobj->start($row);
	    $content = $pobj->cObjGetSingle($conf['pagebrowser_top'], $conf['pagebrowser_top.']) . $content;
	    $content .= $pobj->cObjGetSingle($conf['pagebrowser_bottom'], $conf['pagebrowser_bottom.']);
	    return $content;
	}


	function list_profiles($content, $conf, $uid = 0) {
	    $allrows = array();
	    // if we are showing pictures, we first add a default species for a ll the non-classified pictures
	    if ($this->cObj->data['select_key'] == "pictures") {
		$row = array(
		    "uid"	  => 0,
		    "anon_title"  => 'No species given or need to identify first',
		);
		$allrows[] = $row;
	    }
	    $sel = array(
		"pidInList"	    => $conf['profilesPid'],
		"where"		    => "1 = 1",
		"orderBy"	    => "genus_title, species_title ASC",
		"selectFields"	    => "user_cichlids_species.*, user_cichlids_species_names.title as species_title, user_cichlids_genus_names.title as genus_title",
		"leftjoin"	    => "user_cichlids_genus_names ON user_cichlids_species.genus = user_cichlids_genus_names.uid LEFT JOIN user_cichlids_species_names ON user_cichlids_species.species = user_cichlids_species_names.uid",
	    );
	    // overwrite fe_user and other variables by piVars
	    if ($this->piVars['category'])
		$sel['where'] .= " AND user_cichlids_species.category =" . intval($this->piVars['category']);
	    if ($this->piVars['genus'])
		$sel['where'] .= " AND user_cichlids_genus_names.uid =" . intval($this->piVars['genus']);
	    if (isset($this->piVars['user']))
		$sel['where'] .= " AND fe_user=" . intval($this->piVars['user']);
	    $query = $this->cObj->getQuery("user_cichlids_species", $sel);
	    $res = mysql_query($query);

	    while ($row = mysql_fetch_assoc($res)) {
		$allrows[] = $row;
	    }


	    // we need to cut off allrows more
	    $tmprows = $allrows;
	    $allrows = array();
	    foreach ($tmprows as $row) {
		if ($conf['count_pictures']) {
		    if ($row['uid'] == 0)
			$query = "SELECT COUNT(*) as results FROM user_cichlids_pictures LEFT JOIN user_cichlids_species_pictures_mm ON user_cichlids_pictures.uid = user_cichlids_species_pictures_mm.uid_local WHERE (uid_foreign IS NULL OR uid_foreign=0)";
		    else
			$query = "SELECT COUNT(*) as results FROM user_cichlids_pictures LEFT JOIN user_cichlids_species_pictures_mm ON user_cichlids_pictures.uid = user_cichlids_species_pictures_mm.uid_local WHERE uid_local IS NOT NULL AND uid_foreign=".$row['uid'];
		    $query .= " AND user_cichlids_pictures.pid IN (" . intval($conf['picturesPid']) . ")";
		    $res = mysql_query($query);
		    $countrow = mysql_fetch_assoc($res);
		    mysql_free_result($res);
		    if ($this->cObj->data['select_key'] == "pictures" && $countrow['results'] == 0)
			continue;
		    $row['number_of_pics'] = intval($countrow['results']);
		}	 
		#if ($conf['filter_empty_records'] && $row['number_of_pics'] == 0 && $row['description'] == '' && $row['links'] == '' && $row['gh'] == '') {
		#    continue;
		#}
		$allrows[] = $row;

	    }


	    $displayrows = array();
	    // first defaults
	    if (intval($this->piVars['page']) == 0)
		$this->piVars['page'] = 1;
	    if (intval($conf['max']) == 0)
		$conf['max'] = 10;

	    if ($uid != 0) // we are showing a pic, adjust max = 1
		$conf['max'] = 1;

	    // error checking
	    if (($this->piVars['page'] - 1) * $conf['max'] > count($allrows))
		$this->piVars['page'] = floor(count($allrows) / $conf['max']);

	    // we need to go through all the pics
	    if ($uid != 0) {
		// if we look at a given pic, we need to find its position
		$tostart = 0;
	    } else {
		$tostart = ($this->piVars['page'] - 1) * $conf['max'];
	    }
	    $shown = 0;
	    $all = count($allrows);
	    $last_shown = 0;
	    for ($n = $tostart; $shown < $conf['max']; $n++) {
		if ($n >= count($allrows))
		    break;
	    
		if ($uid != 0) {
		    // ok, we are looking for exactly ONE pic here
		    if ($allrows[$n]['uid'] == $uid) {
			$displayrows[] = $allrows[$n];
			$shown++;
			$last_shown = $n;
			break;
		    }
		    continue;
		}
		// otherwise we can go further
		$displayrows[] = $allrows[$n];
		$shown++;
	    }

	    //return count($displayrows);

	    foreach($displayrows as $row) {
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$obj->start($row);
		$GLOBALS['TSFE']->register['current_species_uid'] = $row['uid'];
		$content .= $obj->cObjGetSingle($conf['species'], $conf['species.']);
	    }


	    if ($uid == 0) {
		// now also gneerate the pagebrowser
	    	$row = array();
	    	$row['results'] = count($allrows);
	    	$row['first'] = 1;
	    	$row['last'] = ceil($row['results'] / $conf['max']);
	    	$row['max'] = $conf['max'];
	    	$row['prev'] = intval($this->piVars['page']) > 1 ? intval($this->piVars['page']) - 1 : 1;
	    	$row['next'] = intval($this->piVars['page']) < $row['last'] ? intval($this->piVars['page']) + 1 : $row['last'];
	    	$row['current_page'] = intval($this->piVars['page']) ? intval($this->piVars['page']) : 1;
	    } else {
		$row = array();
		$row['current_page'] = $uid;
		$row['prev'] = $allrows[$last_shown - 1]['uid'];
		$row['next'] = $allrows[$last_shown + 1]['uid'];
	    }
	    if (!isset($row['first']) || $row['first'] != $row['last']) {
		$pobj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$pobj->start($row);
		$content = $pobj->cObjGetSingle($conf['pagebrowser_top'], $conf['pagebrowser_top.']) . $content;
		$content .= $pobj->cObjGetSingle($conf['pagebrowser_bottom'], $conf['pagebrowser_bottom.']);
	    }
	
	    return $content;
	}

	function make_page_browser($content, $conf) {
	    $data = $this->cObj->data;
	    $out = "";
	    $start = $data['current_page'] - 5;
	    $stop = $data['current_page'] + 5;
	    if ($start < $data['first'])
		$start = $data['first'];
	    if ($stop > $data['last'])
		$stop = $data['last'];

	    for ($i = $start; $i <= $stop; $i++) {
		$data['link_page'] = $i;
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$obj->start($data);
		$out .= $obj->cObjGetSingle($conf['link_page'], $conf['link_page.']);
	    }
	    return $out;
	}

	function edit_area($content, $conf) {
	    switch ($this->piVars['edit_area_action']) {
		case "move_pic":
		    if (isset($this->piVars['move_button'])) {
			switch($this->piVars['type']) {
			    case "tank":
				$pid = 29;
				break;
			    case "fish":
			    default:
				$pid = 21;
			}
			$query = "UPDATE user_cichlids_pictures SET pid=$pid WHERE uid=".intval($this->piVars['picture']);
			mysql_query($query);
			if (mysql_errno())
			    return mysql_error();
			
			$uid = intval($this->piVars['picture']);
			cichlids_generatePicture($uid);
			return "successfully moved pic";
		    } else {
			return $this->cObj->cObjGetSingle($conf['move_pic'], $conf['move_pic.']);
		    }
		case "edit_species":
		    if (isset($this->piVars['edit_species_button'])) {
			// erst vom MM-table alle l&ouml;schen, dann neu einf&uuml;gen
			$query = "DELETE FROM user_cichlids_species_pictures_mm WHERE uid_local=".$this->cObj->data['uid'];
			mysql_query($query);

			if (intval($this->piVars['the_species'])) {
			    $query = "INSERT INTO user_cichlids_species_pictures_mm (uid_local, uid_foreign) VALUES(".intval($this->cObj->data['uid']).", ".intval($this->piVars['the_species']).")";
			    mysql_query($query);
			}
			$uid = intval($this->piVars['picture']);
			cichlids_generatePicture($uid);
			header("Location: /".$this->pi_linkTP_keepPIvars_url(array("picture" => $this->piVars['picture']), 0, 1));
			return "saved species...";
		    } else {
			return $this->cObj->cObjGetSingle($conf['edit_species'], $conf['edit_species.']);
		    }
		default:
		    return "";
	    }
	}

	function delete_picture($content, $conf) {
	    if ($this->piVars['confirmed'] == "yes") {
		$GLOBALS['TSFE']->set_no_cache();
		// erst vom MM-table alle l&ouml;schen, dann neu einf&uuml;gen
		$query = "UPDATE user_cichlids_pictures SET hidden=1 WHERE uid=".intval($this->piVars['picture'])." LIMIT 1";
		mysql_query($query);
		if (mysql_errno())
		    return mysql_error();
		header("Location: /".$this->pi_linkTP_keepPIvars_url(array(), 0, 1, $this->piVars['backPid']));
		return $this->cObj->cObjGetSingle($conf['deleted'], $conf['deleted.']);
	    } else if ($this->piVars['action'] == "confirm") {
		$GLOBALS['TSFE']->set_no_cache();
		$query = "SELECT * from user_cichlids_pictures WHERE uid=".intval($this->piVars['picture']);
		$res = mysql_query($query);
		$row = mysql_fetch_assoc($res);
		mysql_free_result($res);
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$obj->start($row);
		return $obj->cObjGetSingle($conf['confirm'], $conf['confirm.']);
	    } else {
		// einfach das tempalte zeigen
		return $this->cObj->cObjGetSingle($conf['make_link'], $conf['make_link.']);
	    }
	}

	function delete_comment($content, $conf) {
	    if ($this->piVars['confirmed'] == "yes") {
		$GLOBALS['TSFE']->set_no_cache();
		// erst vom MM-table alle l&ouml;schen, dann neu einf&uuml;gen
		$query = "UPDATE user_cichlids_comments SET hidden=1 WHERE uid=".intval($this->piVars['comment'])." LIMIT 1";
		mysql_query($query);
		if (mysql_errno())
		    return mysql_error();
		header("Location: /".$this->pi_linkTP_keepPIvars_url(array("picture" => $this->piVars['picture'], "tank" => $this->piVars['tank']), 0, 1, $this->piVars['backPid']));
		return $this->cObj->cObjGetSingle($conf['deleted'], $conf['deleted.']);
	    } else if ($this->piVars['action'] == "confirm") {
		$GLOBALS['TSFE']->set_no_cache();
		$query = "SELECT * from user_cichlids_comments WHERE uid=".intval($this->piVars['comment']);
		$res = mysql_query($query);
		$row = mysql_fetch_assoc($res);
		mysql_free_result($res);
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$obj->start($row);
		return $obj->cObjGetSingle($conf['confirm'], $conf['confirm.']);
	    } else {
		// einfach das tempalte zeigen
		return $this->cObj->cObjGetSingle($conf['make_link'], $conf['make_link.']);
	    }
	}


	function set_title($title, $indexOverwrite = false) {
	    $GLOBALS['TSFE']->page['title'] .= ": " . $title;
	    if ($indexOverwrite)
		$GLOBALS['TSFE']->indexedDocTitle = $title;
	    else
		$GLOBALS['TSFE']->indexedDocTitle .= ": " . $title;
	}

	function maybe_edit_this($content, $conf) {
		return $this->cObj->cObjGetSingle($conf['cObject'], $conf['cObject.']);
	}

}



if (defined("TYPO3_MODE") && $TYPO3_CONF_VARS[TYPO3_MODE]["XCLASS"]["ext/user_cichlids/pi1/class.user_cichlids_pi1.php"])	{
	include_once($TYPO3_CONF_VARS[TYPO3_MODE]["XCLASS"]["ext/user_cichlids/pi1/class.user_cichlids_pi1.php"]);
}

?>
