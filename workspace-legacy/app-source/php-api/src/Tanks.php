<?php 
namespace Cichlids;

use \Cichlids\Db;
use \Cichlids\Users;
use \Cichlids\Comments;
use \Cichlids\Pictures;
use \Cichlids\Categories;

class Tanks {

	static function listTanks($request, $response, $args) {
		$sort = $request->getParam('sort', 'newest');
		switch($sort) {
			//case "views":
                        //case "popular":
				//$colsort = "views"; break;
                        //case "rating":
                                //// https://math.stackexchange.com/a/41513
                                //$colsort = "(rating_count/50) * rating + (1-rating_count/50) * 4.5";
                                //break;
			//case "newest":
                        default:
                                $colsort = "tstamp"; break;


		}
		$usersql = "";
		$userid = $request->getParam('user');
               	$hiddensql = " AND tank.hidden=0";
		if ($userid != "") {
			$usersql = "AND fe_user=:userid";
                        // check if logged in user

                        if (isset($_SERVER['HTTP_AUTHORIZATION'])) {
                                $authHeader = $_SERVER['HTTP_AUTHORIZATION'];
                                if (preg_match("/Bearer\s+(.*)$/i", $authHeader, $matches)) {
                                        $authToken = $matches[1];

   					$key = Config::Auth0JWTPubkey();
                                        $jwt = \Firebase\JWT\JWT::decode($authToken, $key, ["RS256"]);

                                        $loginuserid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);

                                        if ($loginuserid == $userid)
                                                $hiddensql = "";
                                }
                        }
                }


		$catsql = "";
		$catString = $request->getParam('category', 'all');
		$catid = \Cichlids\Categories::fromIdString($catString);
		if ($catid != null)
			$catsql = "AND category=:category";

       		$db = \Cichlids\Db::get();
    		$sql = "FROM user_cichlids_tanks tank
			WHERE tank.deleted=0 $hiddensql $usersql $catsql";
		$sql = "SELECT *, (SELECT COUNT(*) $sql) total_count $sql ORDER BY $colsort DESC LIMIT :offset,:count";
       		$stmt = $db->prepare($sql);

		$qoffset = intval($request->getParam('offset', 0));
		$qcount = min(20, intval($request->getParam('count', 20)));;

       		$stmt->bindParam("offset", $qoffset, \PDO::PARAM_INT);
       		$stmt->bindParam("count", $qcount, \PDO::PARAM_INT);
		if ($usersql)
			$stmt->bindParam("userid", $userid, \PDO::PARAM_INT);
		if ($catsql)
			$stmt->bindParam("category", $catid, \PDO::PARAM_INT);
       		$stmt->execute();
       		$tanks = $stmt->fetchAll(\PDO::FETCH_OBJ);
       		$db = null;

		$count = count($tanks) > 0 ? $tanks[0]->total_count : 0;

		if (false)
            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
      			->write(json_encode(array("dingel", $count, $qoffset, $qcount)));

