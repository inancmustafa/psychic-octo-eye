import test from 'node:test';
import assert from 'node:assert/strict';
import { percent, normalizeCodex, normalizeClaude, providerFailure, providerResult } from './providers.mjs';

test('missing quotas are not fabricated as zero', () => {
  for (const value of [null, undefined, '', '0', NaN, -1, Infinity]) assert.equal(percent(value), null);
  assert.equal(percent(0), 0);
  assert.equal(percent(100), 100);
});
test('server quota durations and seconds are respected', () => {
  const result = normalizeCodex({ rateLimitsByLimitId: { codex: { primary: {usedPercent: 14, windowDurationMins: 300, resetsAt: 1790511208}, secondary: {usedPercent: 2, windowDurationMins: 10080} } } });
  assert.equal(result[0].resetsAt, 1790511208000);
  assert.equal(result[0].label, '5 saatlik');
  assert.equal(result[1].label, 'Haftalık');
  assert.equal(result[1].resetsAt, null);
});
test('do not conflate a model bucket with the main quota', () => {
  assert.deepEqual(normalizeCodex({rateLimitsByLimitId:{special:{primary:{usedPercent:50}}}}), []);
  assert.deepEqual(normalizeCodex({rateLimits:{limitId:'special',primary:{usedPercent:50}}}), []);
});
test('Claude optional windows and real zero remain distinct', () => {
  const result = normalizeClaude({five_hour:{utilization:0,resets_at:'2026-09-27T12:00:00Z'},seven_day:null,seven_day_opus:{utilization:45,resets_at:null}});
  assert.equal(result.length, 2);
  assert.equal(result[0].usedPercent, 0);
  assert.equal(result[1].resetsAt, null);
});
test('network errors retain a timestamped stale reading; auth errors clear it', () => {
  const previous=providerResult('Codex',[{usedPercent:12}],1000);
  const stale=providerFailure('Codex','connection',previous,5000);
  assert.equal(stale.updatedAt,1000);
  assert.equal(stale.status,'stale');
  assert.equal(stale.windows[0].usedPercent,12);
  const loggedOut=providerFailure('Codex','login_required',previous,5000);
  assert.deepEqual(loggedOut.windows,[]);
  assert.equal(loggedOut.updatedAt,null);
});
test('rate-limit backoff preserves provider retry time', () => {
  assert.equal(providerFailure('Claude','rate_limited',null,1000,900000).nextAttemptAt,901000);
});
