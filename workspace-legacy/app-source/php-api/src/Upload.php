<?php 
namespace Cichlids;

//require_once("vendor/ramsey/uuid/src/Uuid.php");

use \Cichlids\Db;
use \Cichlids\Config;

class Upload {
	static function convertToPicture($request, $response, $args, $jwt) {
		$picuid = Upload::convertToPictureImpl($request, $response, $args, $jwt, 21);

		if ($picuid == "0") {
                	return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->withStatus(500);
		}

		$slug = Pictures::makeSlug($picuid);
		$res = array(
			"status" => "ok",
			"slug" => $slug,
		);
                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->withStatus(200)
                        ->write(json_encode($res));
	}

	static function convertToAvatarImage($request, $response, $args, $jwt) {
		return Upload::convertToUserImageImpl($request, $response, $args, $jwt, 139, 'user_cichlids_avatar_image');
	}

	static function convertToProfileImage($request, $response, $args, $jwt, $pid, $column) {
		return Upload::convertToUserImageImpl($request, $response, $args, $jwt, 138, 'user_cichlids_profile_image');
	}

	static function convertToUserImageImpl($request, $response, $args, $jwt, $pid, $column) {
	    	$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);
		
		if (!$userid) {
            		return $response->withStatus(401);
		}

		$picuid = Upload::convertToPictureImpl($request, $response, $args, $jwt, $pid, false);

		if ($picuid == "0") {
                	return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->withStatus(500);
		}

                $db = \Cichlids\Db::get();

                $sql = "UPDATE fe_users SET $column=:picuid where uid=:userid";

                $stmt = $db->prepare($sql);
                $stmt->bindParam("userid", $userid, \PDO::PARAM_INT);
                $stmt->bindParam("picuid", $picuid, \PDO::PARAM_INT);
                $stmt->execute();

		$res = array(
			"status" => "ok",
		);
 
                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->withStatus(200)
                        ->write(json_encode($res));
	}

	static function convertToTankPicture($request, $response, $args, $jwt) {
                $data = $request->getParsedBody();
                $tankid = $data['tankid'];
                $type = $data['type'];

		error_log("converting to tank picture: $tankid, $type");

		switch ($type) {
			case "image":
				$column = "tank_images";
				break;
			case "deco":
			case "tec":
				$column = $type . "_images";
				break;
			default:
            			return $response->withStatus(400);
		}

    		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);
		if (!$userid) {
            		return $response->withStatus(401);
		}

               	$tank = Tanks::loadTank($tankid, false);

                if ($tank == null) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(404);
                }

		if ($tank->fe_user != $userid) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(401);
		}

		$picuid = Upload::convertToPictureImpl($request, $response, $args, $jwt, 137);

		error_log("converting to tank picture: $tankid, $type, $picuid");

		if ($picuid == "0") {
                	return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->withStatus(500);
		}

                $db = \Cichlids\Db::get();

                $sql = "UPDATE user_cichlids_tanks SET $column = CONCAT(COALESCE($column, ''), ',', :picuid) where uid=:tankid";

                $stmt = $db->prepare($sql);
                $stmt->bindParam("tankid", $tankid, \PDO::PARAM_INT);
                $stmt->bindParam("picuid", $picuid, \PDO::PARAM_STR);
                $stmt->execute();

		$res = array(
			"status" => "ok",
		);
                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->withStatus(200)
                        ->write(json_encode($res));
	}

	static function convertToPictureImpl($request, $response, $args, $jwt, $pid, $hidden=1) {
    		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);

		if (!$userid) {
            		return $response->withStatus(401);
		}

		$filename = $args['fileid'];
		$dir = Config::uploadDir() . "/files/" . $userid . "/";
		$file = null;

		// prevent ".." recursion attacks etc by looking only at existing files
 		foreach (scandir($dir) as $item) {
            		if ($item == "." || $item == "..")
                		continue;
			if ($item === $filename)
				$file = $userid . "/" . $item;
		}
		if ($file === null) {
            		return $response->withStatus(404);
		}

		// 1. file verschieben
		$sourcePath = Config::uploadDir() . "/files/" . $file;
		$targetPath = Config::imageDir() . "/" . $file;
		$targetDir = dirname($targetPath);
		if (!file_exists($targetDir))
			mkdir($targetDir);
		copy($sourcePath, $targetPath);
		if (file_exists($targetPath)) {
			unlink($sourcePath);
		}

		// 2. datenbankeintrag anlegen

                $db = \Cichlids\Db::get();

                $sql = "INSERT INTO user_cichlids_pictures (pid, tstamp, crdate, fe_user, image, hidden)
                        VALUES (:pid,
                                UNIX_TIMESTAMP(NOW()),
                                UNIX_TIMESTAMP(NOW()),
                                :userid,
				:image,
				:hidden
                        )";

                $stmt = $db->prepare($sql);
                $stmt->bindParam("pid", $pid, \PDO::PARAM_INT);
	
	    	$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);

                $stmt->bindParam("userid", $userid, \PDO::PARAM_INT);
                $stmt->bindParam("hidden", $hidden, \PDO::PARAM_INT);
		$dbpath = 'user_pics/' . $file;
                $stmt->bindParam("image", $dbpath, \PDO::PARAM_STR);
                $stmt->execute();
		$picuid = $db->lastInsertId();

		return $picuid;


	}


	static function listAvailFiles($request, $response, $args, $jwt) {
		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);

		if (!$userid) {
            		return $response->withStatus(500);
		}

		$dir = Config::uploadDir() . "/files/" . $userid . "/";
		$files = array();
 		foreach (scandir($dir) as $item) {
            		if ($item == "." || $item == "..")
                		continue;
			$files[] = array(
				"id" => $item,
				"filename" => $item,
				"image" => Upload::buildUploadImageMap($userid, $item),
			);
		}

            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
      			->write(json_encode($files));
	}

       	static function buildUploadImageMap($userid, $filename) {
		return array(
                       	'100'   =>      Upload::uploadImgPath($userid, $filename, 100),
                       	'200'   =>      Upload::uploadImgPath($userid, $filename, 200),
                       	'400'   =>      Upload::uploadImgPath($userid, $filename, 400),
		);
        }

        static function uploadImgPath($userid, $image, $width) {
                return '//www.cichlids.com/p/u/'.$width.'/'.$userid.'/'.$image;
        }

}

