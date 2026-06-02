<?php

require_once(PATH_tslib."class.tslib_pibase.php");
require_once(PATH_t3lib."class.t3lib_div.php");

require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.mvc_tslib_pibase.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.entity_manager.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_comment.php');

class comments_manager {
	var $em = null;
	var $dbtable = "user_cichlids_comment";

	function comments_manager() {
	    $this->__construct();
	}

	function __construct() {
	    $this->em = new entity_manager();
	}

        function create($obj) {
            $obj = $this->em->insert($this->dbtable, $obj);
	    return $obj;
        }
	function update($obj) {
	    $obj = $this->em->update($this->dbtable, $obj);
	    return $obj;
	}
	function delete($obj) {
            $this->em->delete($this->dbtable, $obj);
	}

	function findById($uid) {
	    return $this->em->findById("cichlids_comment", $this->dbtable, $uid);
	}

	function findByPictureId($picid) {
	    return $this->findAll($picid);
	    return $all[0];
	}

	function findAll($picture = 0, $user = 0, $type=0, $start = 0, $stop = 0, $order = " user_cichlids_comments.tstamp DESC", $nonempty = false) {
	    $picture = intval($picture);
	    $where = "WHERE user_cichlids_comments.deleted=0 AND  user_cichlids_comments.hidden=0";

	    /*
	    if ($user > 0)
		$where .= " AND  user_cichlids_pictures.fe_user=".intval($user);
	    if ($species > 0) {
		$join = "RIGHT JOIN user_cichlids_species_pictures_mm ON user_cichlids_species_pictures_mm.uid_local = user_cichlids_pictures.uid";
		$where .= " AND user_cichlids_species_pictures_mm.uid_foreign=" . intval($species);
	    }
	    */
	    if ($picture > 0) {
		$where .= " AND user_cichlids_comments.parent = $picture AND type=1";
	    }
	    if ($nonempty)
		$where .= " AND user_cichlids_comments.note != ''";

	    $limit = ($stop - $start != 0 ? "LIMIT $start, $stop" : "");
	    $query = "SELECT user_cichlids_comments.* FROM user_cichlids_comments $join $where ORDER BY $order $limit";

	    return $this->em->select("cichlids_comment", $query);
	}

}
