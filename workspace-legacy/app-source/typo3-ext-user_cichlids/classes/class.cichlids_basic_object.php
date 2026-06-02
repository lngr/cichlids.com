<?

class cichlids_basic_object {
    function cichlids_basic_object($row) {
        foreach(array_keys($row) as $key)
            $this->$key = $row[$key];
    }
}

?>
