#!/usr/bin/env node
// Dependency-free prompt tinkering harness for Pawn Diary.
// Fires editable fixture prompts at an OpenAI-compatible endpoint and prints the result.
//
//   node run.js                       list fixtures
//   node run.js prompts/insult.txt --show    run one, echo full prompt
//   node run.js --all                 run every fixture
//   node run.js --md                  build a markdown book from results/ for phone reading
//
// Endpoint/model come from config.json (or ENDPOINT / MODEL / API_KEY env vars).

const fs = require('fs');
const path = require('path');
const http = require('http');
const https = require('https');

const ROOT = __dirname;
const PROMPTS_DIR = path.join(ROOT, 'prompts');
const RESULTS_DIR = path.join(ROOT, 'results');
const CONFIG = loadConfig();
const PERSONAS = loadPersonas();

function loadConfig() {
  const def = { endpoint: 'http://localhost:1234/v1', model: 'rocinante-x-12b-v1-i1', apiKey: '', temperature: 0.8, maxTokens: 320 };
  const p = path.join(ROOT, 'config.json');
  let cfg = def;
  if (fs.existsSync(p)) {
    try { cfg = Object.assign({}, def, JSON.parse(fs.readFileSync(p, 'utf8'))); }
    catch (e) { console.error('! bad config.json:', e.message); }
  }
  if (process.env.ENDPOINT) cfg.endpoint = process.env.ENDPOINT;
  if (process.env.MODEL) cfg.model = process.env.MODEL;
  if (process.env.API_KEY) cfg.apiKey = process.env.API_KEY;
  return cfg;
}

// Parse personas.txt into an array of persona description strings.
// Matches numbered entries like "1. stoic-survivor\n   writes in terse, matter-of-fact..."
function loadPersonas() {
  const p = path.join(ROOT, 'personas.txt');
  if (!fs.existsSync(p)) return [];
  const raw = fs.readFileSync(p, 'utf8');
  const personas = [];
  const re = /^\d+\.\s+\S+\n\s+(.+)$/gm;
  let m;
  while ((m = re.exec(raw)) !== null) {
    personas.push(m[1].trim());
  }
  return personas;
}

// Replace each "persona: random" line (initiator/recipient/you persona) with a randomly
// picked persona from the catalog. Every occurrence gets a fresh random pick.
function resolvePersonas(userPrompt) {
  if (!PERSONAS.length) return userPrompt;
  return userPrompt.replace(/^(.* persona:\s*)random\s*$/gim, (_, prefix) =>
    prefix + PERSONAS[Math.floor(Math.random() * PERSONAS.length)]
  );
}

// Parse a fixture: leading "# key: value" header lines, then ===SYSTEM=== / ===USER=== sections.
// If no ===SYSTEM=== block, fall back to prompts/_system.txt.
function parseFixture(file) {
  const raw = fs.readFileSync(file, 'utf8');
  const meta = { mode: 'single', temperature: CONFIG.temperature, maxTokens: CONFIG.maxTokens };
  const lines = raw.split(/\r?\n/);

  let i = 0;
  for (; i < lines.length; i++) {
    if (lines[i].startsWith('===')) break;
    const m = lines[i].match(/^#\s*([\w_]+)\s*:\s*(.+?)\s*$/);
    if (!m) continue;
    const k = m[1].toLowerCase();
    if (k === 'mode') meta.mode = m[2].toLowerCase();
    else if (k === 'temperature') meta.temperature = parseFloat(m[2]);
    else if (k === 'max_tokens' || k === 'maxtokens') meta.maxTokens = parseInt(m[2], 10);
  }

  let system = null, user = null, current = null, buf = [];
  const flush = () => {
    if (current === 'SYSTEM') system = buf.join('\n').trim();
    else if (current === 'USER') user = buf.join('\n').trim();
    buf = [];
  };
  for (; i < lines.length; i++) {
    const sm = lines[i].match(/^===\s*(\w+)\s*===\s*$/);
    if (sm) { flush(); current = sm[1].toUpperCase(); continue; }
    if (current) buf.push(lines[i]);
  }
  flush();

  if (system === null) {
    const sp = path.join(PROMPTS_DIR, '_system.txt');
    if (fs.existsSync(sp)) system = fs.readFileSync(sp, 'utf8').trim();
  }
  return { meta, system, user };
}

function callModel(system, user, meta) {
  const url = new URL(CONFIG.endpoint.replace(/\/+$/, '') + '/chat/completions');
  const messages = [];
  if (system) messages.push({ role: 'system', content: system });
  messages.push({ role: 'user', content: user });
  const body = JSON.stringify({ model: CONFIG.model, messages, temperature: meta.temperature, max_tokens: meta.maxTokens });
  const lib = url.protocol === 'https:' ? https : http;
  const opts = { method: 'POST', headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) } };
  if (CONFIG.apiKey) opts.headers['Authorization'] = 'Bearer ' + CONFIG.apiKey;

  return new Promise((resolve, reject) => {
    const req = lib.request(url, opts, (res) => {
      let data = '';
      res.on('data', (c) => (data += c));
      res.on('end', () => {
        if (res.statusCode >= 400) return reject(new Error('HTTP ' + res.statusCode + ': ' + data.slice(0, 400)));
        try {
          const j = JSON.parse(data);
          const choice = (j.choices && j.choices[0]) || {};
          resolve((choice.message && choice.message.content) || choice.text || '');
        } catch (e) { reject(new Error('bad json response: ' + data.slice(0, 400))); }
      });
    });
    req.on('error', reject);
    req.write(body);
    req.end();
  });
}

