<?php

require_once(PATH_tslib."class.tslib_pibase.php");
require_once(PATH_t3lib."class.t3lib_div.php");

require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.mvc_tslib_pibase.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.entity_manager.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_gallery.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.cichlids_picture.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.gallery_manager.php');
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.picture_manager.php');

class gallery_browser extends mvc_tslib_pibase {
	var $prefixId = "gallery_browser";
	var $extKey = "user_cichlids";	// The extension key.
	var $allowCaching = true;
	var $conf = array();

	var $gallery_manager = null;
	var $picture_manager = null;
	var $xajax;

	/* Actions for Web Layer ---------------------------------------------  */

        var $flow = array(
              // from ist 'default', wenn from nicht gesetzt ist.
              'default' => array(
                    // outcome
                    "defaultview"	      =>  "list_user_galleries.php",
                    "list_user_galleries"     =>  "list_user_galleries.php",
                    "browse_user_gallery"     =>  "browse_user_gallery.php",
              ),
        );
        var $actions = array(
              // actionname => function
        );


	function controllerDefaultAction() {
	      if (isset($this->piVars['gallery'])) {
		  print "doens";
		  return "browse_user_gallery";
	      }
	      return "list_user_galleries";
	}


	function gallery_editor() {
	    $this->__construct();
	}
	function main($content, $conf) {
	    require_once (t3lib_extMgm::extPath('xajax') . 'class.tx_xajax.php');
	    $this->xajax = t3lib_div::makeInstance('tx_xajax');
	    $this->xajax->decodeUTF8InputOn();
	    $this->xajax->setCharEncoding('utf-8');
	    $this->xajax->setWrapperPrefix($this->prefixId);
	    $this->xajax->statusMessagesOn();
	    $this->xajax->debugOff();
	    // $this->xajax->debugOn();
	    $this->xajax->registerFunction(array('addPicture', &$this, 'addPictureAction'));
	    $this->xajax->registerFunction(array('removePicture', &$this, 'removePictureAction'));
	    $this->xajax->registerFunction(array('moveUpPicture', &$this, 'moveUpPictureAction'));
	    $this->xajax->registerFunction(array('moveDownPicture', &$this, 'moveDownPictureAction'));
	    $this->xajax->processRequests();
	    $GLOBALS['TSFE']->additionalHeaderData[$this->prefixId] = $this->xajax->getJavascript(t3lib_extMgm::siteRelPath('xajax'));
	    return parent::main($content, $conf);
	}

	function __construct() {
	    parent::__construct();
	    $this->gallery_manager = new gallery_manager();
	    $this->picture_manager = new picture_manager();
	}

	function getAjaxButton($label, $func, $params, $extra="") {
	    $url = "";
	    $out = sprintf('<form action="%s" onSubmit="return false;" method="POST" enctype="multipart/form-data" style="display: inline;">', $url);
	    foreach(array_keys($params) as $key) {
		$out .= sprintf('<input type="hidden" name="%s[%s]" value="%s">', $this->prefixId, $key, $params[$key]);
	    }
	    $out .= sprintf('<input onClick="%s%s(xajax.getFormValues(this.form))" type="submit" value="%s" %s /></form>',
		$this->prefixId, $func, $label, $extra);
	    return $out;
	}


	function addPictureAction($data) {
            $this->piVars = $data[$this->prefixId];
            $objResponse = new tx_xajax_response();

	    $galid = intval($this->piVars['gallery']);
	    $picid = intval($this->piVars['picture']);

	    $error = $this->gallery_manager->addPictureToGallery($galid, $picid);
	    if ($error)
		$objResponse->addAlert("Error: Could not add picture to gallery: $error");
            $content = $this->templating("edit_gallery_pics.php");
            $objResponse->addAssign("gallery_editor_pics_listing", "innerHTML", $content);
	    $opscontent = $this->templating("edit_gallery_ops.php");
            $objResponse->addAssign("gallery_editor_ops", "innerHTML", $opscontent);
            return $objResponse->getXML();
	}

