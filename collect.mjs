import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { spawn } from 'node:child_process';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';
import { normalizeCodex, normalizeClaude, providerResult, providerFailure } from './providers.mjs';

const base = path.dirname(fileURLToPath(import.meta.url));
const stateDir = process.env.USAGE_WIDGET_STATE_DIR || path.join(process.env.LOCALAPPDATA || os.homedir(), 'UsageWidget');
fs.mkdirSync(stateDir, { recursive: true });
const statePath = path.join(stateDir, 'usage.json');
const configPath = path.join(base, 'runtime.json');
const config = readJson(configPath, {});
const previous = readJson(statePath, {});
const force = process.argv.includes('--force');
function readJson(p, fallback) { try { return JSON.parse(fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '')); } catch { return fallback; } }
function failure(kind, retryMs) { return Object.assign(new Error(kind), { kind, retryMs }); }

async function codexUsage() {
  return new Promise((resolve, reject) => {
    const child = spawn(config.codexPath || 'codex', ['app-server'], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    let settled = false;
    const timer = setTimeout(() => finish(failure('timeout')), 20000);
    const lines = readline.createInterface({ input: child.stdout });
    function finish(error, value) {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      lines.close();
      child.stdin.destroy();
      child.kill();
      error ? reject(error) : resolve(value);
    }
    const send = (message) => { if (!settled && !child.stdin.destroyed) child.stdin.write(JSON.stringify(message) + '\n'); };
    child.stderr.resume(); // Never write authentication or server diagnostics to disk.
    child.stdin.on('error', () => finish(failure('connection')));
    child.on('error', () => finish(failure('not_installed')));
    child.on('exit', () => { if (!settled) finish(failure('connection')); });
    lines.on('line', line => {
      let message;
      try { message = JSON.parse(line); } catch { return; }
      if (message.id !== 1 && message.id !== 2) return;
      if (message.error) {
        const text = String(message.error.message || '');
        return finish(failure(/auth|login|sign.in|chatgpt.*required/i.test(text) ? 'login_required' : 'connection'));
      }
      if (message.id === 1) {
        send({ method: 'initialized', params: {} });
        send({ id: 2, method: 'account/rateLimits/read', params: {} });
      } else {
        try { finish(null, providerResult('Codex', normalizeCodex(message.result || {}))); }
        catch (e) { finish(e); }
      }
    });
    send({ id: 1, method: 'initialize', params: { clientInfo: { name: 'usage_widget', title: 'Usage Widget', version: '0.1.0' } } });
  });
}

async function claudeUsage() {
  // Read the owning CLI's session afresh. Do not copy it, refresh it, or change it.
  const credentialsPath = path.join(process.env.CLAUDE_CONFIG_DIR || path.join(os.homedir(), '.claude'), '.credentials.json');
  const oauth = readJson(credentialsPath, {}).claudeAiOauth;
  if (!oauth?.accessToken) throw failure('login_required');
  const response = await fetch('https://api.anthropic.com/api/oauth/usage', {
    method: 'GET', redirect: 'error', signal: AbortSignal.timeout(15000),
    headers: { Authorization: `Bearer ${oauth.accessToken}`, 'anthropic-beta': 'oauth-2025-04-20' }
  });
  if (response.status === 401) throw failure('login_required');
  if (response.status === 403) throw failure('forbidden');
  if (response.status === 429) {
    const retry = response.headers.get('retry-after');
    const delay = Number.isFinite(Number(retry)) && retry !== null ? Number(retry) * 1000 : Date.parse(retry) - Date.now();
    throw failure('rate_limited', Number.isFinite(delay) ? Math.max(delay, 300000) : 900000);
  }
  if (!response.ok) throw failure('connection');
  return providerResult('Claude', normalizeClaude(await response.json()));
}

// The lock prevents a second window or manual refresh from issuing duplicate requests.
let lock;
const lockPath = path.join(stateDir, 'collect.lock');
try {
  try { if (Date.now() - fs.statSync(lockPath).mtimeMs > 60000) fs.unlinkSync(lockPath); } catch {}
  lock = fs.openSync(lockPath, 'wx');
} catch { process.exit(0); }
try {
  const result = { schema: 1, writtenAt: Date.now() };
  const tasks = [['codex', 'Codex', codexUsage], ['claude', 'Claude', claudeUsage]];
  const settled = await Promise.allSettled(tasks.map(async ([id, name, fetcher]) => {
    const old = previous[id];
    const retryBlocked = old?.error === 'rate_limited' && Date.now() < old.nextAttemptAt;
    const cooldown = old?.attemptedAt && Date.now() - old.attemptedAt < 60000;
    if (old && (retryBlocked || cooldown || (!force && Date.now() < old.nextAttemptAt))) return old;
    try { return await fetcher(); }
    catch (error) { return providerFailure(name, error.kind || 'connection', old, Date.now(), error.retryMs); }
  }));
  settled.forEach((entry, index) => {
    const [id, name] = tasks[index];
    result[id] = entry.status === 'fulfilled' ? entry.value : providerFailure(name, 'connection', previous[id]);
  });
  const temporary = `${statePath}.${process.pid}.tmp`;
  fs.writeFileSync(temporary, JSON.stringify(result, null, 2), { encoding: 'utf8', mode: 0o600 });
  fs.renameSync(temporary, statePath);
  if (process.argv.includes('--summary')) console.log(JSON.stringify({ codex: result.codex.status, claude: result.claude.status }));
} finally {
  if (lock !== undefined) fs.closeSync(lock);
  try { fs.unlinkSync(lockPath); } catch {}
}