// Tolerant [INITIATOR]/[RECIPIENT] split, mirroring DiaryEvent.ParseDualResponse.
function splitDual(text) {
  const clean = (s) => (s || '').replace(/\[INITIATOR\]/gi, '').replace(/\[RECIPIENT\]/gi, '').trim();
  const iIdx = text.search(/\[INITIATOR\]/i);
  const rIdx = text.search(/\[RECIPIENT\]/i);
  let initiator = '', recipient = '';
  if (iIdx >= 0 && rIdx >= 0) {
    if (iIdx < rIdx) { initiator = text.slice(iIdx, rIdx); recipient = text.slice(rIdx); }
    else { recipient = text.slice(rIdx, iIdx); initiator = text.slice(iIdx); }
  } else if (rIdx >= 0) { initiator = text.slice(0, rIdx); recipient = text.slice(rIdx); }
  else if (iIdx >= 0) { initiator = text.slice(iIdx); recipient = text; }
  else { initiator = text; recipient = text; }
  initiator = clean(initiator) || clean(text);
  recipient = clean(recipient) || clean(text);
  return { initiator, recipient };
}

async function runOne(file, showPrompt) {
  const { meta, system, user } = parseFixture(file);
  const resolvedUser = resolvePersonas(user);
  const fixtureName = path.basename(file, '.txt');
  console.log('\n========== ' + fixtureName + '  [mode=' + meta.mode + ' temp=' + meta.temperature + ' max=' + meta.maxTokens + '] ==========');
  if (showPrompt) {
    console.log('--- system ---\n' + (system || '(none)'));
    console.log('--- user ---\n' + resolvedUser);
  }
  const t0 = Date.now();
  let out;
  try { out = await callModel(system, resolvedUser, meta); }
  catch (e) { console.error('  ERROR: ' + e.message); return; }
  const dt = ((Date.now() - t0) / 1000).toFixed(1);
  console.log('--- response (' + dt + 's) ---\n' + out);

  let parsed = null;
  if (meta.mode === 'dual') {
    parsed = splitDual(out);
    console.log('\n--- parsed ---');
    console.log('[INITIATOR] ' + parsed.initiator);
    console.log('[RECIPIENT] ' + parsed.recipient);
  }

  saveResult(fixtureName, meta, system, resolvedUser, out, parsed, dt);
}

