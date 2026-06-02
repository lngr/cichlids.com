<?php 
namespace Cichlids;

use \Cichlids\Db;
use \Cichlids\Util;

class Users {
	static function listUsers($request, $response, $args) {
    		$sql = "SELECT * FROM user_cichlids_pictures pic
			WHERE delete_tstamp=0 AND pic.hidden=0 AND pic.deleted=0
			ORDER BY pic.tstamp DESC LIMIT 0,20";
       		$db = \Cichlids\Db::get();
       		$stmt = $db->prepare($sql);
       		//$stmt->bindParam("query", $query);
       		$stmt->execute();
       		$pictures = $stmt->fetchAll(\PDO::FETCH_OBJ);
       		$db = null;

            	return $response
			->withHeader('Content-Type', 'application/json')
      			->write(json_encode(
				array_map(__NAMESPACE__.'\Pictures::convert', $pictures)));
	}

        static function viewUser($request, $response, $args) {
                $userid = $args['userid'];
                $user = Users::loadUser($userid);
                if ($user == null) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(404);
                }
                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->write(json_encode($user));
        }

	static function loadUser($uid, $internal = false) {
    		$sql = "SELECT * FROM fe_users f WHERE disable=0 AND uid=:uid LIMIT 1";
       		$db = \Cichlids\Db::get();

       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("uid", $uid, \PDO::PARAM_INT);
       		$stmt->execute();
       		$user = $stmt->fetch(\PDO::FETCH_OBJ);
       		$db = null;

		return \Cichlids\Users::convert($user, $internal);
	}

	static function loadUserByUsername($username) {
    		$sql = "SELECT * FROM fe_users f WHERE disable=0 AND deleted=0 AND username=:username LIMIT 1";
       		$db = \Cichlids\Db::get();
       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("username", $username);
       		$stmt->execute();
       		$user = $stmt->fetch(\PDO::FETCH_OBJ);
       		$db = null;
		return \Cichlids\Users::convert($user);
	}

	static function convert($user, $internal = false) {
		$res = Array();
		if ($user == null) return $res;
		$res['uid'] = $user->uid;
		$res['username'] = $user->username;
		$res['name'] = $user->name;
		$res['city'] = $user->city;
		$res['country'] = $user->static_info_country;

		if ($internal) {
			$res['email'] = $user->email;
		} else {
			if ($user->user_cichlids_profile_image > 0)
				$res['profileImage'] = Tanks::imageForPicUid($user->user_cichlids_profile_image);
			if ($user->user_cichlids_avatar_image > 0)
				$res['avatarImage'] = Tanks::imageForPicUid($user->user_cichlids_avatar_image);
			else if ($user->user_cichlids_auth0_image != '') {
				$res['avatarImage'] = array(
                        		"pictureId" => 0,
                        		"images" => array(
						1600 => $user->user_cichlids_auth0_image,
					),
                		);
			}
		}

		return $res;
	}

	static function loadStats($user, $res) {
		$res['numPictures'] = \Cichlids\Pictures::countUserPictures($user->uid);
		$res['numViews'] = \Cichlids\Pictures::countUserPictureViews($user->uid);
		$res['numTanks'] = \Cichlids\Tanks::countUserTanks($user->uid);
		$res['numPosts'] = \Cichlids\Comments::countUserComments($user->uid);
		return $res;
	}

	// unused ?? sicher? ist in index.php
	static function auth0Login($request, $response, $args) {

        	$authUser = $request->getHeader('PHP_AUTH_USER')[0];
        	$authPass = $request->getHeader('PHP_AUTH_PW')[0];

    		$sql = "SELECT * FROM fe_users f 
			WHERE disable=0 AND deleted=0 
				AND LOWER(email)=:email 
				AND LOWER(password)=:password

			ORDER BY uid ASC
			LIMIT 1";

       		$db = \Cichlids\Db::get();
       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("email", $authUser);
       		$stmt->bindParam("password", $authPass);
       		$stmt->execute();
       		$user = $stmt->fetch(\PDO::FETCH_OBJ);

		if ($user == null)
		{
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(401);
                }

		$res = Array();
		$res['id'] = $user->uid;
		$res['nickname'] = $user->username;
		$res['email'] = $user->email;

                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->write(json_encode($res));
	}

	// unused ?? sicher? ist in index.php
        static function auth0getByEmail($request, $response, $args) {
                $email = $args['email'];

    		$sql = "SELECT * FROM fe_users f WHERE disable=0 AND deleted=0 AND email=:email ORDER BY uid ASC LIMIT 1";
       		$db = \Cichlids\Db::get();
       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("email", $email);
       		$stmt->execute();
       		$user = $stmt->fetch(\PDO::FETCH_OBJ);
       		$db = null;

                if ($user == null) {
                        return $response
                                ->withHeader('Access-Control-Allow-Origin', '*')
                                ->withStatus(404);
                }

		$res = \Cichlids\Users::convertAuth0($user);

                return $response
                        ->withHeader('Content-Type', 'application/json')
                        ->withHeader('Access-Control-Allow-Origin', '*')
                        ->write(json_encode($res));
        }

	static function convertAuth0($user) {
		$res = Array();
		if ($user == null) return $res;
		$res['user_id'] = $user->uid;
		$res['nickname'] = $user->username;
		$res['email'] = $user->email;
		$res['name'] = $user->name;
		$res['given_name'] = $user->first_name;
		$res['family_name'] = $user->last_name;
		return $res;
	}

	static function mapJwtSubjectToUserId($jwt) {
       		$db = \Cichlids\Db::get();

		// erst gucken, ob schon ein Eintrag existiert
                $sql = "SELECT * FROM fe_users_auth0 WHERE sub=:sub";
       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("sub", $jwt->sub);
       		$stmt->execute();
       		$map = $stmt->fetch(\PDO::FETCH_OBJ);

		if ($map != null) {
			return $map->user_id;
		}

		$user = null;

		if ($user == null) {
			// jetzt gucken, ob zu dieser E-Mail-Adresse schon ein Eintrag existiert
                	$sql = "SELECT * FROM fe_users WHERE email=:email AND disable=0 AND deleted=0 ORDER BY crdate ASC";
       			$stmt = $db->prepare($sql);
       			$stmt->bindParam("email", strtolower($jwt->email));
       			$stmt->execute();
       			$user = $stmt->fetch(\PDO::FETCH_OBJ);
		}

		// sonst evtl. schonmal mit Facebook angemeldet? (Google geht leider nicht, weil Google openid auf oauth umgestellt hat
		if ($user == null && substr($jwt->sub, 0, 9)) { // "facebook|"
			$fbid = substr($jwt->sub, 9);
                	$sql = "SELECT * FROM fe_users WHERE tx_dixeasylogin_openid=:oauth";
       			$stmt = $db->prepare($sql);
			$oauth = "facebook-$fbid";
       			$stmt->bindParam("oauth", $oauth);
       			$stmt->execute();
       			$user = $stmt->fetch(\PDO::FETCH_OBJ);
		}

		if ($user == null) {
			// create fe_user

			$name = $jwt->name;
			if (strpos($name, "@") !== false) {
				$name = $jwt->nickname;
			}

			$user = new \stdClass;
			$user->username = $jwt->nickname;
			if ($user->username == NULL) {
				$user->username = uniqid();
			}
			$user->email = $jwt->email;
			$user->name = $name;

			$sql = "INSERT INTO fe_users (pid, tstamp, crdate, email, username, usergroup, name) VALUES(4, :tstamp, :tstamp, :email, :username, 1, :name)";
       			$stmt = $db->prepare($sql);

			$now = time();
       			$stmt->bindParam("tstamp", $now);
       			$stmt->bindParam("email", $jwt->email);
       			$stmt->bindParam("username", $jwt->nickname);
       			$stmt->bindParam("name", $name);
       			$stmt->execute();

			$user->uid = $db->lastInsertId();
		}

		$sql = "INSERT INTO fe_users_auth0 (sub, user_id) VALUES(:sub, :userid)";
       		$stmt = $db->prepare($sql);
       		$stmt->bindParam("sub", $jwt->sub);
       		$stmt->bindParam("userid", $user->uid);
       		$stmt->execute();

      		\Cichlids\Sns::sync_user_login($user->uid, $jwt->sub);

		return $user->uid;
	}

	static function getAuthUser($request, $response, $args, $jwt) {
		$userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);

		setcookie("userid", $userid, time()+60*60*24*6004, "/", "cichlids.com", true, true);

		$name = $jwt->name;
		if (strpos($name, "@") !== false) {
			$name = $jwt->nickname;
		}
		
		// Name nur updaten, wenn bisher null - auth0 lässt uns Name nicht ändern, wir wollen
		// ihn nur 1x initial vom oauth provider
       		$db = \Cichlids\Db::get();
		$sql = "UPDATE fe_users set 
				username=COALESCE(:username, username),
				email=COALESCE(:email, email),
				name=COALESCE(name, :name),
				user_cichlids_auth0_image=COALESCE(:picture, user_cichlids_auth0_image),
				lastlogin=:tstamp
				WHERE uid=:uid";


       		$stmt = $db->prepare($sql);
		$now = time();
       		$stmt->bindParam("tstamp", $now);
       		$stmt->bindParam("uid", $userid);
                $stmt->bindParam("username", $jwt->nickname);
                $stmt->bindParam("email", $jwt->email);
                $stmt->bindParam("name", $name);
                $stmt->bindParam("picture", $jwt->picture);
       		$stmt->execute();
		$db = null;

		$user = \Cichlids\Users::loadUser($userid);

                return \Cichlids\Util::addCors($response)
                        ->write(json_encode($user));
        }
}
