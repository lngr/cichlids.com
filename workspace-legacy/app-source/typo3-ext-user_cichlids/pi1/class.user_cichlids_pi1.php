<?php
/***************************************************************
*  Copyright notice
*  
*  (c) 2004  ()
*  All rights reserved
*
*  This script is part of the TYPO3 project. The TYPO3 project is 
*  free software; you can redistribute it and/or modify
*  it under the terms of the GNU General Public License as published by
*  the Free Software Foundation; either version 2 of the License, or
*  (at your option) any later version.
* 
*  The GNU General Public License can be found at
*  http://www.gnu.org/copyleft/gpl.html.
* 
*  This script is distributed in the hope that it will be useful,
*  but WITHOUT ANY WARRANTY; without even the implied warranty of
*  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
*  GNU General Public License for more details.
*
*  This copyright notice MUST APPEAR in all copies of the script!
***************************************************************/
/** 
 * Plugin 'Cichlids.com' for the 'user_cichlids' extension.
 *
 * @author	 <>
 */


require_once(PATH_tslib."class.tslib_pibase.php");

require_once(t3lib_extMgm::extPath('user_cichlids').'banlist.php');

require_once("/var/www/html/www-cichlids/libcichlids/libcichlids.php");

class user_cichlids_pi1 extends tslib_pibase {
	var $prefixId = "user_cichlids_pi1";		// Same as class name
	var $scriptRelPath = "pi1/class.user_cichlids_pi1.php";	// Path to this script relative to the extension dir.
	var $extKey = "user_cichlids";	// The extension key.
	var $allowCaching = true;

	var $picturesPid = 104;