		$res = array(
			"count" => $count,
			"tanks" => array_map(__NAMESPACE__.'\Tanks::convertShort', $tanks),
		);

            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
      			->write(json_encode($res));
	}

        static function countUserTanks($userid) {
                $db = \Cichlids\Db::get();
                $sql = "SELECT COUNT(*) as count FROM user_cichlids_tanks tank
                        WHERE tank.hidden=0 AND tank.deleted=0 AND fe_user=:userid";
                $stmt = $db->prepare($sql);
                $stmt->bindParam("userid", $userid, \PDO::PARAM_INT);
                $stmt->execute();
                $res = $stmt->fetch(\PDO::FETCH_OBJ);
                $db = null;
                return intval($res->count);
        } 

	static function listComments($request, $response, $args) {
		$uid = $args['tankid'];
		if ($uid == null) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(404);
		}

       		$db = \Cichlids\Db::get();
    		$sql = "SELECT * FROM user_cichlids_comments c WHERE
			type=2 AND item=:uid AND
			delete_tstamp=0 AND c.hidden=0 AND c.deleted=0 ORDER BY tstamp DESC LIMIT 0,20";

                $stmt = $db->prepare($sql);
                $stmt->bindParam("uid", $uid);
       		$stmt->execute();
       		$comments = $stmt->fetchAll(\PDO::FETCH_OBJ);
       		$db = null;

		$res = array_map(__NAMESPACE__.'\Comments::convert', $comments);
            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
      			->write(json_encode($res));
	}

	static function addTankFromPicture($request, $response, $args, $jwt) {
		$data = $request->getParsedBody();
		$slug = $data['pictureSlug'];
		$title = $data['title'];

		$picuid = Pictures::getUid($slug);
		if ($picuid == null) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(404);
		}
		$picture = Pictures::loadPicture($picuid);
		$loginuserid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);
		if ($picture->fe_user != $loginuserid) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(401);
		}

       		$db = \Cichlids\Db::get();
		$sql = "INSERT INTO user_cichlids_tanks(pid, tstamp, crdate, fe_user, hidden,
			title, image) VALUES (
				137,
                            	UNIX_TIMESTAMP(NOW()),
                            	UNIX_TIMESTAMP(NOW()),
				:userid,
				1,
				:title,
				:image
			)";
       		$stmt = $db->prepare($sql);
		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);
       		$stmt->bindParam("userid", $userid, \PDO::PARAM_INT);
       		$stmt->bindParam("title", $title, \PDO::PARAM_STR);
       		$stmt->bindParam("image", $picuid, \PDO::PARAM_STR);
       		$stmt->execute();

		$tankid = $db->lastInsertId();

                if ($tankid == "0") {
                        return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->withStatus(500);
                }

                $res = array(
                        "status" => "ok",
                        "id" => $tankid,
                );
                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->withStatus(200)
                        ->write(json_encode($res));
	}

	static function removeFromArrayString($ar, $id) {
		$splitted = preg_split("/,/", $ar);
		$removed = array_diff($splitted, array($id, null, 0));
		return implode(",", array_filter($removed));
	}

	static function swapPositions($ar, $old, $new) {
		$ar = preg_split("/,/", $ar);
		$idx = array_search($old, $ar);
		$ar[$idx] = $new;
		
		return implode(",", array_filter($ar));
	}

	static function deletePictureFromTank($request, $response, $args, $jwt) {
		$uid = $args['tankid'];
		$picuid = $args['pictureid'];
		$tank = Tanks::loadTank($uid, false);

                if ($tank == null) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(404);
                }

		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);

		$roles = $jwt->{"http://www.cichlids.com/roles"};

                if ($userid != $tank->fe_user && !(in_array("cichlids-admins", $roles) || in_array("cichlids-moderators", $roles))) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(401);
                }

		$tank_images = Tanks::removeFromArrayString($tank->tank_images, $picuid);
		$deco_images = Tanks::removeFromArrayString($tank->deco_images, $picuid);
		$tec_images = Tanks::removeFromArrayString($tank->tec_images, $picuid);

       		$db = \Cichlids\Db::get();
		$sql = "UPDATE user_cichlids_tanks SET tank_images=:tank_images, deco_images=:deco_images, tec_images=:tec_images where uid=:uid";

       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
       		$stmt->bindParam("tank_images", $tank_images, \PDO::PARAM_STR);
       		$stmt->bindParam("deco_images", $deco_images, \PDO::PARAM_STR);
       		$stmt->bindParam("tec_images", $tec_images, \PDO::PARAM_STR);
       		$stmt->execute();

                $res = "ok";

                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->withStatus(200)
                        ->write(json_encode($res));
	}

	static function setPictureAsMainImage($request, $response, $args, $jwt) {
		$uid = $args['tankid'];
		$picuid = $args['pictureid'];
		$tank = Tanks::loadTank($uid, false);

                if ($tank == null) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(404);
                }

		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);
		$roles = $jwt->{"http://www.cichlids.com/roles"};

                if ($userid != $tank->fe_user && in_array("cichlids-admins", $roles)) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(401);
                }

		$tank_images = Tanks::swapPositions($tank->tank_images, $picuid, $tank->image);

       		$db = \Cichlids\Db::get();
		$sql = "UPDATE user_cichlids_tanks SET image=:main_image, tank_images=:tank_images where uid=:uid";

       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
       		$stmt->bindParam("main_image", $picuid, \PDO::PARAM_INT);
       		$stmt->bindParam("tank_images", $tank_images, \PDO::PARAM_STR);
       		$stmt->execute();

                $res = "ok";

                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->withStatus(200)
                        ->write(json_encode($res));
	}

	static function addTank($request, $response, $args, $jwt) {
		$data = $request->getParsedBody();
		$title = $data['title'];
		$category = $data['category'];
		$dimensions = $data['dimensions'];

       		$db = \Cichlids\Db::get();
		$sql = "INSERT INTO user_cichlids_tanks(pid, tstamp, crdate, fe_user, hidden,
			category, title, width, height, depth, unit) VALUES (
				137,
                            	UNIX_TIMESTAMP(NOW()),
                            	UNIX_TIMESTAMP(NOW()),
				:userid,
				1,
				:category,
				:title,
				:width,
				:height,
				:depth,
				:unit
			)";
       		$stmt = $db->prepare($sql);
		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);
       		$stmt->bindParam("userid", $userid, \PDO::PARAM_INT);
       		$stmt->bindParam("category", $category, \PDO::PARAM_INT);
       		$stmt->bindParam("title", $title, \PDO::PARAM_STR);
       		$stmt->bindParam("width", $dimensions['length'], \PDO::PARAM_INT);
       		$stmt->bindParam("height", $dimensions['height'], \PDO::PARAM_INT);
       		$stmt->bindParam("depth", $dimensions['width'], \PDO::PARAM_INT);
       		$stmt->bindParam("unit", $dimensions['unit'], \PDO::PARAM_STR);
       		$stmt->execute();

		$tankid = $db->lastInsertId();

                if ($tankid == "0") {
                        return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->withStatus(500);
                }

                $res = array(
                        "status" => "ok",
                        "id" => $tankid,
                );
                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->withStatus(200)
                        ->write(json_encode($res));
	}

        static function editPutTank($request, $response, $args, $jwt) {
                $uid = $args['tankid'];

                $tank = Tanks::loadTank($uid, false);
                if ($tank == null) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(404);
                }

		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);
                if ($tank->fe_user !== $userid) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(401);
                }
                $data = json_decode($request->getBody());

                $tstamp = $tank->hidden ? time() : $tank->tstamp;
		$pid = $tank->hidden ? 137 : 15;

                $db = \Cichlids\Db::get();
                $sql = "UPDATE user_cichlids_tanks SET 
				pid=:pid,
				title=:title,
				description=:description,
				category=:category,
				unit=:unit,
				width=:width,
				height=:height,
				depth=:depth,
				gravel=:gravel,
				plants=:plants,
				more_deco=:moredeco,
				light=:light,
				filtration=:filtration,
				more_tec=:moretec,
				hidden=:hidden,
				tstamp=:tstamp
			WHERE uid=:uid";

                $hidden = $data->published ? 0 : 1;
                $stmt = $db->prepare($sql);
                $stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
                $stmt->bindParam("pid", $pid, \PDO::PARAM_INT);
                $stmt->bindParam("title", $data->title, \PDO::PARAM_STR);
                $stmt->bindParam("description", $data->description, \PDO::PARAM_STR);

		$categoryId = Categories::fromIdString($data->category->id);
                $stmt->bindParam("category", $categoryId == null ? 0 : $categoryId, \PDO::PARAM_INT);

                $stmt->bindParam("unit", $data->dimensions->unit, \PDO::PARAM_STR);
                $stmt->bindParam("width", $data->dimensions->width == NULL ? 0 : $data->dimensions->width, \PDO::PARAM_STR);
                $stmt->bindParam("height", $data->dimensions->height == NULL ? 0 : $data->dimensions->height, \PDO::PARAM_STR);
                $stmt->bindParam("depth", $data->dimensions->depth == NULL ? 0 : $data->dimensions->depth, \PDO::PARAM_STR);
                $stmt->bindParam("gravel", $data->gravel, \PDO::PARAM_STR);
                $stmt->bindParam("plants", $data->plants, \PDO::PARAM_STR);
                $stmt->bindParam("moredeco", $data->otherDecoration, \PDO::PARAM_STR);
                $stmt->bindParam("light", $data->light, \PDO::PARAM_STR);
                $stmt->bindParam("filtration", $data->filtration, \PDO::PARAM_STR);
                $stmt->bindParam("moretec", $data->otherTec, \PDO::PARAM_STR);

                $stmt->bindParam("hidden", $hidden, \PDO::PARAM_INT);
                $stmt->bindParam("tstamp", $tstamp, \PDO::PARAM_INT);
                $stmt->execute();

                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->write(json_encode("ok"));
        }

	static function postComment($request, $response, $args, $jwt) {
		$uid = $args['tankid'];
		$ipAddress = $request->getAttribute('ip_address');

		$data = $request->getParsedBody();
		$rating = $data['rating'] ? $data['rating'] : 0;
		$body = $data['body'];

       		$db = \Cichlids\Db::get();
		$sql = "INSERT INTO user_cichlids_comments (pid, tstamp, crdate, type, item, rating, poster, ip, note, fe_user)
                        VALUES (52, 
                            	UNIX_TIMESTAMP(NOW()),
                            	UNIX_TIMESTAMP(NOW()),
				2,			-- type 2 = tank
				:uid,
				:rating,
				'', 			-- poster name, aus alten zeiten
				:ip,
				:note,
				:userid
			)";
       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
       		$stmt->bindParam("rating", $rating, \PDO::PARAM_INT);
       		$stmt->bindParam("note", $body, \PDO::PARAM_STR | \PDO::PARAM_NULL);
		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);
       		$stmt->bindParam("userid", $userid, \PDO::PARAM_INT);
       		$stmt->bindParam("ip", $ipAddress, \PDO::PARAM_STR);
       		$stmt->execute();

            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
			->withStatus(200)
      			->write(json_encode("ok"));
	}


	static function changeType($request, $response, $args, $jwt) {

		$roles = $jwt->{"http://www.cichlids.com/roles"};
  		if (!(in_array("cichlids-admins", $roles) || in_array("cichlids-moderators", $roles))) {
			return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(401);
		}

		$slug = $args['slug'];
		$uid = Pictures::getUid($slug);
		if ($uid == null) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(404);
		}

		$type = json_decode($request->getBody());
		if ($type == "") {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(403)
				->write(json_encode("Type not allowed: " . $type));
		}
		switch ($type) {
			case "CICHLIDS":
				$type = 21;
				break;
			case "TANKS":
				$type = 29;
				break;
			case "OFFTOPIC":
				$type = 109;
				break;
			default:
				return $response
					->withHeader('Access-Control-Allow-Origin', '*')
					->withStatus(403)
					->write(json_encode("Unkonwn type: " . $type));
		}

       		$db = \Cichlids\Db::get();
		$sql = "UPDATE user_cichlids_pictures SET pid=:type where uid=:uid";

       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
       		$stmt->bindParam("type", $type, \PDO::PARAM_INT);
       		$stmt->execute();

            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
			->withStatus(200)
      			->write(json_encode("ok"));
	}


	static function loadTank($uid, $hideHidden=true) {
		if ($uid == null) return null;
     		$hiddensql = "";
                if ($hideHidden) $hiddensql = "AND tank.hidden=0";

                $sql = "SELECT * FROM user_cichlids_tanks tank 
                        WHERE tank.deleted=0 $hiddensql AND uid=:uid";
                $db = \Cichlids\Db::get();
                $stmt = $db->prepare($sql);
                $stmt->bindParam("uid", $uid);
                $stmt->execute();
                $res = $stmt->fetch(\PDO::FETCH_OBJ);
                $db = null;
                return $res;
        }

	static function viewTank($request, $response, $args) {
		$uid = $args['tankid'];
		$tank = Tanks::loadTank($uid);
		if ($tank == null) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(404);
		}
            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
      			->write(json_encode(Tanks::convert($tank)));
	}

        static function deleteTank($request, $response, $args, $jwt) {
		$uid = $args['tankid'];
		$tank = Tanks::loadTank($uid);

                if ($tank == null) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(404);
                }

		$roles = $jwt->{"http://www.cichlids.com/roles"};
                if (!(in_array("cichlids-admins", $roles) || in_array("cichlids-moderators", $roles))) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(401);
                }

		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);
                $data = json_decode($request->getBody());

                $db = \Cichlids\Db::get();
                $sql = "UPDATE user_cichlids_tanks SET deleted=1, hidden=1 WHERE uid=:uid";
                $stmt = $db->prepare($sql);
                $stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
                $stmt->execute();

                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->write(json_encode("ok"));
        }

        static function editGetTank($request, $response, $args, $jwt) {
		$uid = $args['tankid'];
		$tank = Tanks::loadTank($uid, false);
		if ($tank == null) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(404);
		}
		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);
                if ($tank->fe_user !== $userid) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(401);
                }
                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->write(json_encode(Tanks::convert($tank)));
        }



	static function getUid($slug) {
		if (substr($slug, -5) === ".html")
			$slug = substr($slug, 0, -5);
                $sql = "SELECT * FROM tx_realurl_uniqalias a WHERE 
			tablename='user_cichlids_pictures' AND field_id='uid' AND value_alias=:slug LIMIT 1";
                $db = \Cichlids\Db::get();
                $stmt = $db->prepare($sql);
                $stmt->bindParam("slug", $slug);
                $stmt->execute();
                $res = $stmt->fetch(\PDO::FETCH_OBJ);
                $db = null;
		return $res->value_id;
	}

	static function imgPath($image, $width) {
		return '//www.cichlids.com/p/n/'.$width.'/'.urlencode(preg_replace('#user_pics/(.*)#', '$1', $image));
	}

	static function convertShort($tank) {
		return Tanks::convert($tank, false);
	}

	static function convert($tank, $full = true, $internal = false) {
		$res = Array();
		$uid = $tank->uid;
		$res['id'] = $uid;
		$res['tstamp'] = $tank->tstamp;
		$res['crdate'] = $tank->crdate;
                $res['published'] = $tank->hidden == 0 ? true : false;
		$res['title'] = $tank->title;
		$res['description'] = $tank->description;
		$res['_links'] = array(
			'self'		=>	'/tanks/' . urlencode($uid),
			'comments'	=> 	'/tanks/' . urlencode($uid) . '/comments',
		);
		$userid = $tank->fe_user;
		$user = \Cichlids\Users::loadUser($userid, $internal);
		$res['user'] = $user;

		$res['mainImage'] = Tanks::mainImage($tank);
		$res['images'] = array_values(array_filter(array_map(__NAMESPACE__.'\Tanks::imageForPicUid', Tanks::splitPics($tank->tank_images))));

		$res['category'] = Categories::convert(Categories::loadCategory($tank->category));


		if (!$full) return $res;

		$res['dimensions'] = array(
			"depth" => $tank->depth == 0 ? null : $tank->depth,
			"height" => $tank->height == 0 ? null : $tank->height,
			"width" => $tank->width == 0 ? null : $tank->width,
			"unit" => $tank->unit == null ? "inches" : "cm",
		);

		$res['gravel'] = $tank->gravel;
		$res['plants'] = $tank->plants;
		$res['otherDecoration'] = $tank->more_deco;
		$res['decorationImages'] = array_values(array_filter(array_map(__NAMESPACE__.'\Tanks::imageForPicUid', Tanks::splitPics($tank->deco_images))));

		$res['light'] = $tank->light;
		$res['light_duration'] = $tank->light_duration;
		$res['filtration'] = $tank->filtration;
		$res['otherTec'] = $tank->more_tec;
		$res['tecImages'] = array_values(array_filter(array_map(__NAMESPACE__.'\Tanks::imageForPicUid', Tanks::splitPics($tank->tec_images))));

		return $res;
	}

	static function imageForPicUid($uid) {
		$picture = Pictures::loadPicture($uid, false, false);
		if ($picture == null || $picture->image == "") return null;
		$res = Pictures::buildImageMap($picture);
		return array(
			//"pictureSlug" => Pictures::getSlug($uid, true),
			"pictureId" => $uid,
			"images" => $res,
		);
	}

	static function mainImage($tank) {
		if ($tank->image != "") {
			$res = Tanks::imageForPicUid($tank->image);
			if ($res != null)
				return $res;
		}
		$others = Tanks::splitPics($tank->tank_images);
		$deco = Tanks::splitPics($tank->deco_images);
		$tec = Tanks::splitPics($tank->tec_images);
		if (count($others) > 0) {
			$res = Tanks::imageForPicUid($others[0]);
			if ($res != null)
				return $res;
		}
		if (count($deco) > 0) {
			$res = Tanks::imageForPicUid($deco[0]);
			if ($res != null)
				return $res;
		}
		if (count($tec) > 0) {
			$res = Tanks::imageForPicUid($tec[0]);
			if ($res != null)
				return $res;
		}
		return null;
	}

	static function splitPics($val) {
		$others = explode(',', $val);
		$others = array_filter($others);
		$others = array_values($others);
		return $others;
	}

}
