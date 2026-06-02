<?php
if (!defined ("TYPO3_MODE")) 	die ("Access denied.");
t3lib_extMgm::addUserTSConfig('
	options.saveDocNew.user_cichlids_category=1
');
t3lib_extMgm::addUserTSConfig('
	options.saveDocNew.user_cichlids_pictures=1
');
t3lib_extMgm::addUserTSConfig('
	options.saveDocNew.user_cichlids_comments=1
');
t3lib_extMgm::addUserTSConfig('
	options.saveDocNew.user_cichlids_species=1
');

t3lib_extMgm::addPItoST43($_EXTKEY,"pi1/class.user_cichlids_pi1.php","_pi1","list_type",1);


t3lib_extMgm::addPItoST43($_EXTKEY,"pi2/class.user_cichlids_pi2.php","_pi2","list_type",1);
t3lib_extMgm::addPItoST43($_EXTKEY,"pi3/class.user_cichlids_pi3.php","_pi3","list_type",1);
?>