	function main($content,$conf)	{
		$GLOBALS["TSFE"]->set_no_cache();
		$this->conf=$conf;
		$this->pi_setPiVarDefaults();
		$this->pi_loadLL();

		$this->maxresults = 50;

		$select_key = $this->cObj->data['select_key'];
		if ($this->piVars['picture'] && $select_key != "delete_comment" && $select_key != "delete_picture" && $select_key != "suspend_user")
		    $select_key = "pictures";


		if (false && time() % 86400 < 100) {
			// wohl nicht noetig
			$query = "UPDATE    user_cichlids_species
			  LEFT JOIN user_cichlids_genus_names ON user_cichlids_species.genus = user_cichlids_genus_names.uid
			  LEFT JOIN user_cichlids_species_names ON user_cichlids_species.species = user_cichlids_species_names.uid
			  SET	    user_cichlids_species.title = CONCAT(CONCAT(user_cichlids_genus_names.title, ' '), user_cichlids_species_names.title)";
			$res = mysql_query($query);
			if (mysql_errno())
		    		die(mysql_error());

			// wohl nicht noetig
			$query = "UPDATE    user_cichlids_tanks
			  LEFT JOIN fe_users ON user_cichlids_tanks.fe_user = fe_users.uid
			  SET	    user_cichlids_tanks.realurltitle = CONCAT(CONCAT(fe_users.name, ' '), user_cichlids_tanks.title)";
			$res = mysql_query($query);
			if (mysql_errno())
		    	print mysql_error();
		}

		// we save the frontend_user_id in a register
		$GLOBALS['TSFE']->register['frontend_user_id'] = intval($GLOBALS['TSFE']->fe_user->user['uid']);

                require_once (t3lib_extMgm::extPath('xajax') . 'class.tx_xajax.php');
                $this->xajax = t3lib_div::makeInstance('tx_xajax');
                $this->xajax->decodeUTF8InputOn();
                $this->xajax->setCharEncoding('utf-8');
                $this->xajax->setWrapperPrefix($this->prefixId);
                $this->xajax->statusMessagesOn();
                $this->xajax->debugOff();
                // $this->xajax->debugOn();
                $this->xajax->registerFunction(array('reportPicture', &$this, 'reportPictureAction'));
                $this->xajax->registerFunction(array('rateComment', &$this, 'rateCommentAction'));

                $this->xajax->processRequests();
                $GLOBALS['TSFE']->additionalHeaderData[$this->prefixId] = $this->xajax->getJavascript(t3lib_extMgm::siteRelPath('xajax'));

		switch($select_key) {
		    case "tanks":
			return $this->show_tanks($content, $conf['tanks.']);
		    case "list_species":
			return $this->list_species($content, $conf);
		    case "profiles":
			return $this->show_profiles($content, $conf['profiles.']);
		    case "pictures":
			return $this->show_pictures($content, $conf['pictures.']);
		    case "delete_comment":
			return $this->delete_comment($content, $conf['delete_comment.']);
		    case "delete_picture":
			return $this->delete_picture($content, $conf['delete_picture.']);
		    case "suspend_user":
			return $this->suspend_user($content, $conf['suspend_user.']);
		}
		
		return $this->pi_wrapInBaseClass($content);
	}

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
		  $this->allowCaching = false;
		  return $this->show_single_picture($this->piVars['picture']);
	     }
	     else {
		  if ($this->piVars['user'] == -1)
		      return $this->list_users($content, $conf);
		  return $this->list_pictures($content, $conf);
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

	function list_species($content, $conf) {
	    if (intval($conf['max']) == 0)
		$conf['max'] = 50;
	    if (intval($this->piVars['page']) == 0)
		$this->piVars['page'] = 1;
	    $max = $conf['max'];
	    $query = "SELECT species.*, COUNT(pictures.uid) as number_of_pics
		      FROM user_cichlids_species species,
			   user_cichlids_pictures pictures,
			   user_cichlids_species_pictures_mm mm
		      WHERE species.uid = mm.uid_foreign AND pictures.uid = mm.uid_local
		      GrOUP BY species.uid
		      HAVING number_of_pics > 0
		      ORDER BY species.title ASC
	";
	    $res = mysql_query($query);
	    $anzahl = mysql_num_rows($res);
	    $page = intval($this->piVars['page']);

	    $start = ($page - 1) * $max;
	    $query .= "\nLIMIT $start,$max";

	    $res = mysql_query($query);
	    if (mysql_errno()) print mysql_error();


	    while($row = mysql_fetch_assoc($res)) {
		$overwrite = array("page" => $page);
		$link = $this->pi_linkTP_keepPIvars_url(array("species" => $row['uid']), 0, 1);
		$params = array(
		  "user_cichlids_pi1[species]" => $row['uid'],
		);
		$link = $this->pi_getPageLink(104, "", $params);
		$content .= '<a href="'.$link.'">'.$row['title'].' (' . $row['number_of_pics'] . ' picture'.($row['number_of_pics'] > 1 ?  "s" : "") .')</a><br>'."\n";
	    }

	    $browser = $this->build_page_browser($anzahl, $conf['max'], intval($this->piVars['page']));
	    $content = $browser . $content . $browser;
	    return $content;
	}


	function link_to_page($page) {
	    $overwrite = array("page" => $page);
	    $link = $this->pi_linkTP_keepPIvars_url($overwrite, 1, 0);
	    return '<a href="'.$link.'">';
	}

	function build_page_browser($total, $perpage, $currentpage) {

	    $first = 1;
	    $last = floor(($total - 1) / $perpage) + 1;
	    $current = $currentpage;

	  $out = '
<div class="pagebrowser"
    style="
        clear: both;
        font-size: 8pt;
        margin-bottom: 10px;
        margin-top: 10px;

    "
    >
    <table border="0" cellspacing="0" cellpadding="0" width="100%" class="pagebrowser_table">
        <tr>
            <td nowrap valign="top" align="left">';
            if ($first != $current) {
		$out .= $this->link_to_page($first);
		    $out .= '<img src="/fileadmin/nav_first.gif" border="0"></a>';
		$out .= $this->link_to_page($current - 1);
		    $out .= '<img src="/fileadmin/nav_prev.gif" border="0"></a>';
	    }
            $out .= '</td><td valign="top" align="center" > Page '.$current.' of '.$last.'
            </td>
            <td nowrap valign="top" align="right" >';
	    if ($last != $current) {
		$out .= $this->link_to_page($current + 1);
		    $out .= '<img src="/fileadmin/nav_next.gif" border="0"></a>';
                $out .= $this->link_to_page($last);
		$out .= '<img src="/fileadmin/nav_last.gif" border="0"></a>';
	    }
	    $out .= '</td>
            </td>
        </tr>
        <tr><td colspan="3" valign="top" align="center" class="pagebrowser_allpages">';
                for($n = max($current - 5, 1); $n <= min($current + 5, $last); $n++) {
                    if ($n == $current)
                        $out .= '<div class="pagebrowser_page_current"><a name="pagebrowser_page_current_mark">'.$n.'</a></div>&nbsp;';
		    else
                        $out .= '<div class="pagebrowser_page">' . $this->link_to_page($n) . $n . '</a></div>&nbsp;';
		}
	    $out .= '
        </td></tr>
    </table>
</div>
';
		return "$out";
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
	    $content = $this->build_page_browser($allrows, $conf['max'], intval($this->piVars['page'])) . $content;
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
	    if ($uid) {
		$sel['uidInList'] = $uid;
		header("HTTP/1.0 301 Permanent Redirect");
     		header("Location: /tanks/details/$uid");
		return;
	    }

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

	    // XXX hack to support the FORCE command
	    if ($uid) {
		$query = "SELECT * from user_cichlids_tanks WHERE uid=$uid";
		if (!isset($this->piVars['force'])) $query .= " AND hidden=0 AND deleted=0";
	    }



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
	    $browser = $this->build_page_browser(count($allrows), $conf['max'], intval($this->piVars['page']));
	    $content = $browser . $content . $browser;
	    return $content;
	}

	function list_picture_comments($content, $conf) {
	    return $this->list_comments($content, $conf, 1);
	}
	function list_tank_comments($content, $conf) {
	    return $this->list_comments($content, $conf, 2);
	}

	function list_comments($content, $conf, $type) {
	    $fe_user = $GLOBALS['TSFE']->fe_user->user['uid'];
	    $fe_user_row = $GLOBALS['TSFE']->fe_user->user;
	    $groups = preg_split("/,/", $fe_user_row['usergroup']);
	    $is_mod = in_array(3, $groups);

	    $item = intval($this->cObj->data['uid']);
	    $cquery = "SELECT * FROM user_cichlids_comments WHERE type=$type AND item=$item ";
	    if (!$is_mod && !isset($this->piVars['force'])) $cquery .= " AND hidden=0 AND deleted=0 ";
	    $cquery .= " ORDER BY tstamp ASC";
	    $cres = mysql_query($cquery);
	    while ($crow = mysql_fetch_assoc($cres)) {
		$content .= $this->output_comment("", array(), $crow);
	    }
	    return $content;
	}

	function post_comment($content, $conf) {
	    switch ($this->piVars['comment_action']) {
		case "post": 

		    if (preg_match('/(shit|fuck|gay|wank)/i', $this->piVars['note'])) {
			die("This comment was caught by our spam/insult filter, please check your post.");
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
			die(mysql_error());
		    $uid = mysql_insert_id();
		    $query = "SELECT * from user_cichlids_comments WHERE uid=$uid";
		    $row = mysql_fetch_assoc(mysql_query($query));
		    $obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		    $obj->start($row);
		    $content = $obj->cObjGetSingle($conf['posted'], $conf['posted.']);
		    if ($GLOBALS['TSFE']->fe_user->user['uid'] == 8851) { // 8851
			$GLOBALS['TSFE']->fe_user->logoff();
		    }
		    // redirect mich auf startseite wegen mass
		    if ($GLOBALS['TSFE']->fe_user->user['uid'] == 23 && $this->piVars['comment_action_auto'] == 'true') {
			header("Location: /");
		    } else
		    	header("Location: " . $_SERVER['REQUEST_URI']);
		    return $content;
		default:
		    $user = intval($GLOBALS['TSFE']->fe_user->user['uid']);
		    if (false && $user == 23) {
			$GLOBALS["TSFE"]->set_no_cache();
			return "Sorry, you are not allowed to comment to this picture.";
		    }
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

	function show_single_picture($uid = 0) {
	    $uid = intval($uid);
	    $GLOBALS["TSFE"]->set_no_cache();

	    if ($uid <= 0) {
		return "No such picture.";
	    }

	    // fetch
	    $query = "SELECT * from user_cichlids_pictures WHERE uid=$uid";
	    if (!isset($this->piVars['force'])) $query .= " AND hidden=0 AND deleted=0";
	    $res = mysql_query($query);
	    if (mysql_errno())
		return $query . "<br>" . mysql_error();
	    if (mysql_num_rows($res) == 0) {
		header('HTTP/1.0 404 Not Found');
		return "This picture is no longer available...";
	    }
	    $row = mysql_fetch_assoc($res);

	    // fetch id of next
	    $nextid = 0;
	    $nextquery = "SELECT * from user_cichlids_pictures WHERE uid > $uid AND hidden=0 AND deleted=0
			ORDER BY uid ASC LIMIT 0,1";
	    $res = mysql_query($nextquery);
	    if (mysql_errno())
		return $query . "<br>" . mysql_error();
	    if (mysql_num_rows($res) > 0) {
	    	$next = mysql_fetch_assoc($res);
		$nextid = $next['uid'];
	    }

	    // fetch id of prev
	    $previd = 0;
	    $prevquery = "SELECT * from user_cichlids_pictures WHERE uid < $uid AND hidden=0 AND deleted=0
			ORDER BY uid DESC LIMIT 0,1";
	    $res = mysql_query($prevquery);
	    if (mysql_errno())
		return $query . "<br>" . mysql_error();
	    if (mysql_num_rows($res) > 0) {
	    	$prev = mysql_fetch_assoc($res);
		$previd = $prev['uid'];
	    }

  	    $nexturl = 'http://www.cichlids.com/' . $this->pi_linkTP_keepPIvars_url(array('picture' => $nextid), 0, 1);
  	    $prevurl = 'http://www.cichlids.com/' . $this->pi_linkTP_keepPIvars_url(array('picture' => $previd), 0, 1);


	    // update views
	    $viewq = "UPDATE user_cichlids_pictures SET views=views + 1 WHERE uid=$uid";
	    $viewres = mysql_query($viewq);
	    $row['views']++;


	    $GLOBALS['TSFE']->register['current_picture_uid'] = $row['uid'];
	    $GLOBALS['TSFE']->page['title'] = $row['title'];
            $GLOBALS['TSFE']->indexedDocTitle = $row['title'];

	    // muss hier dirn sein, um z.B. die Anzahl der Views in den kleinen Previews zu aktualisieren
	    cichlids_generatePicture($uid);

	    $postcommentconf = array(
	      "commentsPid" => 52,
	      "commentsType" => 1,
	    );
	    $this->cObj->data = $row;
	    $this->post_comment("", $postcommentconf);

	    ob_start();

	    // XXX offtopic / species / edit area
	    $pobj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
	    $pobj->start($row);

	    echo $pobj->cObjGetSingle($this->conf['EDIT_AREA'], $this->conf['EDIT_AREA.']);

	    $sq = "SELECT * FROM user_cichlids_species_pictures_mm mm LEFT JOIN 
		  user_cichlids_species spectable ON mm.uid_foreign = spectable.uid WHERE mm.uid_local=$uid";
	    $sr = mysql_query($sq);
	    if (mysql_errno()) die(mysql_error());
	    if(mysql_num_rows($sr) > 0) {
		$srow = mysql_fetch_assoc($sr);
		$species = $srow;
		$row['species'] = $species['uid'];
		$specname = $species['title'];
	    } else
		$specname = "";
	$imagetitle = htmlentities($row['title']);
?>

<div style="
    text-align: center;
">
<div id="single_picture_area">
  <div class="the_image"><a title="<?=htmlentities($specname);?>: <?=htmlentities($row['title']);?>" href="/uploads/tx_usercichlids/<?=$row['image'];?>"><img src="/p/<?=cichlids_getImageFilename($uid, $row['image'], 450, 600, false);?>" alt="<?=$imagetitle;?>" width="450" border="0"></a></div>



  <div class="the_title"><?=htmlentities($row['title']);?></div>
  <? if($row['species'] > 0): 
      $params = array( "user_cichlids_pi1[species]" => $species['uid']);
      $link = $this->pi_getPageLink(104, "", $params);
      ?><a href="<?=$link;?>"><?=$species['title'];?></a>
  <? endif; ?>
  <? if ($row['pid'] == 109): ?>
    (posted as offtopic)
  <? endif; ?>
  <? if ($row['pid'] == 131): ?>
    (photo contest submission)
  <? endif; ?>
<div style="margin-top: 15px;"><?=nl2br(strip_tags($row['description'])); ?></div>
  <table border="0" width="80%" align="center" cellpadding="2" style="border: 1px solid #AAAAAA; margin-top: 10px; margin-bottom: 10px;">
    <tr>
        <td width="50%">posted by: <?
		$params = array( "user_cichlids_pi1[user]" => $row['fe_user']);
		$link = $this->pi_getPageLink(104, "", $params);
		echo '<a href="'.$link.'">'.cichlids_getUsername($row['fe_user']).'</a>';
	?></td>
        <td width="50%">
        <div style="float: right; text-align: center;">
<form onSubmit="return false;" method="POST" enctype="multipart/form-data" style="display: inline;">
<input type="hidden" name="user_cichlids_pi1[picture]" value="<?=$row['uid'];?>">
<input type="hidden" name="user_cichlids_pi1[reason]">
<input onClick="val = prompt('If you want to report this picture, please give a short reason.   Thank you!', ''); if (val) { this.form['user_cichlids_pi1[reason]'].value = val; user_cichlids_pi1reportPicture(xajax.getFormValues(this.form)); }"
  type="image" value="report" title="Report as inappropriate or copyright theft"
  src="/fileadmin/delete.gif" width="20" height="20"
/>
<br>
<div style="font-size: 6pt;">(report)</div>
</form>
</div>
<?php 
	$thepicfile = $row['image'];
        $exif_make = exif_read_data ( $thepicfile ,'IFD0' ,0 ); 

        $exif_make = exif_read_data ( $thepicfile ,'IFD0' ,0 ); 
        $emake = $exif_make['Make']; 
        
        $exif_model = exif_read_data ( $thepicfile ,'IFD0' ,0 ); 
        $emodel = $exif_model['Model']; 
        
        $exif_exposuretime = exif_read_data ( $thepicfile ,'EXIF' ,0 ); 
        $eexposuretime = $exif_exposuretime['ExposureTime']; 
        
        $exif_fnumber = exif_read_data ( $thepicfile ,'EXIF' ,0 ); 
        $efnumber = $exif_fnumber['FNumber']; 

        $exif_iso = exif_read_data ( $thepicfile ,'EXIF' ,0 ); 
        $eiso = $exif_iso['ISOSpeedRatings']; 
                
        $exif_date = exif_read_data ( $thepicfile ,'IFD0' ,0 ); 
        $edate = $exif_date['DateTime']; 
?>
	<a id="pictureTooltip" title="<?
		if ($emake != "" || $emodel != "")
			print "Camera $emake / $emodel. ";
		if ($eexposuretime != "")
			print "Exposure: $eexposuretime s. ";
		if ($efnumber != "")
			print "Shutter: f/$efnumber. ";
		if ($eiso != "")
			print "ISO $eiso. ";
	?>">
        <?=date("M jS, Y", $row['tstamp']); ?> </a>
        </td>
    </tr>
    <tr>
        <td width="50%">
	Views:  <?=$row['views'];?>
</td>
	<? if($row['rating_count'] >= 5): ?>
        <td width="50%">Rated: <?=cichlids_getRatingStars($row['rating']);?> (<?=$row['rating_count'];?> votes)</td>
	<? else: ?>
	  <td>&nbsp;</td>
	<? endif; ?>
    </td>
    </tr>
<!--tr><td colspan="2"><script src="http://connect.facebook.net/en_US/all.js#xfbml=1"></script><fb:like layout="button_count" show_faces="false" width="200"></fb:like></td></tr-->
  </table>
</div>
</div>

<script type="text/javascript"><!--

function toggleLayer( whichLayer )
{
  var elem, vis;
  if( document.getElementById ) // this is the way the standards work
    elem = document.getElementById( whichLayer );
  else if( document.all ) // this is the way old msie versions work
      elem = document.all[whichLayer];
  else if( document.layers ) // this is the way nn4 works
    elem = document.layers[whichLayer];
  vis = elem.style;
  // if the style.display value is blank we try to figure it out here
  if(vis.display==''&&elem.offsetWidth!=undefined&&elem.offsetHeight!=undefined)
    vis.display = (elem.offsetWidth!=0&&elem.offsetHeight!=0)?'block':'none';
  vis.display = (vis.display==''||vis.display=='block')?'none':'block';
}

function showComment(comment) {
    toggleLayer('comment-content-' + comment);
    toggleLayer('show-comment-' + comment);
}

//--></script>

<?
    $fe_user = intval($GLOBALS['TSFE']->fe_user->user['uid']);
    $fe_user_row = $GLOBALS['TSFE']->fe_user->user;
    $groups = preg_split("/,/", $fe_user_row['usergroup']);
    $is_mod = in_array(3, $groups);
?>

<? if ($fe_user == 0 && false): ?>
<div style="text-align: center;">
<script type="text/javascript"><!--
google_ad_client = "ca-pub-2393694403529430";
/* Single Picture Rect new */
google_ad_slot = "1191658228";
google_ad_width = 300;
google_ad_height = 250;
//-->
</script>
<script type="text/javascript"
src="http://pagead2.googlesyndication.com/pagead/show_ads.js">
</script>
</div>
<!--div style="text-align: center; font-size: 8pt; margin-top: 5px; margin-bottom: 10px;">
(Make this ad go away by <a href="/control/login.html">logging in</a>.)
</div-->
<? endif; ?>

<h2>Visitor Comments</h2>

<?


    $cquery = "SELECT * FROM user_cichlids_comments WHERE pid=52 AND type=1 AND item=$uid ";
    if (!$is_mod && !isset($this->piVars['force'])) $cquery .= " AND hidden=0 AND deleted=0 ";
    $cquery .= " ORDER BY tstamp ASC";
    $cres = mysql_query($cquery);
    while ($crow = mysql_fetch_assoc($cres)) {
	if($fe_user ==  1579 && $crow['fe_user'] == 3584) continue; // Wayne nicht Roger
	echo $this->output_comment("", array(), $crow);
    }
?>

<h2>Post a comment</h2>
<a name="postcomment"></a>

<?
  $url = $this->pi_linkTP_keepPIvars_url(array('picture' => $uid), 0, 1);
  $fe_user = $GLOBALS['TSFE']->fe_user->user['uid'];
  if ($fe_user > 0): 
?>
<form method="POST" action="<?=$url;?>">
    <input type="hidden" name="user_cichlids_pi1[comment_action]" value="post">
    <table border="0">
  <tr><td valign="top">Your rating:</td><td>
      <input type="radio" name="user_cichlids_pi1[rating]" value="5" id="rate_com_5"><label for="rate_com_5"><img src="/fileadmin/smile5.gif"> Excellent </label><br>
      <input type="radio" name="user_cichlids_pi1[rating]" value="4" id="rate_com_4"><label for="rate_com_4"><img src="/fileadmin/smile4.gif"> Good</label><br>
      <input type="radio" name="user_cichlids_pi1[rating]" value="3" id="rate_com_3"><label for="rate_com_3"><img src="/fileadmin/smile3.gif"> Moderate</label><br>
      <input type="radio" name="user_cichlids_pi1[rating]" value="2" id="rate_com_2"><label for="rate_com_2"><img src="/fileadmin/smile2.gif"> Poor</label><br>
      <input type="radio" name="user_cichlids_pi1[rating]" value="1" id="rate_com_1"><label for="rate_com_1"><img src="/fileadmin/smile1.gif"> Bad</label><br>
  </td></tr>
  <tr><td valign="top">Your comment:</td><td>
      <textarea rows="5" cols="40" name="user_cichlids_pi1[note]"></textarea>
  </td></tr>
  <tr><td>&nbsp;</td><td><input type="submit" value="Post comment!"></td></tr>

    </table>
</form>

<? else: ?>
You must be logged in to post comments.  <!-- Please <a href="/control/login.html">click here to login.</a> -->
<?

if (true) { //
	$recs = array(139, 138, 141, 140, 11);
	$cObj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
	$conf = array(
		"source" => join(",", $recs),
		"tables" => "tt_content",
	);
	print $cObj->cObjGetSingle("RECORDS", $conf);
}
?>

<? endif; ?>


<SCRIPT src="http://www.openjs.com/scripts/events/keyboard_shortcuts/shortcut.js"></SCRIPT>
<SCRIPT>
var options = { "disable_in_input":true };
shortcut.add("Left",
	function() {
<?php if ($previd > 0): ?>
		window.location = '<?php echo $prevurl;?>';
<?php else: ?>
		alert('Already reached the first image.');
<?php endif; ?>
	}, options);
shortcut.add("Right",
	function() {
<?php if ($nextid > 0): ?>
		window.location = '<?php echo $nexturl;?>';
<?php else: ?>
		alert('Already reached the latest image.');
<?php endif; ?>
	}, options);
</SCRIPT>
<?

	    $alex_user = intval($GLOBALS['TSFE']->fe_user->user['uid']);
	    if ($alex_user == 23) {

print '
<form method="POST" name="QuickrateForm">
    <input type="hidden" name="user_cichlids_pi1[comment_action]" value="post">
    <input type="hidden" name="user_cichlids_pi1[comment_action_auto]" value="true">
    <input type="hidden" name="user_cichlids_pi1[rating]" value="5">
    <input type="submit" value="rate5" tabindex="1" accesskey="r">
</form>
';
print '
<SCRIPT>
shortcut.add("r",function() {
	document.QuickrateForm.submit();
}, {
"disable_in_input":true,
});
</SCRIPT>
';


}


	    $content = ob_get_contents();
	    ob_end_clean();

	    return $content;
	}



	function list_pictures($content, $conf, $uid = 0) {
		
	    // first defaults
	    if (intval($this->piVars['page']) == 0)
		$this->piVars['page'] = 1;

	    if (intval($conf['max']) == 0)
		$conf['max'] = 12;

	    if ($uid != 0)
		// we are showing a pic, adjust max = 1
		$conf['max'] = 1;

	    /* single bild */
	    if ($uid != 0) {
		$sel = array(
		    "selectFields"	    => "user_cichlids_pictures.*",
		    "pidInList"	    => $conf['picturesPid'],
		    "where"	    => " user_cichlids_pictures.uid=$uid",
		);
	    } else {

		$sel = array(
		    "selectFields"	    => "user_cichlids_pictures.*",
		    "pidInList"	    =>	  $conf['picturesPid'],
		    "orderBy"	    =>	  "user_cichlids_pictures.tstamp DESC",
		    "where"		    => "1=1",
		    "max"	    =>	  $conf['max'],
		    "begin"	    =>	  ($this->piVars['page'] - 1) * $conf['max'],
		);

		if ($conf['fe_user'] && !$this->piVars['user']) {
		    if ($conf['fe_user'] == "current")
			$fe_user = $GLOBALS['TSFE']->fe_user->user['uid'];
		    else
			$fe_user = $conf['fe_user'];
		    $sel['where'] .= " AND fe_user = $fe_user";
		}
		if (isset($this->piVars['user'])) {
		    $sel['where'] .= " AND fe_user=" . intval($this->piVars['user']);

		 	header("HTTP/1.0 301 Permanent Redirect");
     			header("Location: /members/$uid/$uid/photos");
			return;
		}

		if (isset($this->piVars['category']) || isset($this->piVars['species'])) {
	
		    $sel["leftjoin"] = "user_cichlids_species_pictures_mm ON user_cichlids_species_pictures_mm.uid_local = user_cichlids_pictures.uid
				        LEFT JOIN user_cichlids_species ON user_cichlids_species.uid = user_cichlids_species_pictures_mm.uid_foreign";

		    // overwrite fe_user and other variables by piVars
		    if (isset($this->piVars['category'])) {
			if (intval($this->piVars['category']))
			    $sel['where'] .= " AND user_cichlids_species.category =" . intval($this->piVars['category']);
			else
			    $sel['where'] .= " AND user_cichlids_species.uid IS NULL";
		    }
		    if (isset($this->piVars['species'])) {
			if (intval($this->piVars['species']))
			    $sel['where'] .= " AND user_cichlids_species.uid =" . intval($this->piVars['species']);
			else
			    $sel['where'] .= " AND user_cichlids_species.uid IS NULL";
		    }
		}
	    }
	    $query = $this->cObj->getQuery("user_cichlids_pictures", $sel);
	    $res = mysql_query($query);
	    if (mysql_errno())
		return $query . "<br>" . mysql_error();

	    $displayrows = array();
	    while ($row = mysql_fetch_assoc($res)) {
		$displayrows[] = $row;
	    }
	    if (count($displayrows) == 0 && $uid != 0)
		return "Sorry, the requested picture is no longer available.";

	    if ($uid != 0) {
		// update views
		$GLOBALS["TSFE"]->set_no_cache();
		$query = "UPDATE user_cichlids_pictures SET views=views + 1 WHERE uid=$uid";
		$res = mysql_query($query);
		if (mysql_errno())
		    return $query . "<br>" . mysql_error();
		cichlids_generatePicture($uid);
		$displayrows[0]['views']++;
	    }

	    $sel['selectFields'] = "user_cichlids_pictures.*";
	    $n = 1;
	    foreach($displayrows as $row) {
		if ($uid != 0 || $conf['fe_user'] == "current") {
		    $GLOBALS['TSFE']->register['current_picture_uid'] = $row['uid'];
		    $obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		    $obj->start($row, 'user_cichlids_pictures');
		    $content .= $obj->cObjGetSingle($conf['picture'], $conf['picture.']);
		    if ($uid != 0) {
		        $this->set_title($row['title'], true);
		    }
		} else {
		    ob_start();
		    $staticfile = cichlids_getStaticHtmlPicture($row['uid'], "listing_old");
		    if (!file_exists($staticfile)) {
			  $curuid = $row['uid'];
			  cichlids_generatePicture($curuid);
		    }
		    if (file_exists($staticfile))
			  include($staticfile);
		    $out = ob_get_contents();
		    ob_end_clean();
		    $content .= $out;
		}

		//
		if (isset($conf['clear']) && $n++ % $conf['clear'] == 0) {
		    $content .= '<br style="clear: both;"/>';
		}
	    }

	    if ($uid == 0) {
		unset($sel['max']);
		unset($sel['begin']);
		$sel['selectFields'] = "COUNT(user_cichlids_pictures.uid) count";
		$query = $this->cObj->getQuery("user_cichlids_pictures", $sel);
		$res = mysql_query($query);
		$bla = mysql_fetch_assoc($res);
	    	$current_page = intval($this->piVars['page']) ? intval($this->piVars['page']) : 1;
		$pagebrowser = $this->build_page_browser($bla['count'], $conf['max'], $current_page);
	    } else 
		$pagebrowser = "";

	    return $pagebrowser . $content . $pagebrowser;
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
			    case "offtopic":
				$pid = 109;
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
	    $fe_user = $GLOBALS['TSFE']->fe_user->user;
	    $fe_uid = $fe_user['uid'];
	    $groups = preg_split("/,/", $fe_user['usergroup']);
	    $is_mod = in_array(3, $groups);
	    if(!$is_mod) die("Unauthorized deletion");

	    if ($this->piVars['confirmed'] == "yes" &&
		isset($this->piVars['reason']) && $this->piVars['reason'] != '') {
		$GLOBALS['TSFE']->set_no_cache();
		$reason = mysql_escape_string($this->piVars['reason']);
		// erst vom MM-table alle l&ouml;schen, dann neu einf&uuml;gen
		$query = "UPDATE user_cichlids_pictures SET hidden=1,
				delete_tstamp=UNIX_TIMESTAMP(NOW()),
				delete_reason='$reason',
				delete_user=$fe_uid
		    WHERE uid=".intval($this->piVars['picture'])." LIMIT 1";
		mysql_query($query);
		if (mysql_errno())
		    return mysql_error();
		header("Location: /browse.html");
		// ".$this->pi_linkTP_keepPIvars_url(array(), 0, 1, $this->piVars['backPid']));
		return $this->cObj->cObjGetSingle($conf['deleted'], $conf['deleted.']);
	    } else if ($this->piVars['action'] == "confirm") {
		if (isset($this->piVars['reason']) && $this->piVars['reason'] == '') 
			print '<div style="margin: 20px; border: 10px solid red; padding: 20px; font-weight: bold;">Please give a reason why you want to delete the picture.</div>';
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
	    $fe_user = $GLOBALS['TSFE']->fe_user->user;
	    $fe_uid = $fe_user['uid'];
	    $groups = preg_split("/,/", $fe_user['usergroup']);
	    $is_mod = in_array(3, $groups);
	    if(!$is_mod) die("Unauthorized deletion");

	    if ($this->piVars['confirmed'] == "yes" &&
		isset($this->piVars['reason']) && $this->piVars['reason'] != '') {
		$GLOBALS['TSFE']->set_no_cache();
		$reason = mysql_escape_string($this->piVars['reason']);
		// erst vom MM-table alle l&ouml;schen, dann neu einf&uuml;gen
		$query = "UPDATE user_cichlids_comments SET
				hidden=1,
				delete_tstamp=UNIX_TIMESTAMP(NOW()),
				delete_reason='$reason',
				delete_user=$fe_uid
			WHERE uid=".intval($this->piVars['comment'])." LIMIT 1";
		print $query;
		mysql_query($query);
		if (mysql_errno())
		    return mysql_error();
		header("Location: /".$this->pi_linkTP_keepPIvars_url(array("picture" => $this->piVars['picture'], "tank" => $this->piVars['tank']), 0, 1, $this->piVars['backPid']));
		return $this->cObj->cObjGetSingle($conf['deleted'], $conf['deleted.']);
	    } else if ($this->piVars['action'] == "confirm") {
		if (isset($this->piVars['reason']) && $this->piVars['reason'] == '') 
			print '<div style="margin: 20px; border: 10px solid red; padding: 20px; font-weight: bold;">Please give a reason why you want to delete the comment.</div>';
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

	function suspend_user($content, $conf) {
	    $fe_user = $GLOBALS['TSFE']->fe_user->user;
	    $fe_uid = $fe_user['uid'];
	    $groups = preg_split("/,/", $fe_user['usergroup']);
	    $is_mod = in_array(3, $groups);
	    if(!$is_mod) die("Unauthorized access");

	    if ($this->piVars['confirmed'] == "yes" &&
		isset($this->piVars['reason']) && $this->piVars['reason'] != '') {
		$GLOBALS['TSFE']->set_no_cache();
		$reason = mysql_escape_string($this->piVars['reason']);
		$starttime = time() + 24 * 60 * 60;
		$userid = $this->piVars['user'];
		if($userid == '') return "An error occurred: Unknown user.";
		mail('alex@cichlids.com', 'cichlids.com: Suspended User', "
The user $userid was suspended.

Moderator: $fe_uid
Reason: $reason
");
		// anti-sandy-ban
		//return "An error occurred :-(.  A mail was sent to the site owner such that he can fix it.";
		$query = "UPDATE fe_users SET starttime=$starttime WHERE uid=$userid LIMIT 1";
		mysql_query($query);
		if (mysql_errno())
		    return mysql_error();
		return "Thanks, the user was suspended until " . date('Y-m-d H:i', $starttime) . " CET (24 hours from now)";
	    } else if ($this->piVars['action'] == "confirm") {
		if (isset($this->piVars['reason']) && $this->piVars['reason'] == '') 
			print '<div style="margin: 20px; border: 10px solid red; padding: 20px; font-weight: bold;">Please give a reason why you want to suspend the user.</div>';
		$GLOBALS['TSFE']->set_no_cache();
		$query = "SELECT * from fe_users WHERE uid=".intval($this->piVars['user']);
		$res = mysql_query($query);
		$row = mysql_fetch_assoc($res);
		mysql_free_result($res);
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		$obj->start($row);
		return $obj->cObjGetSingle($conf['confirm'], $conf['confirm.']);
	    } else {
		return "Error, do not know what to do here";
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


	function latest_comments($content, $conf) {
	    /* Alle Kats selecten */
	    $sel = array(
		"pidInList"	    =>	$conf['commentsPid'],
		"orderBy"	    =>	"tstamp DESC",
		"max"		    => 50,
		"where"		    => "type=1"
	    );
	    $query = $this->cObj->getQuery("user_cichlids_comments", $sel);
	    $res = mysql_query($query);
	    $max = 5;
	    $n = 0;
	    while ($n < $max && $row = mysql_fetch_assoc($res)) {
		$obj = t3lib_div :: makeInstance('tslib_cObj'); // Create new tslib_cObj for our use
		if ($row['note'] == "")
		    continue;
		$obj->start($row, 'user_cichlids_comments');
		$content .= $obj->cObjGetSingle($conf['comment'], $conf['comment.']);
		$n++;
	    }
	    return $content;
	}



      function comment_geturl($content, $conf) {
            $row = $this->cObj->data;
            $type = $row['type'];
            $id = $row['uid'];
            switch($row['type']) {
                case 1:
                    // pictures. muessen nachsheen, wo genau der drin is, ob cichlids oder tanks
                    $pic = $row['item'];
                    $res = mysql_query("SELECT * FROM user_cichlids_pictures WHERE uid=".$pic);
                    $row = mysql_fetch_assoc($res);
                    switch($row['pid']) {
                        case 29:
                            // tank pictures
                            $pid = 13;
                            break;
                        default:
                            $pid = 18;
                    }

                    break;
                case 2:
                    $pid = 61;
                    $tank = $row['item'];
                    break;
            }
            return $this->pi_linkTP_keepPIvars_url(array("picture" => $pic, "tank" => $tank), 1, 1, $pid);
        }


        function reportPictureAction($data) {
            $this->piVars = $data[$this->prefixId];
            $objResponse = new tx_xajax_response();

	    $picid = intval($this->piVars['picture']);
	    $reason = $this->piVars['reason'];
	    $agent = $_SERVER['HTTP_USER_AGENT'];
	    $fe_user = $GLOBALS['TSFE']->fe_user->user;
	    $username = $fe_user['username'];
	    $username_name = $fe_user['name'];

            mysql_query("UPDATE user_cichlids_pictures SET reported=reported+1 WHERE uid=".$picid);

	    mail("alex@cichlids.com", "cichlids.com Picture $picid reported",
"
The following picture has been reported:
<http://www.cichlids.com/pictures.html?user_cichlids_pi1[picture]=$picid>

User: $username_name ($username)
Reason: $reason

User-Agent: $agent
");

            $objResponse->addAlert("Thanks for reporting this picture.  We'll take care.");

            // $objResponse->addAssign("gallery_editor_pics_listing", "innerHTML", $content);
            // $objResponse->addAssign("gallery_editor_ops", "innerHTML", $opscontent);
            return $objResponse->getXML();
	}


	function get_rating_stars($rating) {
	    if ($rating == 0)
		return "&nbsp;";
	    return '<img src="/fileadmin/smile'.$rating.'.gif" width="75" height="15" align="right">';
	}

	function get_user($id) {
	    $res = mysql_query("SELECT * FROM fe_users WHERE uid=".$id);
	    print mysql_error();
	    if (mysql_num_rows($res) == 0)
		return NULL;
             return mysql_fetch_assoc($res);
	}

	function output_comment($content, $conf, $comment = null) {
	    ob_start();
	    if($comment == null)
		$comment = $this->cObj->data;
	    $uid = $comment['uid'];

	    $tstamp = $comment['tstamp']; 
	    $rating = $this->get_rating_stars($comment['rating']);
	    $user_id = $comment['fe_user'];
	    $note = strip_tags($comment['note']);
	    $note = nl2br($note);

	    $user = $this->get_user($user_id);
	    $userlink = "";
	    $username = "";

	    if (is_array($user)) {
		$username = strip_tags($user['first_name'] . " " . $user['last_name']);
		if (trim($username) == "") $username = strip_tags($user['username']);
		$userlink = $this->pi_linkTP_keepPIvars_url(array("user" => $user_id), 1, 1, $this->picturesPid);
		$comment_user_groups = preg_split("/,/", $user['usergroup']);
		$comment_user_is_mod = in_array(3, $comment_user_groups);
		$comment_user_is_admin = in_array(6, $comment_user_groups);
	    }

	    $fe_user = $GLOBALS['TSFE']->fe_user->user;
	    $groups = preg_split("/,/", $fe_user['usergroup']);
	    $is_mod = in_array(3, $groups);
	    $is_admin = in_array(6, $groups);

	    $deletepid = 58;
	    $params = array(
		'picture' => $GLOBALS['TSFE']->register['current_picture_uid'],
		'tank' => $GLOBALS['TSFE']->register['current_tank_uid'],
		'action' => 'confirm',
		'comment' => $uid,
		'backPid' => $GLOBALS['TSFE']->id,
	    );
	    $deletelink = $this->pi_linkTP_keepPIvars_url($params, 0, 1, $deletepid);

	?>
<a name="comment-<?=$uid;?>"/>

<? if($comment['hidden'] == 1) $is_deleted = true; else $is_deleted = false; ?>

<table border="0" cellspacing="0" cellpadding="0" class="c_comment"
	  <? if ($comment['score'] < -3): ?>
	    style="
	      filter:alpha(opacity=50);
	      -moz-opacity: 0.5;
	      -khtml-opacity: 0.5;
	      opacity: 0.5;
	    "
	  <? endif; ?>
>
  <tr class="c_comment_header" style="background: #DDDDDD; border-bottom: 1px solid #AAAAAA;;">
    <td style="width: 200px; border-right: 1px solid #AAAAAA;">
      <? if (is_array($user)): ?>
	<a href="<?=$userlink;?>" ><?=$username;?></a>
	<? if(false && $user['deleted'] == 1): ?> (account quit) <? endif; ?>
      <? else: ?>
	(anonymous)
      <? endif; ?>
	<? if($comment_user_is_admin): ?>
	    <span style="font-size: 6pt; color: #AAAAAA;">(a)</span>
	<? elseif($comment_user_is_mod): ?>
	    <span style="font-size: 6pt; color: #AAAAAA;">(m)</span>
	<? endif; ?>
	<? if($is_mod && is_array($user)): ?>
	  <?
	        $suspendpid = 113;
	        $params = array(
	    	'action' => 'confirm',
	    	'user' => $user['uid'],
	        );
	        $suspendlink = $this->pi_linkTP_keepPIvars_url($params, 0, 1, $suspendpid);
	    ?>	
	    <? if ($user['starttime'] > time()): ?>
	      <br>(susp. <?php echo date('Y-d-m H:i', $user['starttime']); ?>)
	    <? else: ?>
	    <a href="<?=$suspendlink;?>" style="display: block; float: right;">(suspend)</a>
	    <? endif; ?>
	<? endif; // is_array($user) ?>
	<? if($is_admin): ?>
	  <br><?=$comment['ip'];?>
	<? endif; ?>




    </td>
    <td style="border-right: 1px solid #AAAAAA; font-size: 7pt; ">
	&raquo;&nbsp;posted&nbsp;<?=date('Y/m/d h:i a', $tstamp);?>
    </td>
    <td style="width: 85px;">
	<? if(!$is_deleted || $is_mod) print $rating; else print "&nbsp;"?> 
	<? if($is_mod): ?>
	    <br/><div style="float: right; text-align: right;">
	      <? if($comment['hidden'] == 1): print "(is deleted)"; else: ?>
	      <a href="<?=$deletelink;?>">(delete)</a>
	      <? endif; ?>
	    </div>
	<? endif; ?>
    </td>
  </tr>
  <? if($note != '' || $is_deleted): ?>
      <tr>
	  <td colspan="3" style="padding: 5px; padding-bottom: 0px;">
	      <div id="comment-content-<?=$uid;?>"
		  <? if ($comment['score'] < -3 && !$is_deleted): ?>
		      style="display: none;"
		  <? endif; ?>
	      >
		  <? if(!$is_deleted || $is_mod): ?>
		      <div style="margin-bottom: 5px; width: 100%; overflow: hidden;">
			  <?=$note;?>
		      </div>
		  <? endif; ?>
		<? if(is_array($fe_user)): // Only logged in users may rate comments ?>
		    <div id="rate-comment-<?=$comment['uid'];?>" style="margin-top: 5px; color: #AAAAAA; font-size: 8pt;">
			<? echo $this->output_comment_rating($comment); ?>
		    </div>
		<? endif; ?>

		<? if($is_deleted): ?>
		    <?
			$duser = $this->get_user($comment['delete_user']);
			$del_user = $duser['name'];
			$del_tstamp = $comment['delete_tstamp'];
			$del_reason = $comment['delete_reason'];
		    ?>
		    <div style="font-size: 8pt;
			font-style: italic;
		    	margin-top: 10px;
		    	margin-bottom: 0px;
		    	width: 100%;">
			Comment deleted by <?=$del_user; ?> at <?=date('Y/m/d h:i a', $del_tstamp);?>, reason:
		    	<?=$del_reason; ?>
		    </div>
		<? endif; ?>
	    </div>
	    <? if ($comment['score'] < -3 && !$is_deleted): ?>
		<div id="show-comment-<?=$comment['uid'];?>" style="margin-bottom: 5px; padding-left: 40px;">
		    This comment is rated below a threshold and therefore hidden by default.
		    <a href="javascript:showComment(<?=$uid;?>)">Show.</a>
		</div>
	    <? endif; ?>
	</td>
      </tr>
  <? endif; ?>
</table>
	<?

	    $content = ob_get_contents();
	    ob_end_clean();
	    return $content;
	}

    function output_comment_rating($comment) {
	ob_start();
	$uid = $comment['uid'];
	$has_rated = false;

	$fe_user = intval($GLOBALS['TSFE']->fe_user->user['uid']);
	if($fe_user > 0) {
	    $query = "SELECT * FROM user_cichlids_comments_rated WHERE comment_uid=$uid AND fe_user=$fe_user";
	    $res = mysql_query($query);
	    if(mysql_num_rows($res) > 0) {
		$has_rated = true;
		$row = mysql_fetch_assoc($res);
		$has_rated_as = $row['rated'];
	    }
	}

      ?>
	<? if ($comment['fe_user'] != $fe_user): ?>
	<div style="float: right;">
	  <? if($has_rated): ?>
	    Your rating: <?
	      switch($has_rated_as) {
		  case 1:
		      print '<img src="/fileadmin/arrowup.gif" width="20" height="20" />';
		      break;
		  case -1:
		      print '<img src="/fileadmin/arrowdown.gif" width="20" height="20" />';
		      break;
	      }
	    ?>
	  <? else: ?>
	    <? $this->output_comment_rating_form($comment); ?>
	  <? endif; ?>
	</div>
	<? endif; ?>
	<? if(true || $comment['score'] < -3 || $comment['score'] > 3): ?>
	<br>Comment rating: <?=($comment['score'] > 0 ? '+' : '');?><?=$comment['score'];?>
	<? endif; ?>
    <?
	    $content = ob_get_contents();
	    ob_end_clean();
	    return $content;
    }

    function output_comment_rating_form($comment) {
?>
Rate this comment:
<form onSubmit="return false;" method="POST" enctype="multipart/form-data" style="display: inline;">
<input type="hidden" name="user_cichlids_pi1[comment]" value="<?=$comment['uid'];?>">
<input type="hidden" name="user_cichlids_pi1[rating]" value="down">
<input onClick="user_cichlids_pi1rateComment(xajax.getFormValues(this.form))"
  type="image" value="report" title="Poor comment" src="/fileadmin/arrowdown.gif" width="20" height="20" />
</form>
<form onSubmit="return false;" method="POST" enctype="multipart/form-data" style="display: inline;">
<input type="hidden" name="user_cichlids_pi1[comment]" value="<?=$comment['uid'];?>">
<input type="hidden" name="user_cichlids_pi1[rating]" value="up">
<input onClick="user_cichlids_pi1rateComment(xajax.getFormValues(this.form))"
  type="image" value="report" title="Helpful comment" src="/fileadmin/arrowup.gif" width="20" height="20" />
</form>
<?
    }

        function rateCommentAction($data) {
            $this->piVars = $data[$this->prefixId];
	    $uid = $this->piVars['comment'];
            $objResponse = new tx_xajax_response();

	    $fe_user = intval($GLOBALS['TSFE']->fe_user->user['uid']);
	    if ($fe_user == 0) {
		    $objResponse->addAlert("Error:  Must be logged in to rate comment.");
		    return $objResponse->getXML();
	    }

	    switch($this->piVars['rating']) {
		case 'up':
		  $val = 1;
		  break;
		case 'down':
		  $val = -1;
		  break;
		default:	
		    $objResponse->addAlert("Error:  invalid rating.  Please report.  Thanks.");
		    return $objResponse->getXML();
	    }

	    $query = "SELECT * FROM user_cichlids_comments WHERE uid=$uid";
            $res = mysql_query($query);
	    if(mysql_num_rows($res) == 0) {
		$objResponse->addAlert("No comment found.  Please report this error.  Thanks.");
		return $objResponse->getXML();
	    }
	    $comment = mysql_fetch_assoc($res);


	    $query = "SELECT * FROM user_cichlids_comments_rated WHERE comment_uid=$uid AND fe_user=$fe_user";
            $res = mysql_query($query);
	    if(mysql_num_rows($res) > 0) {
		$objResponse->addAlert("You have already rated this comment.");
		return $objResponse->getXML();
	    }

            mysql_query("INSERT INTO user_cichlids_comments_rated (comment_uid, fe_user, rated, tstamp)
		VALUES ($uid, $fe_user, $val, NOW())");

	    $comment['score'] = $comment['score'] + $val;
            mysql_query("UPDATE user_cichlids_comments SET score=score+$val WHERE uid=$uid");



	    $newout = $this->output_comment_rating($comment);
            $objResponse->addAssign("rate-comment-$uid", "innerHTML", $newout);
            return $objResponse->getXML();
        }





}



if (defined("TYPO3_MODE") && $TYPO3_CONF_VARS[TYPO3_MODE]["XCLASS"]["ext/user_cichlids/pi1/class.user_cichlids_pi1.php"])	{
	include_once($TYPO3_CONF_VARS[TYPO3_MODE]["XCLASS"]["ext/user_cichlids/pi1/class.user_cichlids_pi1.php"]);
}

?>
