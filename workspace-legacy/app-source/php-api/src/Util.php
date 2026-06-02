<?php
namespace Cichlids;

class Util {
	static function addCors($response) {
		$allowed = [
			"http://localhost:4200",
			"http://localhost:4000",
			"https://www.cichlids.com",
			"https://dev.cichlids.com",
		];
		$origin = isset($_SERVER['HTTP_ORIGIN']) ? $_SERVER['HTTP_ORIGIN'] : "";

		//error_log("checking orrigin $origin");
	
		if (in_array($origin, $allowed)) {
			return $response
				->withHeader('Access-Control-Allow-Origin', $origin)
				->withHeader('Access-Control-Allow-Methods', 'POST, GET, OPTIONS, HEAD, DELETE, PUT')
				->withHeader('Access-Control-Allow-Headers', 'Authorization, Content-Type')
				->withHeader('Access-Control-Allow-Credentials', 'true')
				->write("");
		}
		return $response;
	}
}
