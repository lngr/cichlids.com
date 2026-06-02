<?

require_once("link_to_page.php");

$first = 1;
$total = $pagebrowser_count;
$last = floor(($total - 1) / $pagebrowser_perpage) + 1;
$current = $pagebrowser_currentpage;
$perpage = $pagebrowser_perpage;

?>
<div class="pagebrowser"
    style="
        clear: both;
        font-size: 8pt;
        margin-bottom: 10px;
        margin-top: 10px;

    "
    >
    <table border="0" cellspacing="0" cellpadding="0" width="100%" class="pagebrowser_table">
        <tr>
            <td nowrap valign="top" align="left">
	    <? if ($first != $current): ?>
		<?=link_to_page($this, $first, $total, $perpage, $current);?><img src="/fileadmin/nav_first.gif" border="0"></a>
		<?=link_to_page($this, $current - 1, $total, $perpage, $current);?><img src="/fileadmin/nav_prev.gif" border="0"></a>
	    <? endif; ?></td>
            <td valign="top" align="center" >
	    <? if ($pagebrowser_dopages): ?>
		Page <?=$current;?> of <?=$last;?>
	    <? endif; ?>
	    </td>
            <td nowrap valign="top" align="right" >
	    <? if ($last != $current): ?>
		<?=link_to_page($this, $current + 1, $total, $perpage, $current);?><img src="/fileadmin/nav_next.gif" border="0"></a>
		<?=link_to_page($this, $last, $total, $perpage, $current);?><img src="/fileadmin/nav_last.gif" border="0"></a>
	    <? endif; ?></td>
	    </td>
        </tr>
	    <? if ($pagebrowser_dopages): ?>
        <tr><td colspan="3" valign="top" align="center" class="pagebrowser_allpages">
	    <?
		for($n = max($current - 5, 1); $n <= min($current + 5, $last); $n++) {
		    if ($n == $current): ?>
			<div class="pagebrowser_page_current"><a name="pagebrowser_page_current_mark"><?=$n;?></a></div>&nbsp;
		    <? else: ?>
			<div class="pagebrowser_page"><?=link_to_page($this, $n, $total, $perpage, $current);?><?=$n;?></a></div>&nbsp;
		    <? endif; ?>
	    <?
		}
	    ?>
	</td></tr>
	    <? endif; ?>
    </table>
</div>

