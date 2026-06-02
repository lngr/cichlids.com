<?
    $opic = $this->get_picture(); 
    $user=$this->getFeUserById($opic->fe_user);
    $pics = $this->get_user_pictures($user);
    if (count($pics) == 0) return;
    $max = 20;
    $n = 0;
?>
<? foreach($pics as $pic) {
    if ($pic == $opic) continue;
    if (++$n > $max) break;
    include("show_picture_rel_out.php");
?>
<? } ?>
