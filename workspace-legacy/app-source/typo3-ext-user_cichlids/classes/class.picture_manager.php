<?php

require_once(PATH_tslib."class.tslib_pibase.php");
require_once(PATH_t3lib."class.t3lib_div.php");

require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.mvc_tslib_pibase.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.entity_manager.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_gallery.php');

require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_picture.php');

class picture_manager {
	var $em = null;
	var $cObj = null;

	function picture_manager() {
	    $this->__construct();
	}

	function __construct() {
	    $this->em = new entity_manager();
	    $this->cObj = t3lib_div::makeInstance('tslib_cObj');

	}

        function createPicture($obj) {
            $obj = $this->em->insert("user_cichlids_pictures", $obj);
	    return $obj;
        }
	function updatePicture($obj) {
	    $obj = $this->em->update("user_cichlids_pictures", $obj);
	    return $obj;
	}
	function deletePicture($obj) {
            $this->em->delete("user_cichlids_pictures", $obj);
	}


	function getPictureById($uid) {
	    return $this->em->findById("cichlids_picture", "user_cichlids_pictures", $uid);
	}
	function findById($uid) {
	    return $this->getPictureById($uid);
	}

	function createQuery($fields, $type, $user, $category, $species, $page, $perpage, $order) {
	    $where = "WHERE user_cichlids_pictures.deleted=0 AND  user_cichlids_pictures.hidden=0";

	    switch($type) {
		  case "tanks":
		      $where .= " AND  user_cichlids_pictures.pid IN (29)";
		      break;
		  case "cichlids":
		      $where .= " AND  user_cichlids_pictures.pid IN (21)";
		      break;
		  case "contest":
		      $where .= " AND  user_cichlids_pictures.pid IN (131)";
		      break;
		  default:
		      $where .= " AND  user_cichlids_pictures.pid IN (21,29,109)";
		      break;
	    }
	    if ($user > 0)
		$where .= " AND  user_cichlids_pictures.fe_user=".intval($user);
	    if ($species > 0) {
		$join = "RIGHT JOIN user_cichlids_species_pictures_mm ON user_cichlids_species_pictures_mm.uid_local = user_cichlids_pictures.uid";
		$where .= " AND user_cichlids_species_pictures_mm.uid_foreign=" . intval($species);
	    }

	    if ($category > 0) {
		// join query machen, umd ie species bzw. die species cat zu finden		  
		$join = "LEFT JOIN user_cichlids_species_pictures_mm ON user_cichlids_species_pictures_mm.uid_local = user_cichlids_pictures.uid
                              LEFT JOIN user_cichlids_species ON user_cichlids_species.uid = user_cichlids_species_pictures_mm.uid_foreign";
		$where .= " AND user_cichlids_species.category =" . intval($category);
	    }
	    if ($perpage > 0) {
		$start = ($page - 1) * $perpage;
		$limit =  "LIMIT $start,$perpage";
	    }
	    $query = "SELECT $fields FROM user_cichlids_pictures $join $where ORDER BY $order $limit";
	    return $query;
	}
	
	function findAll($type = "", $user = 0, $category = 0, $species = 0, $page = 1, $perpage = 0, $order = " user_cichlids_pictures.tstamp DESC") {
	    $query = $this->createQuery("user_cichlids_pictures.*", $type, $user, $category, $species, $page, $perpage, $order);
	    return $this->em->select("cichlids_picture", $query);
	}

	function findByUser($user) {
	    $userid = $user->uid;
	    $query = "SELECT * FROM user_cichlids_pictures WHERE fe_user=$userid AND deleted=0 AND hidden=0 ORDER BY views DESC";
	    return $this->em->select("cichlids_picture", $query);
	}
}
