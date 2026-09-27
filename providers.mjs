// Only quota metadata crosses the provider boundary. Never serialize credentials.
export function percent(value) {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0
    ? Math.min(100, value) : null;
}

export function quotaWindow(id, label, used, reset, durationMins = null) {
  const usedPercent = percent(used);
  if (usedPercent === null) return null;
  const parsed = typeof reset === 'number' ? reset * 1000 : Date.parse(reset);
  return { id, label, usedPercent, resetsAt: Number.isFinite(parsed) ? parsed : null, durationMins };
}

export function normalizeCodex(result) {
  // A missing default bucket must not silently select a different model's allowance.
  const bucket = result.rateLimitsByLimitId?.codex ?? result.rateLimits;
  if (!bucket || (bucket.limitId && bucket.limitId !== 'codex')) return [];
  return [['primary', bucket.primary], ['secondary', bucket.secondary]].flatMap(([id, w]) => {
    if (!w) return [];
    const m = w.windowDurationMins;
    const label = m === 10080 ? 'Haftalık' : m === 300 ? '5 saatlik' :
      typeof m === 'number' ? (m % 60 === 0 ? `${m / 60} saatlik` : `${m} dakikalık`) : 'Kota';
    const normalized = quotaWindow(id, label, w.usedPercent, w.resetsAt, m);
    return normalized ? [normalized] : [];
  });
}

export function normalizeClaude(result) {
  return [['five_hour', 'Oturum · 5 saat', 300], ['seven_day', 'Haftalık · tüm modeller', 10080],
    ['seven_day_sonnet', 'Haftalık · Sonnet', 10080], ['seven_day_opus', 'Haftalık · Opus', 10080]]
    .flatMap(([id, label, minutes]) => {
      const w = result[id];
      const normalized = w && quotaWindow(id, label, w.utilization, w.resets_at, minutes);
      return normalized ? [normalized] : [];
    });
}

export function providerResult(name, windows, now = Date.now()) {
  if (!windows.length) throw Object.assign(new Error('no_quota'), { kind: 'no_quota' });
  return { name, status: 'ok', windows, updatedAt: now, attemptedAt: now, error: null, nextAttemptAt: now + 300000 };
}

export function providerFailure(name, kind, previous, now = Date.now(), retryMs = 300000) {
  const clear = ['login_required', 'forbidden', 'no_quota'].includes(kind);
  return { name, status: clear ? kind : 'stale', windows: clear ? [] : previous?.windows ?? [],
    updatedAt: clear ? null : previous?.updatedAt ?? null, attemptedAt: now, error: kind,
    nextAttemptAt: now + Math.max(60000, retryMs) };
}

// Kullanıcı arayüzden kapattığı sağlayıcıyı sorgulamaz; eksik ya da bozuk tercih "açık" sayılır.
export function enabledProviders(prefs) {
  const flags = prefs && typeof prefs.providers === 'object' && prefs.providers !== null ? prefs.providers : {};
  return ['codex', 'claude'].filter(id => flags[id] !== false);
}
