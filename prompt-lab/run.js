#!/usr/bin/env node
// Dependency-free prompt tinkering harness for Pawn Diary.
// Fires editable fixture prompts at an OpenAI-compatible endpoint and prints the result.
//
//   node run.js                       list fixtures
//   node run.js prompts/insult.txt --show    run one, echo full prompt
//   node run.js --all                 run every fixture
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

// Parse a fixture: leading "# key: value" header lines, then named sections such as
// ===SYSTEM===, ===USER===, ===INITIATOR===, and ===RECIPIENT===.
// If no ===SYSTEM=== block, fall back to prompts/_system.txt.
function parseFixture(file) {
  const raw = fs.readFileSync(file, 'utf8');
  const meta = { mode: null, temperature: CONFIG.temperature, maxTokens: CONFIG.maxTokens };
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

  let system = null, current = null, buf = [];
  const sections = {};
  const flush = () => {
    if (current === 'SYSTEM') system = buf.join('\n').trim();
    else if (current) sections[current.toLowerCase()] = buf.join('\n').trim();
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
  if (!meta.mode) {
    meta.mode = sections.initiator && sections.recipient ? 'paired' : 'single';
  }
  return {
    meta,
    system,
    user: sections.user || null,
    initiator: sections.initiator || null,
    recipient: sections.recipient || null
  };
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

async function runOne(file, showPrompt) {
  const fixture = parseFixture(file);
  const { meta, system } = fixture;
  const fixtureName = path.basename(file, '.txt');
  console.log('\n========== ' + fixtureName + '  [mode=' + meta.mode + ' temp=' + meta.temperature + ' max=' + meta.maxTokens + '] ==========');

  if (fixture.initiator && fixture.recipient) {
    await runPaired(fixtureName, meta, system, fixture.initiator, fixture.recipient, showPrompt);
    return;
  }

  const userPrompt = resolvePersonas(fixture.user || '');
  if (showPrompt) {
    console.log('--- system ---\n' + (system || '(none)'));
    console.log('--- user ---\n' + userPrompt);
  }

  const t0 = Date.now();
  let response;
  try { response = await callModel(system, userPrompt, meta); }
  catch (e) { console.error('  ERROR: ' + e.message); return; }
  const elapsed = secondsSince(t0);
  console.log('--- response (' + elapsed + 's) ---\n' + response);

  saveResult(fixtureName, meta, system, [{
    role: 'entry',
    label: 'Diary Entry',
    prompt: userPrompt,
    response,
    elapsedSeconds: Number(elapsed)
  }], elapsed);
}

async function runPaired(name, meta, system, initiatorTemplate, recipientTemplate, showPrompt) {
  const startedAt = Date.now();
  const initiatorPrompt = resolvePersonas(initiatorTemplate);
  if (showPrompt) {
    console.log('--- system ---\n' + (system || '(none)'));
    console.log('--- initiator prompt ---\n' + initiatorPrompt);
  }

  const initiatorStartedAt = Date.now();
  let initiatorResponse;
  try { initiatorResponse = await callModel(system, initiatorPrompt, meta); }
  catch (e) { console.error('  INITIATOR ERROR: ' + e.message); return; }
  const initiatorElapsed = secondsSince(initiatorStartedAt);
  console.log('--- initiator response (' + initiatorElapsed + 's) ---\n' + initiatorResponse);

  const recipientPrompt = resolvePersonas(fillInitiatorResult(recipientTemplate, initiatorResponse));
  if (showPrompt) {
    console.log('--- recipient prompt ---\n' + recipientPrompt);
  }

  const recipientStartedAt = Date.now();
  let recipientResponse;
  try { recipientResponse = await callModel(system, recipientPrompt, meta); }
  catch (e) { console.error('  RECIPIENT ERROR: ' + e.message); return; }
  const recipientElapsed = secondsSince(recipientStartedAt);
  console.log('--- recipient response (' + recipientElapsed + 's) ---\n' + recipientResponse);

  saveResult(name, meta, system, [
    {
      role: 'initiator',
      label: 'Initiator POV',
      prompt: initiatorPrompt,
      response: initiatorResponse,
      elapsedSeconds: Number(initiatorElapsed)
    },
    {
      role: 'recipient',
      label: 'Recipient POV',
      prompt: recipientPrompt,
      response: recipientResponse,
      elapsedSeconds: Number(recipientElapsed)
    }
  ], secondsSince(startedAt));
}

function fillInitiatorResult(prompt, initiatorResponse) {
  return String(prompt || '').replace(/\{\{initiator_result\}\}/g, compactLine(initiatorResponse));
}

function compactLine(value) {
  return String(value || '').replace(/\r?\n/g, ' ').replace(/\s+/g, ' ').trim();
}

function secondsSince(tick) {
  return ((Date.now() - tick) / 1000).toFixed(1);
}

function saveResult(name, meta, system, entries, totalElapsed) {
  const model = CONFIG.model.replace(/[\\/:*?"<>|]/g, '-');
  const catalogDir = path.join(RESULTS_DIR, name);
  if (!fs.existsSync(catalogDir)) fs.mkdirSync(catalogDir, { recursive: true });
  const generatedAt = new Date();
  const ts = generatedAt.toISOString().replace(/[:.]/g, '-');
  const outPath = path.join(catalogDir, model + '-' + ts + '.md');
  const rawData = {
    fixture: name,
    generatedAt: generatedAt.toISOString(),
    endpoint: CONFIG.endpoint,
    model: CONFIG.model,
    mode: meta.mode,
    temperature: meta.temperature,
    maxTokens: meta.maxTokens,
    elapsedSeconds: Number(totalElapsed),
    systemPrompt: system || '',
    entries: entries.map((entry) => ({
      role: entry.role,
      prompt: entry.prompt || '',
      rawResponse: entry.response || '',
      elapsedSeconds: entry.elapsedSeconds
    }))
  };

  let content = '# ' + titleFromName(name) + '\n\n';
  content += '| Field | Value |\n';
  content += '| --- | --- |\n';
  content += '| Fixture | `' + escapeTable(name) + '` |\n';
  content += '| Model | `' + escapeTable(CONFIG.model) + '` |\n';
  content += '| Endpoint | `' + escapeTable(CONFIG.endpoint) + '` |\n';
  content += '| Mode | `' + escapeTable(meta.mode) + '` |\n';
  content += '| Temperature | `' + escapeTable(meta.temperature) + '` |\n';
  content += '| Max tokens | `' + escapeTable(meta.maxTokens) + '` |\n';
  content += '| Time | `' + escapeTable(totalElapsed + 's') + '` |\n';
  content += '| Generated | `' + escapeTable(generatedAt.toISOString()) + '` |\n\n';

  content += '## Result\n\n';
  for (const entry of entries) {
    content += '### ' + entry.label + '\n\n';
    content += quoteBlock(entry.response) + '\n\n';
  }

  content += '## Prompt\n\n';
  content += '### System\n\n' + codeBlock(system || '(none)', 'text') + '\n\n';
  for (const entry of entries) {
    content += '### ' + entry.label + '\n\n' + codeBlock(entry.prompt || '', 'text') + '\n\n';
  }
  content += '## Raw Response\n\n';
  for (const entry of entries) {
    content += '### ' + entry.label + '\n\n' + codeBlock(entry.response || '', 'text') + '\n\n';
  }
  content += '## Raw Data\n\n' + codeBlock(JSON.stringify(rawData, null, 2), 'json') + '\n';

  fs.writeFileSync(outPath, content, 'utf8');
  console.log('\nsaved: ' + path.relative(ROOT, outPath));
}

function titleFromName(name) {
  return name
    .replace(/[-_]+/g, ' ')
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

function escapeTable(value) {
  return String(value == null ? '' : value)
    .replace(/\|/g, '\\|')
    .replace(/\r?\n/g, ' ');
}

function quoteBlock(value) {
  const text = String(value || '').trim();
  if (!text) return '> (empty)';
  return text.split(/\r?\n/).map((line) => line.trim() ? '> ' + line : '>').join('\n');
}

function codeBlock(value, lang) {
  const text = String(value == null ? '' : value).replace(/\s+$/g, '');
  const matches = text.match(/`{3,}/g) || [];
  const fenceLength = matches.reduce((max, s) => Math.max(max, s.length + 1), 3);
  const fence = '`'.repeat(fenceLength);
  return fence + (lang || '') + '\n' + text + '\n' + fence;
}

function listFixtures() {
  if (!fs.existsSync(PROMPTS_DIR)) return [];
  return fs.readdirSync(PROMPTS_DIR)
    .filter((f) => f.endsWith('.txt') && !f.startsWith('_'))
    .map((f) => path.join(PROMPTS_DIR, f));
}

(async () => {
  const args = process.argv.slice(2);

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
    console.log('results: saved directly as formatted Markdown under results/<fixture>/');
    return;
  }

  for (const f of files) await runOne(f, showPrompt);
})();
