<?php

require_once(PATH_tslib."class.tslib_pibase.php");
require_once(PATH_t3lib."class.t3lib_div.php");
require_once(t3lib_extMgm::extPath('user_cichlids').'classes/class.entity_manager.php');

class mvc_tslib_pibase extends tslib_pibase {
	var $flow = array(
	      'default' => array(
		    "defaultview"   =>	"default.php",
	      ),
	);
	var $actions = array(
	);
	var $errors = array();
	var $em = null;

	function mvc_tslib_pibase() {
	    $this->__construct();
	}
	function __construct() {
	    parent::tslib_pibase();
	    $this->em = new entity_manager();
	}
	
	function main($content,$conf)	{
		$this->conf=$conf;
		$this->pi_setPiVarDefaults();
		$this->pi_loadLL();
		return $this->controller();
	}

	function controller() {
	    $view = "";
	    if ($this->piVars['controllerAction'] == "") {
		$outcome = $this->controllerDefaultAction();
		$action = "default";
	    } else {
		$action = $this->piVars['controllerAction'];
		$func = $this->actions[$action];
		if ($func == "") {
		    // Keine Funktion fuer diese action gesetzt, vielleicht koennen wir direkt ein view finden?
		    if (!is_array($this->flow[$action])) {
			$view = $this->flow[$action];
		    } else
			$outcome = $this->controllerDefaultAction();
		} else {
		    $func = $this->actions[$action];
		    $outcome = $this->$func();
		}
	    }
	    if ($view == "")
		$view = $this->flow[$action][$outcome];
	    return $this->templating($view);
	}

	function controllerDefaultAction() {
	    return "defaultview";
	}

        function templating($file) {
            ob_start();
	    $path = get_class($this);
            include(t3lib_extMgm::extPath('user_cichlids').'scripts/'.$path.'/'.$file);
            $out = ob_get_contents();
            ob_end_clean();
            return $out;
        }

	function getError($str) {
	    if ($this->errors[$str] != "") {
		return '<div class="error">'.$this->errors[$str].'</div>';
	    }
	    return "";
	}
	function addError($str, $err) {
	    $this->errors[$str] = $err;
	}
	function getInputName($name) {
	    return $this->prefixId . "[" . $name . "]";
	}
	function getActionLinkUrl($action, $ar=array()) {
	    $ar["controllerAction"]  = $action;
	    $link = $this->pi_linkTP_keepPIvars_url($ar, 0, 1);
	    return $link;
	}

	function getActionForm($ar = array()) {
	    $link = $this->pi_linkTP_keepPIvars_url(array(), 0, 1);
	    $out = '<form method="POST" action="'.$link.'">' . "\n";
	    $out .= '<input type="hidden" name="'.$this->prefixId.'[controllerAction]" value="default">' . "\n";
	    foreach(array_keys($ar) as $key) {
		$out .= '<input type="hidden" name="'.$this->prefixId.'['.$key.']" value="'.$ar[$key].'">' . "\n";
	    }
	    return $out;
	}
	function getActionButton($value, $action, $extra="") {
	    return '<input type="submit" onClick="this.form[\''.$this->prefixId.'[controllerAction]\'].value=\''.$action.'\';" value="'.$value.'" '.$extra.'>';
	}

	function linkView($view, $cache = false, $overwrite = false) {
	    return $this->pi_linkTP_keepPIvars_url(array("view" => $view), $cache, $overwrite);
	}

	function crop($str, $len, $after = "") {
	    return $this->cObj->crop($str, "$len | $after");
	}
}
