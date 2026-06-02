<?
    $pics = $this->get_related_pictures_category();
    if (count($pics) == 0) return;
    $max = 10;
    $n = 0;
?>
<? foreach($pics as $pic) {
    if ($pic == $opic) continue;
    if (++$n > $max) break;
    include("show_picture_rel_out.php");
?>
<? } ?>
    <?  // $link = $this->getSpeciesLink($this->species_manager->findSpeciesByPicture($pic)); ?>
    <!-- <a href="<?=$link;?>">Alle <?=count($pics); ?> Bilder</a> -->
