<?php
if (!defined('TYPO3_MODE')) {
	die ('Access denied.');
}

t3lib_extMgm::addPItoST43($_EXTKEY, 'pi1/class.tx_dixeasylogin_pi1.php', '_pi1', 'list_type', 0);

$GLOBALS['TYPO3_CONF_VARS']['EXTCONF']['realurl']['_DEFAULT']['postVarSets']['_DEFAULT']['easylogin'] = array(
	'0' => array (
		'GETvar' => 'tx_dixeasylogin_pi1[action]',
	),	
);

?>