<?php

require_once(PATH_t3lib."class.t3lib_div.php");

class entity_manager {
      function findById($classname, $table, $id) {
              $res = mysql_query("SELECT * FROM $table WHERE uid=$id AND deleted=0 LIMIT 1");
              if (mysql_errno()) {
          	print mysql_error();
          	return null;
              }
              if (mysql_num_rows($res) == 0)
          	return null;
              $row = mysql_fetch_assoc($res);
              return $this->instantiateObject($classname, $row);
      }

      function find($classname, $table, $where) {
      }

      function select($classname, $query) {
	    $res = mysql_query($query);
	    if (mysql_errno()) {
		print mysql_error();
		return null;
	    }
	    $all = array();
	    while ($row = mysql_fetch_assoc($res))
		$all[] = $this->instantiateObject($classname, $row);
	    return $all;
      }

      function instantiateObject($classname, $row) {
          $cls = t3lib_div::makeInstanceClassName($classname);
          if (class_exists ($cls)) {
              $classObj = new $cls;
	      if (is_array($classObj->db2fields)) {
		  foreach(array_keys($row) as $key) {
		      if (isset($classObj->db2fields[$key]) && $classObj->db2fields[$key] != "") {
			  $name = $classObj->db2fields[$key];
			  $classObj->$name = $row[$key];
		      }
		  }
	      return $classObj;
	      }
          } else
              return null;
      }

      function insert($table, $obj) {
	  $query = "INSERT INTO $table (";
	  $cols = array();
	  $values = array();
	  foreach(array_keys($obj->fields2db) as $field) {
	      if (!isset($obj->$field) || $obj->$field == "")
		  continue;
	      if ($field == "uid")
		  continue;
	      $type = $obj->fields2db[$field]['type'];
	      $col = $obj->fields2db[$field]['col'];
	      $cols[] = $col;
	      switch($type) {
		  case "int":
		      $values[] = intval($obj->$field);
		      break;
		  default:
		      $values[] = "'".mysql_escape_string($obj->$field)."'";
		      break;
	      }
	  }
	  $query .= join($cols, ",");
	  $query .= ") VALUES (";
	  $query .= join($values, ",");
	  $query .= ")";

	  $res = mysql_query($query);
	  if (mysql_errno()) {
	      print mysql_error();
	      return null;
	  }
	  $obj->uid = mysql_insert_id();
	  return $obj;
      }

      function update($table, $obj) {
	  $query = "UPDATE $table SET ";
	  $lines = array();
	  $uid = intval($obj->uid);
	  if ($uid == 0)
	      return null;

	  foreach(array_keys($obj->fields2db) as $field) {
	      if (!isset($obj->$field))
		  continue;
	      if ($field == "uid")
		  continue;
	      $type = $obj->fields2db[$field]['type'];
	      $col = $obj->fields2db[$field]['col'];
	      switch($type) {
		  case "int":
		      $val = intval($obj->$field);
		      break;
		  default:
		      $val = "'".mysql_escape_string($obj->$field)."'";
		      break;
	      }
	      $lines[] = "$col=$val";
	  }
	  $query .= join($lines, ", ");
	  $query .= " WHERE uid=$uid";
	  $res = mysql_query($query);
	  if (mysql_errno()) {
	      print mysql_error();
	      return null;
	  }
	  return $obj;
      }

      function delete($table, $obj) {
	  $lines = array();
	  $uid = intval($obj->uid);
	  if ($uid == 0)
	      return null;
	  $query = "UPDATE $table SET deleted=1 WHERE uid=$uid";
	  $res = mysql_query($query);
	  if (mysql_errno()) {
	      print mysql_error();
	  }
      }

}
