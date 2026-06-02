<?php 
namespace Cichlids;

class Categories {

	static function loadCategory($uid) {
		if ($uid == null) return null;
                $sql = "SELECT * FROM user_cichlids_category cat WHERE cat.hidden=0 AND cat.deleted=0 AND uid=:uid";
                $db = \Cichlids\Db::get();
                $stmt = $db->prepare($sql);
                $stmt->bindParam("uid", $uid);
                $stmt->execute();
                $res = $stmt->fetch(\PDO::FETCH_OBJ);
                $db = null;
                return $res;
        }

	static function fromIdString($s) {
		switch (strtoupper($s)) {
			case "TANGANYIKA": return 1;
			case "MALAWI": return 2;
			case "AMERICAN": return 3;
			case "AFRICAN": return 6;
			case "COMMUNITY": return 7;
			case "CENTRAL_AMERICAN": return 8;
			case "SOUTH_AMERICAN": return 9;
			default: return null;
		}
	}

	static function toIdString($uid) {
		switch ($uid) {
			case 1: return "TANGANYIKA";
			case 2: return "MALAWI";
			case 3: return "AMERICAN";
			case 6: return "AFRICAN";
			case 7: return "COMMUNITY";
			case 8: return "CENTRAL_AMERICAN";
			case 9: return "SOUTH_AMERICAN";
			default: return "UNKNOWN";
		}
	}

	static function convert($category) {
		$res = Array();
		$res['id'] = Categories::toIdString($category == null ? 0 : $category->uid);
		$res['title'] = $category == null ? "Unknown" : $category->title;
		return $res;
	}

}
