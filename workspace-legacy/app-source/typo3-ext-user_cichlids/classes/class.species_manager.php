<?php

require_once(PATH_tslib."class.tslib_pibase.php");
require_once(PATH_t3lib."class.t3lib_div.php");

require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.mvc_tslib_pibase.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.entity_manager.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_gallery.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_picture.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_species.php');

class species_manager {
	var $em = null;
	var $dbtable = "user_cichlids_species";

	function species_manager() {
	    $this->__construct();
	}

	function __construct() {
	    $this->em = new entity_manager();

	}

        function createSpecies($obj) {
            $obj = $this->em->insert($this->dbtable, $obj);
	    return $obj;
        }
	function updateSpecies($obj) {
	    $obj = $this->em->update($this->dbtable, $obj);
	    return $obj;
	}
	function deleteSpecies($obj) {
            $this->em->delete($this->dbtable, $obj);
	}

	function getSpeciesById($uid) {
	    return $this->em->findById("cichlids_species", $this->dbtable, $uid);
	}

	function findById($uid) {
	    return $this->em->findById("cichlids_species", "user_cichlids_species", $uid);
	}

	function findByPictureId($picid) {
	    $query = "SELECT user_cichlids_species.* FROM user_cichlids_species JOIN user_cichlids_species_pictures_mm ON user_cichlids_species_pictures_mm.uid_foreign = user_cichlids_species.uid
		      WHERE user_cichlids_species_pictures_mm.uid_local=$picid LIMIT 0,1";
	    $all = $this->em->select("cichlids_species", $query);
	    return $all[0];
	}

}
