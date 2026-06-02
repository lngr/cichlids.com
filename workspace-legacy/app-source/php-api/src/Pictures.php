<?php 
namespace Cichlids;

use \Cichlids\Db;
use \Cichlids\Users;
use \Cichlids\Comments;
use \Rhumsaa\Uuid\Uuid;
use Hashids\Hashids;

class Pictures {


	static function listPictures($request, $response, $args) {
		$start = microtime(true);

		$sort = $request->getParam('sort', 'newest');
		switch($sort) {
			case "views":
			case "popular":
				$colsort = "views";
				break;
			case "rating":
				// https://math.stackexchange.com/a/41513
				$colsort = "(rating_count/50) * rating + (1-rating_count/50) * 4.5";
				break;
			case "newest":
			default:
				$colsort = "tstamp"; break;
		}

		$hiddensql = " AND pic.hidden=0";

		$usersql = "";
		$userid = $request->getParam('user');

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

		$type = $request->getParam('type', 'all');
		switch($type) {
			case "cichlids":
				$typeid = '21';
				break;
			case "tanks":
				$typeid = '29';
				break;
			case "offtopic":
				$typeid = '109';
				break;
			default:
				$typeid = '21,29';
					// $sort == "newest" ?  '21,29,109' : // offtopic nur bei newest anzeigen
				if ($userid != "") {
					$typeid .= ",109"; // auf profile page vom user auch offtopic anzeigen
				}
				break;
		}

       		$db = \Cichlids\Db::get();
    		$sql = "FROM user_cichlids_pictures pic
			WHERE delete_tstamp=0 $hiddensql AND pic.deleted=0 AND pid IN($typeid) $usersql";
		$sql = "SELECT *, (SELECT COUNT(*) $sql) total_count $sql ORDER BY $colsort DESC LIMIT :offset,:count";
       		$stmt = $db->prepare($sql);

		$qoffset = intval($request->getParam('offset', 0));
		$qcount = min(20, intval($request->getParam('count', 20)));;

       		$stmt->bindParam("offset", $qoffset, \PDO::PARAM_INT);
       		$stmt->bindParam("count", $qcount, \PDO::PARAM_INT);
		if ($usersql)
			$stmt->bindParam("userid", $userid, \PDO::PARAM_INT);
       		$stmt->execute();
       		$pictures = $stmt->fetchAll(\PDO::FETCH_OBJ);
       		$db = null;

		$count = count($pictures) > 0 ? $pictures[0]->total_count : 0;

		//error_log("List pictures, query time: " . (microtime(true) - $start));

		$res = array(
			"count" => $count,
			"pictures" => array_map(__NAMESPACE__.'\Pictures::convert', $pictures),
		);

		//error_log("List pictures, total time: " . (microtime(true) - $start));
            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
      			->write(json_encode($res));
	}

	static function countUserPictures($userid) {
       		$db = \Cichlids\Db::get();
    		$sql = "SELECT COUNT(*) as count FROM user_cichlids_pictures pic
			WHERE delete_tstamp=0 AND pic.hidden=0 AND pic.deleted=0 AND pid IN(21,29,109) AND fe_user=:userid";
       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("userid", $userid, \PDO::PARAM_INT);
       		$stmt->execute();
       		$res = $stmt->fetch(\PDO::FETCH_OBJ);
       		$db = null;
		return intval($res->count);
	}

	static function countUserPictureViews($userid) {
       		$db = \Cichlids\Db::get();
    		$sql = "SELECT SUM(views) as count FROM user_cichlids_pictures pic
			WHERE delete_tstamp=0 AND pic.hidden=0 AND pic.deleted=0 AND pid IN(21,29,109) AND fe_user=:userid";
       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("userid", $userid, \PDO::PARAM_INT);
       		$stmt->execute();
       		$res = $stmt->fetch(\PDO::FETCH_OBJ);
       		$db = null;
		return intval($res->count);
	}


	static function listComments($request, $response, $args, $jwt) {
		$withDeleted = false;

		if ($jwt != null && property_exists($jwt, 'http://www.cichlids.com/roles')) {
			$roles = $jwt->{"http://www.cichlids.com/roles"};
			if ($roles != null && (in_array("cichlids-admins", $roles))) {
				$withDeleted = true;
			}
		}

		$slug = $args['slug'];
		$uid = Pictures::getUid($slug);
		if ($uid == null) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(404);
		}

       		$db = \Cichlids\Db::get();
    		$sql = "SELECT * FROM user_cichlids_comments c WHERE
			type=1 AND item=:uid ";
		if (!$withDeleted) {
			$sql .= " AND c.hidden=0 AND delete_tstamp=0 AND c.deleted=0";
		}
		$sql .= " ORDER BY tstamp ASC";

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

	static function postComment($request, $response, $args, $jwt) {
		$jwtuserid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);

