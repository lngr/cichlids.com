<?

function link_to_page($obj, $page, $total, $perpage, $current) {
    $overwrite = array("page" => $page);
    $link = $obj->pi_linkTP_keepPIvars_url($overwrite, 1, 0);
    return '<a href="'.$link.'">';
}


?>
