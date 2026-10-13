import test from 'node:test';
import assert from 'node:assert/strict';
import { admittedPreviews } from './preview-admission.mjs';

const now = Date.parse('2026-10-13T12:00:00Z');
const pr = (number, overrides = {}) => ({ number, state: 'open', draft: false,
  created_at: '2026-10-13T10:00:00Z', labels: [{ name: 'preview' }],
  head: { repo: { full_name: 'lngr/cichlids.com' } }, ...overrides });

test('TASK-2.7: at most two requested open same-repository previews qualify', () => {
  assert.deepEqual(admittedPreviews([pr(4), pr(3), pr(2), pr(1)], now), [1, 2]);
});
test('closed, expired, draft, fork and unrequested PRs release their slots', () => {
  assert.deepEqual(admittedPreviews([pr(1, { state: 'closed' }),
    pr(2, { created_at: '2026-10-11T12:00:00Z' }), pr(3, { draft: true }),
    pr(4, { head: { repo: { full_name: 'someone/fork' } } }),
    pr(5, { labels: [{ name: 'preview-active' }] }), pr(6)], now), [6]);
});
test('expiry boundary is 48 hours and time input must be valid', () => {
  assert.deepEqual(admittedPreviews([pr(1, { created_at: '2026-10-11T12:00:01Z' })], now), [1]);
  assert.deepEqual(admittedPreviews([pr(1, { created_at: 'invalid' })], now), []);
});