		$slug = $args['slug'];
		$uid = Pictures::getUid($slug);
		if ($uid == null) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(404);
		}

		$ipAddress = $request->getAttribute('ip_address');

		$data = $request->getParsedBody();
		$rating = $data['rating'] ? $data['rating'] : 0;
		$body = $data['body'];
		if (!$body) $body = "";

		if (strpos($body, 'dogstrainingtools.com') !== false) {
            		return $response
				->withHeader('Content-Type', 'application/json')
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(200)
      				->write(json_encode("ok"));
		}

       		$db = \Cichlids\Db::get();
		$sql = "INSERT INTO user_cichlids_comments (pid, tstamp, crdate, type, item, rating, poster, ip, note, fe_user)
                        VALUES (52, 
                            	UNIX_TIMESTAMP(NOW()),
                            	UNIX_TIMESTAMP(NOW()),
				1,			-- type 1 = cichlids
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
       		$stmt->bindParam("userid", $jwtuserid, \PDO::PARAM_INT);
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

		$pid = Pictures::typeToPid($type);
		if ($pid == null) {
			return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(403)
				->write(json_encode("Unkonwn type: " . $type));
		}

       		$db = \Cichlids\Db::get();
		$sql = "UPDATE user_cichlids_pictures SET pid=:type where uid=:uid";

       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
       		$stmt->bindParam("type", $pid, \PDO::PARAM_INT);
       		$stmt->execute();

            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
			->withStatus(200)
      			->write(json_encode("ok"));
	}


	static function typeToPid($type) {
		switch ($type) {
			case "CICHLIDS": return 21;
			case "TANKS": return 29;
			case "OFFTOPIC": return 109;
			default: return null;
		}
	}

	static function loadPicture($uid, $hideHidden=true, $hideDeleted=true) {
		if ($uid == null) return null;
		$hiddensql = "";
		if ($hideHidden) $hiddensql .= " AND pic.hidden=0";
		if ($hideDeleted) $hiddensql .= " AND pic.deleted=0";
                $sql = "SELECT * FROM user_cichlids_pictures pic WHERE uid=:uid $hiddensql";
                $db = \Cichlids\Db::get();
                $stmt = $db->prepare($sql);
                $stmt->bindParam("uid", $uid);
                $stmt->execute();
                $res = $stmt->fetch(\PDO::FETCH_OBJ);
                $db = null;
                return $res;
        }


	static function viewPicture($request, $response, $args) {
		$slug = $args['slug'];
		$uid = Pictures::getUid($slug);
		$picture = Pictures::loadPicture($uid);
		if ($picture == null) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(404);
		}

                $sql = "UPDATE user_cichlids_pictures SET views=views+1 WHERE uid=:uid";
                $db = \Cichlids\Db::get();
                $stmt = $db->prepare($sql);
                $stmt->bindParam("uid", $uid);
                $stmt->execute();
                $db = null;

            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
      			->write(json_encode(Pictures::convert($picture)));
	}


	static function editGetPicture($request, $response, $args, $jwt) {
		$jwtuserid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);
		$slug = $args['slug'];
		$uid = Pictures::getUid($slug);
		$picture = Pictures::loadPicture($uid, false);
		if ($picture == null) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(404);
		}
		$userid = $jwtuserid;
		if ($picture->fe_user !== $userid) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(401);
		}
            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
      			->write(json_encode(Pictures::convert($picture)));
	}

	static function editPutPicture($request, $response, $args, $jwt) {
		$slug = $args['slug'];
		$uid = Pictures::getUid($slug);
		$picture = Pictures::loadPicture($uid, false);
		if ($picture == null) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(404);
		}
		
		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);
		if ($picture->fe_user !== $userid) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(401);
		}
		$data = json_decode($request->getBody());

		$tstamp = $picture->hidden ? time() : $picture->tstamp;

       		$db = \Cichlids\Db::get();
		$sql = "UPDATE user_cichlids_pictures SET pid=:pid, title=:title, description=:description, hidden=:hidden, tstamp=:tstamp where uid=:uid";

		$hidden = $data->published ? 0 : 1;
       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
       		$stmt->bindParam("pid", Pictures::typeToPid($data->type), \PDO::PARAM_INT);
       		$stmt->bindParam("title", $data->title, \PDO::PARAM_STR);
       		$stmt->bindParam("description", $data->description, \PDO::PARAM_STR);
       		$stmt->bindParam("hidden", $hidden, \PDO::PARAM_INT);
       		$stmt->bindParam("tstamp", $tstamp, \PDO::PARAM_INT);
       		$stmt->execute();

            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
      			->write(json_encode("ok"));
	}


	static function deletePicture($request, $response, $args, $jwt) {
		$slug = $args['slug'];
		$uid = Pictures::getUid($slug);
		$picture = Pictures::loadPicture($uid, false);
		if ($picture == null) {
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

			// error_log(print_r($data->reason,true));

       		$db = \Cichlids\Db::get();
		$sql = "UPDATE user_cichlids_pictures SET deleted=1, hidden=1, delete_tstamp=UNIX_TIMESTAMP(NOW()),
			delete_reason=:reason, delete_user=:user WHERE uid=:uid";
       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
       		$stmt->bindParam("user", $userid, \PDO::PARAM_INT);
       		$stmt->bindParam("reason", $data->reason, \PDO::PARAM_STR);
       		$stmt->execute();

            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
      			->write(json_encode("ok"));
	}


	static function discardPicture($request, $response, $args, $jwt) {
		$slug = $args['slug'];
		$uid = Pictures::getUid($slug);
		$picture = Pictures::loadPicture($uid, false);
		if ($picture == null) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(404);
		}

		if ($picture->hidden == 0) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(401);
		}

		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);

		if ($picture->fe_user != $userid) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(401);
		}
		
       		$db = \Cichlids\Db::get();
		$sql = "UPDATE user_cichlids_pictures SET deleted=1, hidden=1, delete_tstamp=UNIX_TIMESTAMP(NOW()),
			delete_reason='Discarded', delete_user=:user WHERE uid=:uid";
       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
       		$stmt->bindParam("user", $userid, \PDO::PARAM_INT);
       		$stmt->execute();

            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
      			->write(json_encode("ok"));
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

	static function makeSlug($uid) {
                $sql = "SELECT * FROM tx_realurl_uniqalias a WHERE 
			tablename='user_cichlids_pictures' AND field_id='uid' AND value_id=:uid LIMIT 1";
                $db = \Cichlids\Db::get();
                $stmt = $db->prepare($sql);
                $stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
                $stmt->execute();
                $res = $stmt->fetch(\PDO::FETCH_OBJ);
                $db = null;

		if ($res) return $res->value_alias;

		//$uuid = Uuid::uuid4()->toString();
		$hashids = new Hashids('tl2j342;34');
		$slug = $hashids->encode($uid);
		
		$sql = "INSERT INTO tx_realurl_uniqalias (tablename, tstamp, field_id, field_alias, value_alias, value_id) VALUES (
			'user_cichlids_pictures',
                        UNIX_TIMESTAMP(NOW()),
			'uid',
			'title',
			:slug,
			:uid
		)";
                $db = \Cichlids\Db::get();
                $stmt = $db->prepare($sql);
                $stmt->bindParam("uid", $uid, \PDO::PARAM_STR);
                $stmt->bindParam("slug", $slug, \PDO::PARAM_STR);
                $stmt->execute();
                $db = null;
		return $slug;
	}

	static function getSlug($uid, $createIfNotExists = false) {
                $sql = "SELECT * FROM tx_realurl_uniqalias a WHERE 
			tablename='user_cichlids_pictures' AND field_id='uid' AND value_id=:uid
			LIMIT 1";
                $db = \Cichlids\Db::get();
                $stmt = $db->prepare($sql);
                $stmt->bindParam("uid", $uid);
                $stmt->execute();
                $res = $stmt->fetch(\PDO::FETCH_OBJ);
                $db = null;

		if ($res->value_alias != null)
			return $res->value_alias;
		if ($createIfNotExists)
			return Pictures::makeSlug($uid);
		return null;
	}

	static function imgPath($image, $width) {
		return '//www.cichlids.com/p/n/'.$width.'/'.urlencode(preg_replace('#user_pics/(.*)#', '$1', $image));
	}

	static function convert($picture, $internal = false) {
		$res = Array();
		if ($picture == null) {
			return null;
		}
		$uid = $picture->uid;
		$slug = self::getSlug($uid) . ".html";

		$res['id'] = $uid;
		$res['slug'] = $slug;
		$res['title'] = $picture->title;
		$res['tstamp'] = $picture->tstamp;
		$res['description'] = $picture->description;
		$res['views'] = $picture->views;
		$res['rating'] = $picture->rating;
		$res['published'] = $picture->hidden == 0 ? true : false;
		$res['ratingCount'] = $picture->rating_count;
		$res['image'] = Pictures::buildImageMap($picture);
		$res['_links'] = array(
			'self'		=>	'/pictures/' . urlencode($slug),
			'comments'	=> 	'/pictures/' . urlencode($slug) . '/comments',
		);
		$userid = $picture->fe_user;
		$user = \Cichlids\Users::loadUser($userid, $internal);
		$res['user'] = $user;

		switch ($picture->pid) {
			case 21: $res['type'] = "CICHLIDS"; break;
			case 29: $res['type'] = "TANKS"; break;
			case 62: $res['type'] = "TANKS_TEC"; break;
			case 63: $res['type'] = "TANKS_DECO"; break;
			case 109: $res['type'] = "OFFTOPIC"; break;
			case 131: $res['type'] = "CONTEST"; break;
			default: $res['type'] = "UNKNOWN"; break;
		}
		return $res;
	}

	static function buildImageMap($picture) {
		return array(
			'100'	=> 	Pictures::imgPath($picture->image, 100),
			'200'	=> 	Pictures::imgPath($picture->image, 200),
			'400'	=> 	Pictures::imgPath($picture->image, 400),
			'800'	=> 	Pictures::imgPath($picture->image, 800),
			'1600'	=> 	Pictures::imgPath($picture->image, 1600),
		);
	}

}