function saveResult(name, meta, system, user, raw, parsed, dt) {
  const model = CONFIG.model.replace(/[\\/:*?"<>|]/g, '-');
  const catalogDir = path.join(RESULTS_DIR, name);
  if (!fs.existsSync(catalogDir)) fs.mkdirSync(catalogDir, { recursive: true });
  const ts = new Date().toISOString().replace(/[:.]/g, '-');
  const outPath = path.join(catalogDir, model + '-' + ts + '.md');
  let content = '# ' + name + '\n\n';
  content += 'mode: ' + meta.mode + '  temp: ' + meta.temperature + '  max_tokens: ' + meta.maxTokens + '  time: ' + dt + 's\n\n';
  content += '## prompt\n\n```\n' + user + '\n```\n\n';
  content += '## response\n\n';
  if (parsed) {
    content += '**Initiator:** ' + parsed.initiator + '\n\n';
    content += '**Recipient:** ' + parsed.recipient + '\n';
  } else {
    content += raw + '\n';
  }
  fs.writeFileSync(outPath, content, 'utf8');
  console.log('\nsaved: ' + path.relative(ROOT, outPath));
}

function listFixtures() {
  if (!fs.existsSync(PROMPTS_DIR)) return [];
  return fs.readdirSync(PROMPTS_DIR)
    .filter((f) => f.endsWith('.txt') && !f.startsWith('_'))
    .map((f) => path.join(PROMPTS_DIR, f));
}

// ——————————————————————————— Markdown book builder ————————————————————————————

function buildMd() {
  if (!fs.existsSync(RESULTS_DIR)) {
    console.log('No results/ directory yet. Run some fixtures first.');
    return;
  }

  const dirs = fs.readdirSync(RESULTS_DIR).filter(d => {
    const st = fs.statSync(path.join(RESULTS_DIR, d));
    return st.isDirectory();
  }).sort();

  if (!dirs.length) {
    console.log('No fixture result folders found in results/.');
    return;
  }

  const date = new Date().toISOString().slice(0, 10);
  const model = CONFIG.model;
  let md = '# Pawn Diary — ' + date + '\n';
  md += 'model: ' + model + '\n\n';

  for (const dir of dirs) {
    const dirPath = path.join(RESULTS_DIR, dir);
    const files = fs.readdirSync(dirPath).filter(f =>
      (f.endsWith('.md') || f.endsWith('.txt')) && f !== 'catalog.txt'
    ).sort().reverse();
    if (!files.length) continue;

    const latest = files[0];
    const raw = fs.readFileSync(path.join(dirPath, latest), 'utf8');

    let initiator = null, recipient = null;

    // Try markdown format first
    const mdSec = raw.split(/\n## /);
    for (const s of mdSec) {
      if (s.startsWith('response')) {
        const body = s.slice('response'.length).trim();
        const im = body.match(/\*\*Initiator:\*\*\s*([\s\S]*?)(?:\*\*Recipient:\*\*|$)/);
        const rm = body.match(/\*\*Recipient:\*\*\s*([\s\S]*?)$/);
        initiator = im ? im[1].trim() : body.trim();
        recipient = rm ? rm[1].trim() : '';
      }
    }

    // Fall back to old .txt format
    if (!initiator && !recipient) {
      const txtSec = raw.split(/\n--- /);
      for (const s of txtSec) {
        if (s.startsWith('parsed')) {
          const val = s.slice('parsed'.length).trim();
          const im = val.match(/\[INITIATOR\]\s*([\s\S]*?)(?:\[RECIPIENT\]|$)/i);
          const rm = val.match(/\[RECIPIENT\]\s*([\s\S]*?)$/i);
          initiator = im ? im[1].trim() : val.trim();
          recipient = rm ? rm[1].trim() : '';
        } else if (s.startsWith('raw response')) {
          initiator = s.slice('raw response'.length).trim();
        }
      }
    }

    md += '\n\n## ' + dir.replace(/-/g, ' ') + '\n\n';
    if (initiator) {
      md += '**Initiator:** ' + initiator + '\n\n';
      if (recipient) {
        md += '**Recipient:** ' + recipient + '\n';
      }
    }
  }

  const outPath = path.join(ROOT, 'pawn-diary-' + date + '.md');
  fs.writeFileSync(outPath, md, 'utf8');
  console.log('Markdown written: ' + path.relative(ROOT, outPath) + '  (' + dirs.length + ' chapters)');
}

(async () => {
  const args = process.argv.slice(2);

  if (args.includes('--md')) {
    buildMd();
    return;
  }

  const showPrompt = args.includes('--show');
  const positional = args.filter((a) => !a.startsWith('--'));
  let files;

  if (args.includes('--all')) {
    files = listFixtures();
  } else if (positional.length) {
    files = positional.map((a) => (fs.existsSync(a) ? a : path.join(PROMPTS_DIR, a)));
  } else {
    console.log('endpoint: ' + CONFIG.endpoint + '   model: ' + CONFIG.model);
    console.log('\nfixtures (prompts/):');
    listFixtures().forEach((f) => console.log('  ' + path.basename(f)));
    console.log('\nusage: node run.js <fixture.txt> [--show]   |   node run.js --all');
    return;
  }

  for (const f of files) await runOne(f, showPrompt);
})();
