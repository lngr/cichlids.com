<?php 
namespace Cichlids;

use \Cichlids\Db;
use \Cichlids\Users;
use \Cichlids\Pictures;

class Comments {

	static function deleteComment($request, $response, $args, $jwt) {
		$roles = $jwt->{"http://www.cichlids.com/roles"};
		if (!(in_array("cichlids-admins", $roles) || in_array("cichlids-moderators", $roles))) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(401);
		}

		$uid = $args['id'];
		if ($uid == null || $uid == 0) {
            		return $response
				->withHeader('Access-Control-Allow-Origin', '*')
				->withStatus(404);
		}

                $data = $request->getParsedBody();
                $reason = $data['reason'];

		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);

       		$db = \Cichlids\Db::get();
		$sql = "UPDATE user_cichlids_comments SET hidden=1, delete_tstamp=UNIX_TIMESTAMP(NOW()),
				delete_reason=:reason, delete_user=:userid WHERE uid=:uid";
       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
       		$stmt->bindParam("reason", $reason, \PDO::PARAM_STR | \PDO::PARAM_NULL);

       		$stmt->bindParam("userid", $userid, \PDO::PARAM_INT);

      		$stmt->execute();

            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
			->withStatus(200)
      			->write(json_encode("ok"));
	}

        static function countUserComments($userid) {
                $db = \Cichlids\Db::get();
                $sql = "SELECT COUNT(*) as count FROM user_cichlids_comments c
                        WHERE c.hidden=0 AND c.deleted=0 AND c.delete_tstamp=0 AND note != '' AND fe_user=:userid";
                $stmt = $db->prepare($sql);
                $stmt->bindParam("userid", $userid, \PDO::PARAM_INT);
                $stmt->execute();
                $res = $stmt->fetch(\PDO::FETCH_OBJ);
                $db = null;
                return intval($res->count);
        } 


	static function listComments($request, $response, $args) {
       		$db = \Cichlids\Db::get();

		$sql = "SELECT * FROM user_cichlids_comments WHERE note <> '' AND hidden=0 AND deleted=0 ORDER BY tstamp DESC LIMIT :offset,:count";
       		$stmt = $db->prepare($sql);

		$qoffset = intval($request->getParam('offset', 0));
		$qcount = min(20, intval($request->getParam('count', 20)));;

       		$stmt->bindParam("offset", $qoffset, \PDO::PARAM_INT);
       		$stmt->bindParam("count", $qcount, \PDO::PARAM_INT);

       		$stmt->execute();

		$comments = $stmt->fetchAll(\PDO::FETCH_OBJ);
       		$db = null;

		$res = array_map(__NAMESPACE__.'\Comments::convert', $comments);

            	return $response
			->withHeader('Content-Type', 'application/json')
			->withHeader('Access-Control-Allow-Origin', '*')
      			->write(json_encode($res));
	}

       static function convert($comment, $internal = false) {
                $res = Array();
                $uid = $comment->uid;
                $res['id'] = "$uid";
                $res['tstamp'] = $comment->tstamp;
                $res['body'] = $comment->note;
                $res['rating'] = intval($comment->rating);
		$res['deleted'] = $comment->deleted > 0 || $comment->hidden > 0;

		$res['type'] = $comment->type == 1 ? "picture" : "tank";

		$res['parent'] = $res['type'] == "tank" ?
			$comment->item :
			Pictures::getSlug($comment->item);

		if ($internal) {
			if ($res['type'] == "tank") {
				$tank = Tanks::loadTank($comment->item);
				$res['parentTank'] = Tanks::convert($tank, true, true);
			}
			if ($res['type'] == "picture") {
				$picture = Pictures::loadPicture($comment->item);
				$res['parentPicture'] = Pictures::convert($picture, true);
			}
		}

                $userid = $comment->fe_user;
                $user = \Cichlids\Users::loadUser($userid, $internal);
                $res['poster'] = $user;

                return $res;
        }

       static function convertInternal($comment) {
		return Comments::convert($comment, true);
        }
}
