<?php

require_once(PATH_tslib."class.tslib_pibase.php");
require_once(PATH_t3lib."class.t3lib_div.php");

require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_tslib_pibase.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.entity_manager.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_gallery.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_picture.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.gallery_manager.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.picture_manager.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.species_manager.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.comments_manager.php');

class picture_viewer extends cichlids_tslib_pibase {
	var $prefixId = "user_cichlids_pi1"; // compat
	var $extKey = "user_cichlids";	// The extension key.
	var $allowCaching = true;
	var $conf = array();

	var $gallery_manager = null;
	var $picture_manager = null;
	var $species_manager = null;
	var $comments_manager = null;
	var $xajax;

	/* Actions for Web Layer ---------------------------------------------  */

        var $flow = array(
              // from ist 'default', wenn from nicht gesetzt ist.
              'default' => array(
                    // outcome
                    "defaultview"	      =>  "list_pictures.php",
                    "list_pictures"	      =>  "list_pictures.php",
                    "show_picture"	      =>  "show_picture.php",
              ),
        );
        var $actions = array(
              // actionname => function
        );


	function controllerDefaultAction() {
	      if (isset($this->piVars['picture'])) {
		  return "show_picture";
	      }
	      return "list_pictures";
	}


	function picture_viewer() {
	    $this->__construct();
	}

	function __construct() {
	    parent::__construct();
	    $this->gallery_manager = new gallery_manager();
	    $this->picture_manager = new picture_manager();
	    $this->species_manager = new species_manager();
	}

	function get_picture() {
	    if (!isset($this->_pic)) {
		$pic = intval($this->piVars['picture']);
		if ($pic == 0)
		    $this->_pic = new cichlids_picture();
		else
		    $this->_pic = $this->picture_manager->findById($pic);
	    }
	    return $this->_pic;
	}

	function get_user_pictures($user) {
	    return $this->picture_manager->findByUser($user);
	}

	function get_related_pictures_species($max = 15) {
	    $pic = $this->get_picture();
	    $species = $this->species_manager->findByPictureId($pic->uid);
	    $uid = intval($species->uid);
	    if ($uid == 0)
		return array();
	    return $this->picture_manager->findAll("cichlids", 0, 0, $uid, 0, $max, "user_cichlids_pictures.views DESC");
	}

	function get_related_pictures_category($max = 15) {
	    $pic = $this->get_picture();
	    $species = $this->species_manager->findByPictureId($pic->uid);
	    $uid = intval($species->category);
	    return $this->picture_manager->findAll("cichlids", 0, $uid, 0, 0, $max, "user_cichlids_pictures.views DESC");
	}

	function get_picture_list($max = 15) {
	    list($type, $user, $category, $species, $page, $order) = $this->_query_helper();
	    return $this->picture_manager->findAll($type, $user, $category, $species, $page, $max, $order);
	}

	function get_picture_count() {
	    list($type, $user, $category, $species, $page, $order) = $this->_query_helper();
	    $query = $this->picture_manager->createQuery("COUNT(user_cichlids_pictures.uid) as count", $type, $user, $category, $species, $page, 0, $order);
	    $res = mysql_query($query);
	    if (mysql_errno()) {
		print mysql_error();
	    }
	    $row = mysql_fetch_assoc($res);
	    return $row['count'];
	}

	function _query_helper() {
	    $gallery = intval($this->piVars['gallery']);
	    if ($gallery > 0) {
		  // gallery kann man nich zusamemn mit dem rest aufrufen, gallery_manager befragen
		  print "gallery nich";
		  return array();
	    }

	    $type = $this->piVars['type'];
	    if (isset($_GET['type']))
		$this->piVars['type'] = $_GET['type'];
	    $user = intval($this->piVars['user']);

	    $page = intval($this->piVars['page']);
	    if ($page == 0)
		$page = 1;

	    $category = intval($this->piVars['category']);
	    $species = intval($this->piVars['species']);

	    $sort = $this->piVars['sort'];
	    $extrasort = "user_cichlids_pictures.views";
	    switch($sort) {
		case "views":
		    $order = " user_cichlids_pictures.views DESC";
		    break;
		case "comments":
		    $order = " user_cichlids_pictures.num_comments,$extrasort DESC";
		    break;
		case "rating":
		    $order = " user_cichlids_pictures.rating DESC,$extrasort DESC";
		    break;
		default:
		case "tstamp":
		    $order = " user_cichlids_pictures.tstamp DESC";
		    break;
	    }

	    return array($type, $user, $category, $species, $page, $order);
	}

}
