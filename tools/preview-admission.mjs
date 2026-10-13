// Same-repository PRs request a preview with the preview label. Admission expires
// 48 hours after PR creation and selects the two oldest eligible PR numbers.
export function admittedPreviews(prs, now = Date.now()) {
  return prs.filter(pr => pr.state === 'open' && !pr.draft &&
    pr.head?.repo?.full_name === 'lngr/cichlids.com' &&
    pr.labels.some(label => label.name === 'preview') &&
    Number.isFinite(Date.parse(pr.created_at)) &&
    now - Date.parse(pr.created_at) < 48 * 60 * 60 * 1000)
    .sort((a, b) => a.number - b.number).slice(0, 2).map(pr => pr.number);
}

// Label reconciliation removes excess admissions before granting a free slot.
export async function reconcilePreviews(github, { owner, repo }, now = Date.now()) {
  const prs = await github.paginate(github.rest.pulls.list, { owner, repo, state: 'open', per_page: 100 });
  const admitted = new Set(admittedPreviews(prs, now));
  const active = pr => pr.labels.some(label => label.name === 'preview-active');
  for (const pr of prs.filter(pr => active(pr) && !admitted.has(pr.number))) {
    await github.rest.issues.removeLabel({ owner, repo, issue_number: pr.number, name: 'preview-active' });
  }
  for (const pr of prs.filter(pr => !active(pr) && admitted.has(pr.number))) {
    await github.rest.issues.addLabels({ owner, repo, issue_number: pr.number, labels: ['preview-active'] });
  }
  return [...admitted];
}