	function removePictureAction($data) {
            $this->piVars = $data[$this->prefixId];
            $objResponse = new tx_xajax_response();

	    $galid = intval($this->piVars['gallery']);
	    $picid = intval($this->piVars['picture']);

	    $error = $this->gallery_manager->removePictureFromGallery($galid, $picid);
	    if ($error)
		  $objResponse->addAlert("Error: Could not delete picture from gallery: $galid-$picid");
	    $content = $this->templating("edit_gallery_pics.php");
            $objResponse->addAssign("gallery_editor_pics_listing", "innerHTML", $content);
	    $opscontent = $this->templating("edit_gallery_ops.php");
            $objResponse->addAssign("gallery_editor_ops", "innerHTML", $opscontent);
            return $objResponse->getXML();
	}

	function movePictureAction($data, $offset) {
            $this->piVars = $data[$this->prefixId];
            $objResponse = new tx_xajax_response();

	    $galid = intval($this->piVars['gallery']);
	    $picid = intval($this->piVars['picture']);

	    if ($galid * $picid != 0) {
		$this->gallery_manager->changePictureOrderOffset($galid, $picid, $offset);
	    }
	    $content = $this->templating("edit_gallery_pics.php");
            $objResponse->addAssign("gallery_editor_pics_listing", "innerHTML", $content);
            return $objResponse->getXML();
	}

	function moveUpPictureAction($data) {
	    return $this->movePictureAction($data, -1);
	}

	function moveDownPictureAction($data) {
	    return $this->movePictureAction($data, +1);
	}


	/* Hier sind die Actions vom Web */

	function createGalleryAction() {
	    if ($this->piVars['gallery_title'] == "") {
		$this->addError("gallery_title", "required");
		return "error";
	    }
	    $gal = new cichlids_gallery();
	    $gal->title = $this->piVars['gallery_title'];
	    $user = $this->getCurrentFEUser();
	    $gal->fe_user = $user->uid;
	    $gal->hidden = 1;

	    $gal = $this->gallery_manager->createGallery($gal);
	    $galid = $gal->uid;
	    $this->createdGallery = $gal;
	    return "created";
	}

	function renameGalleryAction() {
	    $gal = $this->getManagedUserGallery();
	    if ($this->piVars['gallery_title'] == "") {
		$this->addError("gallery_title", "required");
		return "error";
	    }
	    $gal->title = $this->piVars['gallery_title'];
	    $gal = $this->gallery_manager->updateGallery($gal);
	    return "renamed";
	}
	function deleteGalleryAction() {
	    $gal = $this->getManagedUserGallery();
	    $pics = $this->gallery_manager->getPicturesInGallery($gal);
	    if (count($pics) > 0) {
		$this->addError("delete_gallery", "Gallery not empty.");
		return "error";
	    }
	    $this->gallery_manager->deleteGallery($gal);
	    return "deleted";
	}
	function hideGalleryAction() {
	    $gal = $this->getManagedUserGallery();
	    $gal->hidden = 1;
	    $gal = $this->gallery_manager->updateGallery($gal);
	    return "hidden";
	}
	function unhideGalleryAction() {
	    $gal = $this->getManagedUserGallery();
	    $gal->hidden = 0;
	    $gal = $this->gallery_manager->updateGallery($gal);
	    return "unhidden";
	}

	function getFeUserById($uid) {
	    $uid = intval($uid);
	    $res = mysql_query("SELECT * FROM fe_users WHERE uid=$uid LIMIT 1");
	    if (mysql_num_rows($res) == 0)
		return new cichlids_feuser(array());
	    return new cichlids_feuser(mysql_fetch_assoc($res));
	}

	function getCurrentFEUser() {
	    return $this->getFEUserById($GLOBALS['TSFE']->fe_user->user['uid']);
	}

	function getManagedUserGallery() {
	    if (isset($this->createdGallery))
		return $this->createdGallery;
	    $galid = $this->piVars['gallery'];
	    $gal = $this->gallery_manager->getGalleryById($galid);
	    $user = $this->getCurrentFEUser();
	    if ($gal->fe_user != $user->uid)
		die("Unauthorized access.");
	    return $gal;
	}

}
