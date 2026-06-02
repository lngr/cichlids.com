<?php

require_once(PATH_tslib."class.tslib_pibase.php");
require_once(PATH_t3lib."class.t3lib_div.php");

require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.mvc_tslib_pibase.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.entity_manager.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_gallery.php');

class gallery_manager {
	var $em = null;

	function gallery_manager() {
	    $this->__construct();
	}

	function __construct() {
	    // parent::__construct();
	    $this->em = new entity_manager();
	}

        function createGallery($gal) {
            $gal = $this->em->insert("user_cichlids_gallery", $gal);
	    return $gal;
        }
	function updateGallery($gal) {
	    $gal = $this->em->update("user_cichlids_gallery", $gal);
	    return $gal;
	}
	function deleteGallery($gal) {
            $this->em->delete("user_cichlids_gallery", $gal);
	}

	function getGalleryById($uid) {
	    return $this->em->findById("cichlids_gallery", "user_cichlids_gallery", $uid);
	}

	function getPictureUidsInGallery($galid) {
	    $query = "SELECT uid_picture FROM user_cichlids_gallery_pictures_mm WHERE uid_gallery=$galid ORDER BY sorting";
	    $res = mysql_query($query);
	    if (mysql_errno()) {
		print mysql_error();
		return array();
	    }
	    $all = array();
	    while($row = mysql_fetch_assoc($res))
		$all[] = $row['uid_picture'];
	    return $all;
	}

	function getPicturesInGallery($gal) {
	    $galid = $gal->uid;
	    $query = "SELECT uid_gallery,user_cichlids_pictures.* FROM user_cichlids_gallery_pictures_mm R LEFT JOIN user_cichlids_pictures ON R.uid_picture=user_cichlids_pictures.uid WHERE R.uid_gallery=$galid ORDER BY R.sorting";
	    return $this->em->select("cichlids_picture", $query);
	}

	function addPictureToGallery($galid, $picid) {
	    if (!$galid)
		return "Gallery $galid not found.";
	    if (!$picid)
		return "Picture $picid not found.";

	    $ingal = $this->getPictureUidsInGallery($galid);
	    if (in_array($picid, $ingal))
		return "Picture is already in this gallery.";
	    $n = count($ingal);
	    $query = "INSERT INTO user_cichlids_gallery_pictures_mm (uid_gallery, uid_picture, sorting) VALUES($galid, $picid, $n)";
	    mysql_query($query);
	}

	function removePictureFromGallery($galid, $picid) {
	    if (!$galid)
		return "Gallery $galid not found.";
	    if (!$picid)
		return "Picture $picid not found.";
	    $query = "DELETE FROM user_cichlids_gallery_pictures_mm WHERE uid_gallery=$galid AND uid_picture=$picid LIMIT 1";
	    mysql_query($query);
	}

	function changePictureOrderOffset($galid, $picid, $offset) {
	    if (!$galid)
		return "Gallery $galid not found.";
	    if (!$picid)
		return "Picture $picid not found.";

	    $allpics = $this->getPictureUidsInGallery($galid);
	    $positions = array_flip($allpics);
	    $oldpos = $positions[$picid];
	    $newpos = $oldpos + $offset;
	    if ($newpos < 0 || $newpos >= count($allpics)) {
		return;
	    }
	    $tmp = $allpics[$newpos];
	    $allpics[$newpos] = $allpics[$oldpos];
	    $allpics[$oldpos] = $tmp;
	    $pos = 0;
	    foreach($allpics as $pic) {
		$this->setPictureSorting($galid, $pic, $pos++);
	    }
	    return;
	}

	function getPicturePosition($galid, $picid) {
	    $allpics = $this->getPictureUidsInGallery($galid);
	    $positions = array_flip($allpics);
	    return $positions[$picid];
	}

	function setPictureSorting($galid, $picid, $sort) {
	    $query = "UPDATE user_cichlids_gallery_pictures_mm SET sorting=$sort WHERE uid_gallery=$galid AND uid_picture=$picid";
	    $res = mysql_query($query);
	    if (mysql_errno()) {
		print mysql_error();
	    }
	}

	function getUserGalleries($user) {
	    $userid = $user->uid;
	    $query = "SELECT * FROM user_cichlids_gallery WHERE fe_user=$userid AND deleted=0";
	    return $this->em->select("cichlids_gallery", $query);
	}

	function listGalleries($order = "tstamp DESC") {
	    $query = "SELECT * FROM user_cichlids_gallery WHERE deleted=0 AND hidden=0 ORDER BY $order ";
	    return $this->em->select("cichlids_gallery", $query);
	}

}
