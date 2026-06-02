<?php
/***************************************************************
*  Copyright notice
*
*  (c) 2011 Markus Kappe <markus.kappe@dix.at>
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

class tx_dixeasylogin_div {
	static function validateUrl($url) {
		if(function_exists('filter_var')) {
			return filter_var($url, FILTER_VALIDATE_URL);
		} else { 
			return eregi("^((https?)://)?(((www\.)?[^ ]+\.[com|org|net|edu|gov|us]))([^ ]+)?$", $url); 
		}
	}

	/**
	 * Tries to log in user into TYPO3 front-end by checking if the ID provided by the external 
	 * auth system matches a record in fe_users in the field tx_dixeasylogin_openid
	 * If configured so, it will create a user or connect a logged-in user with the given identifier
	 * 	 
	 * @param   string $identifier    The identifier as provided by Facebook or other systems. 
	 * @return  string   Message to be displayed to the user (success / error)
	 */
	static function loginFromIdentifier($identifier, $userinfo) {
		$user = tx_dixeasylogin_div::fetchUserByIdentifier($identifier);
		$fe_user = $GLOBALS['TSFE']->fe_user->fetchUserSession();
		
		if ($fe_user['uid']) { // user already logged in -> try to update the identifier
			if ($GLOBALS['piObj']->conf['allowUpdate']) {
				$res = $GLOBALS['TYPO3_DB']->exec_UPDATEquery('fe_users', 'uid='.(int)$fe_user['uid'], array('tx_dixeasylogin_openid' => $identifier) );
				return $GLOBALS['piObj']->pi_getLL('connect_success'); 
			}
			return 'how come you see this message?'; // should never be reached
		}

		// from this point on we are sure that the user is not logged in yet
		if (!$user['uid'] && $GLOBALS['piObj']->conf['allowCreate']) {
			$user = self::createUser($identifier, $userinfo);
		}

		if ($user['uid']) {
			self::login($user);
			self::redirectToSelf();
		} else {
			return sprintf($GLOBALS['piObj']->pi_getLL('nouser'), $identifier); // User not found. Please contact the admin of the website to request access to this site. Tell the admin this identifier: %s
		}
	}

	static function login($user) {
		$GLOBALS['TSFE']->fe_user->checkPid=0; //do not use a particular pid
		$GLOBALS['TSFE']->fe_user->createUserSession($user);
		$GLOBALS['TSFE']->fe_user->user = $GLOBALS['TSFE']->fe_user->fetchUserSession();
	}

	static function redirectToSelf() {
		#$url =  t3lib_div::locationHeaderUrl($GLOBALS['piObj']->pi_getPageLink($GLOBALS['TSFE']->id));
		$url = $GLOBALS['piObj']->cObj->getTypoLink_URL($GLOBALS['TSFE']->id, array('logintype' => 'login'));
		t3lib_utility_Http::redirect($url);
	}

	/**
	* @param string $identifier Identifier provided by the authorization mechanism e.g facebook-ID 
	* @return array corresponding fe_user record
	*/
		static function fetchUserByIdentifier($identifier) {
		$table = 'fe_users';
		$where = sprintf('tx_dixeasylogin_openid = %s %s', $GLOBALS['TYPO3_DB']->fullQuoteStr($identifier, $table), $GLOBALS['piObj']->cObj->enableFields($table));
		$res = $GLOBALS['TYPO3_DB']->exec_SELECTquery('*', $table, $where);
		return $GLOBALS['TYPO3_DB']->sql_fetch_assoc($res);
	}

	function createUser($identifier, $userinfo) {
		# debugster ($userinfo); debugster ($_GET); exit();
		// possible keys in $userinfo: nickname,email,fullname,dob,gender,postcode,country,language,timezone,prefix,firstname,lastname,suffix
		// @see http://openid.net/specs/openid-simple-registration-extension-1_0.html#response_format
		$table = 'fe_users';
		$values = array(
			'email' => $userinfo['email'],
			'username' => ($userinfo['nickname'] ? $userinfo['nickname'] : $userinfo['email']), // TODO: check if username is unique
			'tx_dixeasylogin_openid' => $identifier,
			'pid' => $GLOBALS['piObj']->conf['user_pid'],
			'crdate' => time(),
			'tstamp' => time(),
			'password' => md5(microtime(1)),
			'usergroup' => $GLOBALS['piObj']->conf['usergroup'],
			'name' => $userinfo['fullname'] ? $userinfo['fullname'] : trim($userinfo['firstname'].' '.$userinfo['lastname'].' '.$userinfo['suffix']),
			'title' => $userinfo['prefix'],
			'first_name' => $userinfo['firstname'],
			'last_name' => $userinfo['lastname'],
			'zip' => $userinfo['postcode'],
			'country' => $userinfo['country'], // incoming format: http://www.iso.org/iso/country_codes/iso_3166_code_lists/country_names_and_code_elements.htm (e.g. DE or AT)
			# no field like "date of birth" in fe_users. incoming format: YYYY-MM-DD
			# no field like "gender" in fe_users. incoming format: "M" or "F"
			# no field like "language" in fe_users. incoming format: http://www.loc.gov/standards/iso639-2/php/code_list.php (e.g. de or de-DE)
			# no field like "timezone" in fe_users. incoming format: http://www.twinsun.com/tz/tz-link.htm (e.g.  "Europe/Paris" or "America/Los_Angeles")
		);
		$res = $GLOBALS['TYPO3_DB']->exec_INSERTquery($table, $values);
		$user = tx_dixeasylogin_div::fetchUserByIdentifier($identifier);
		return $user;
	}

	static function makeCURLRequest($url, $method="GET", $params = "") {
		if (is_array($params)) {
			$params = http_build_query($params);
		}
		$curl = curl_init($url . ($method == "GET" && $params != "" ? "?" . $params : ""));
		
		//curl_setopt($curl, CURLOPT_FOLLOWLOCATION, TRUE);
		curl_setopt($curl, CURLOPT_HEADER, FALSE);
		curl_setopt($curl, CURLOPT_SSL_VERIFYPEER, FALSE);
		curl_setopt($curl, CURLOPT_HTTPGET, ($method == "GET"));
		curl_setopt($curl, CURLOPT_POST, ($method == "POST"));
		curl_setopt($curl, CURLOPT_RETURNTRANSFER, TRUE);
		curl_setopt($curl, CURLOPT_USERAGENT, $_SERVER['HTTP_USER_AGENT']);
		if ($method == "POST") {
			curl_setopt($curl, CURLOPT_POSTFIELDS, $params);
		}
		
		$response = curl_exec($curl);
		return $response;
	}
	
	static function render_ttContent($uid) {
		$conf = array(
			'tables' => 'tt_content',
			'source' => $uid
		);
		return $GLOBALS['TSFE']->cObj->RECORDS($conf);
	}

	// @credits go out to http://modi.de/2010/02/12/fluid-without-extbase/
	static function renderFluidTemplate($filename, $values) {
		$renderer = t3lib_div::makeInstance('Tx_Fluid_View_StandaloneView');
		$path = $GLOBALS['piObj']->conf['template_path'];
		if (substr($path, -1) != '/') { $path .= '/'; }

		$controllerContext = t3lib_div::makeInstance('Tx_Extbase_MVC_Controller_ControllerContext');
		$controllerContext->setRequest(t3lib_div::makeInstance('Tx_Extbase_MVC_Request'));
		$renderer->setControllerContext($controllerContext);
		$renderer->setTemplatePathAndFilename(t3lib_div::getFileAbsFileName($path . $filename));
		$renderer->assign('values', $values);

		return $renderer->render();
	}
	
	static function getFileRelFileName($filename) {
		if (substr($filename, 0, 4) == 'EXT:') { // extension
			list($extKey, $local) = explode('/', substr($filename, 4), 2);
			$filename = '';
			if (strcmp($extKey, '') && t3lib_extMgm::isLoaded($extKey) && strcmp($local, '')) {
				$filename = t3lib_extMgm::siteRelPath($extKey) . $local;
			}
		}
		return $filename;
	}

}


if (defined('TYPO3_MODE') && $TYPO3_CONF_VARS[TYPO3_MODE]['XCLASS']['ext/dix_easylogin/pi1/class.tx_dixeasylogin_div.php'])	{
	include_once($TYPO3_CONF_VARS[TYPO3_MODE]['XCLASS']['ext/dix_easylogin/pi1/class.tx_dixeasylogin_div.php']);
}

?>