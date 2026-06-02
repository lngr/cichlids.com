<?php
      if (
                        $GLOBALS['TSFE']->fe_user->user['tx_dixeasylogin_openid'] == "facebook-100003586456551"
                        // Alex testuser || $GLOBALS['TSFE']->fe_user->user['tx_dixeasylogin_openid'] == "facebook-1411159411"
                        || $GLOBALS['TSFE']->fe_user->user['email'] == "ciara8300@gmail.com"
                        || $GLOBALS['TSFE']->fe_user->user['email'] == "rajlovescoops@gmail.com"
                        //|| $GLOBALS['TSFE']->fe_user->user['email'] == "alex@cichlids.com"
			|| $_SERVER['HTTP_X_FORWARDED_FOR'] == '::ffff:98.116.63.69'
			|| $_SERVER['HTTP_X_FORWARDED_FOR'] == '::ffff:70.192.69.202'

                ) {
                        //print "Please go away! ";
                        die("");
                }
?>
