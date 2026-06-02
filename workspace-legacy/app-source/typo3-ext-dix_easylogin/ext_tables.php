<?php
if (!defined('TYPO3_MODE')) {
	die ('Access denied.');
}

t3lib_div::loadTCA('tt_content');
$TCA['tt_content']['types']['list']['subtypes_excludelist'][$_EXTKEY.'_pi1']='layout,select_key,pages';


t3lib_extMgm::addPlugin(array(
	'LLL:EXT:dix_easylogin/locallang_db.xml:tt_content.list_type_pi1',
	$_EXTKEY . '_pi1',
	t3lib_extMgm::extRelPath($_EXTKEY) . 'ext_icon.gif'
),'list_type');

$tempColumns = array (
	'tx_dixeasylogin_openid' => array (        
		'exclude' => 0,        
		'label' => 'LLL:EXT:dix_easylogin/locallang_db.xml:fe_users.tx_dixeasylogin_openid',        
		'config' => array (
			'type' => 'input',    
			'size' => '30',
		)
	),
);


t3lib_div::loadTCA('fe_users');
t3lib_extMgm::addTCAcolumns('fe_users',$tempColumns,1);
t3lib_extMgm::addToAllTCAtypes('fe_users','tx_dixeasylogin_openid;;;;1-1-1');

t3lib_extMgm::addStaticFile($_EXTKEY,'static/', 'Easylogin');

?>