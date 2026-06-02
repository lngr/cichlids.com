<?php 
namespace Cichlids;
require("/var/www/html/www-cichlids/cichlids.extra/api/vendor/firebase/php-jwt/src/JWT.php");
require("/var/www/html/www-cichlids/cichlids.extra/api/src/Config.php");

use \Cichlids\Config;

class JWT {
	static function generateToken($user) {
		$key = \Cichlids\Config::JWTSecret();

		// fields siehe https://self-issued.info/docs/draft-ietf-oauth-json-web-token.html#rfc.section.4.1
		$token = array(
			"sub"		=> $user['uid'],	// subject
			"iat"		=> mktime(),		// issued at
			"exp"		=> mktime() + 86400 * 30,	// 30 tage expires
			"username"	=> $user['username'],
			"groups"	=> $user['groups'],
		);
		$jwt = \Firebase\JWT\JWT::encode($token, $key);
		return $jwt;
	}
}
