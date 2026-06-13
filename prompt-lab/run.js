#!/usr/bin/env node
// Dependency-free prompt tinkering harness for Pawn Diary.
// Fires editable fixture prompts at an OpenAI-compatible endpoint and prints the result.
//
//   node run.js                       list fixtures
//   node run.js prompts/insult.txt    run one fixture
//   node run.js --all                 run every fixture
//   node run.js --epub               build an .epub from results/ for phone reading
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
  const meta = { mode: 'dual', temperature: CONFIG.temperature, maxTokens: CONFIG.maxTokens };
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
  const outPath = path.join(catalogDir, model + '-' + ts + '.txt');
  let content = 'fixture: ' + name + '\n';
  content += 'mode: ' + meta.mode + '  temp: ' + meta.temperature + '  max_tokens: ' + meta.maxTokens + '  time: ' + dt + 's\n';
  content += '\n--- system ---\n' + (system || '(none)') + '\n';
  content += '--- user ---\n' + user + '\n';
  content += '\n--- raw response ---\n' + raw + '\n';
  if (parsed) {
    content += '\n--- parsed ---\n';
    content += '[INITIATOR]\n' + parsed.initiator + '\n';
    content += '[RECIPIENT]\n' + parsed.recipient + '\n';
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

// ——————————————————————————— EPUB builder ————————————————————————————

// Minimal ZIP writer (store, no compression) — just enough for .epub.
class ZipWriter {
  constructor() {
    this.entries = [];
  }

  add(filename, data) {
    this.entries.push({ name: filename.replace(/\\/g, '/'), data: Buffer.from(data, 'utf8') });
  }

  build() {
    const chunks = [];
    const cd = [];
    let offset = 0;

    const now = new Date();
    const dosTime = (now.getHours() << 11) | (now.getMinutes() << 5) | (now.getSeconds() >> 1);
    const dosDate = ((now.getFullYear() - 1980) << 9) | ((now.getMonth() + 1) << 5) | now.getDate();

    for (const e of this.entries) {
      const nameBuf = Buffer.from(e.name, 'ascii');
      const crc = crc32(e.data);
      const size = e.data.length;

      // local file header
      const lh = Buffer.alloc(30 + nameBuf.length);
      lh.writeUInt32LE(0x04034b50, 0);
      lh.writeUInt16LE(20, 4);
      lh.writeUInt16LE(0, 6);
      lh.writeUInt16LE(0, 8);   // store
      lh.writeUInt16LE(dosTime, 10);
      lh.writeUInt16LE(dosDate, 12);
      lh.writeUInt32LE(crc, 14);
      lh.writeUInt32LE(size, 18);
      lh.writeUInt32LE(size, 22);
      lh.writeUInt16LE(nameBuf.length, 26);
      lh.writeUInt16LE(0, 28);
      nameBuf.copy(lh, 30);

      chunks.push(lh, e.data);
      offset += lh.length + size;

      // central directory entry
      const ch = Buffer.alloc(46 + nameBuf.length);
      ch.writeUInt32LE(0x02014b50, 0);
      ch.writeUInt16LE(20, 4);
      ch.writeUInt16LE(20, 6);
      ch.writeUInt16LE(0, 8);
      ch.writeUInt16LE(0, 10);  // store
      ch.writeUInt16LE(dosTime, 12);
      ch.writeUInt16LE(dosDate, 14);
      ch.writeUInt32LE(crc, 16);
      ch.writeUInt32LE(size, 20);
      ch.writeUInt32LE(size, 24);
      ch.writeUInt16LE(nameBuf.length, 28);
      ch.writeUInt16LE(0, 30);
      ch.writeUInt16LE(0, 32);
      ch.writeUInt16LE(0, 34);
      ch.writeUInt32LE(0, 36);
      ch.writeUInt32LE(offset - lh.length - size, 42);
      nameBuf.copy(ch, 46);

      cd.push(ch);
    }

    const cdBuf = Buffer.concat(cd);
    const cdOffset = offset;

    // end of central directory
    const eocd = Buffer.alloc(22);
    eocd.writeUInt32LE(0x06054b50, 0);
    eocd.writeUInt16LE(0, 4);
    eocd.writeUInt16LE(0, 6);
    eocd.writeUInt16LE(this.entries.length, 8);
    eocd.writeUInt16LE(this.entries.length, 10);
    eocd.writeUInt32LE(cdBuf.length, 12);
    eocd.writeUInt32LE(cdOffset, 16);
    eocd.writeUInt16LE(0, 20);

    return Buffer.concat([...chunks, cdBuf, eocd]);
  }
}

function crc32(buf) {
  let crc = 0xFFFFFFFF;
  for (let i = 0; i < buf.length; i++) {
    crc ^= buf[i];
    for (let j = 0; j < 8; j++) {
      crc = (crc >>> 1) ^ (crc & 1 ? 0xEDB88320 : 0);
    }
  }
  return (crc ^ 0xFFFFFFFF) >>> 0;
}

function buildEpub() {
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

  const zip = new ZipWriter();
  const chapters = [];
  const date = new Date().toISOString().slice(0, 10);

  // mimetype (must be first, uncompressed)
  zip.add('mimetype', 'application/epub+zip');

  // container.xml
  zip.add('META-INF/container.xml', '<?xml version="1.0" encoding="UTF-8"?>\n<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">\n  <rootfiles>\n    <rootfile full-path="content.opf" media-type="application/oebps-package+xml"/>\n  </rootfiles>\n</container>\n');

  // Build chapter XHTML files from result files
  for (const dir of dirs) {
    const dirPath = path.join(RESULTS_DIR, dir);
    const files = fs.readdirSync(dirPath).filter(f => f.endsWith('.txt') && f !== 'catalog.txt').sort().reverse();
    if (!files.length) continue;

    // Take the newest result for this fixture
    const latest = files[0];
    const raw = fs.readFileSync(path.join(dirPath, latest), 'utf8');

    // Parse sections from the result file
    const sec = {};
    const sections = raw.split(/\n--- /);
    for (const s of sections) {
      const m = s.match(/^(\w+)\s*\n/);
      if (!m) continue;
      const key = m[1].trim().toLowerCase();
      const val = s.slice(m[0].length).trim();
      if (key === 'system') sec.system = val;
      else if (key === 'user') sec.user = val;
      else if (key === 'parsed') {
        sec.parsed = val;
        const im = val.match(/\[INITIATOR\]\s*([\s\S]*?)(?:\[RECIPIENT\]|$)/i);
        const rm = val.match(/\[RECIPIENT\]\s*([\s\S]*?)$/i);
        sec.initiator = im ? im[1].trim() : val.trim();
        sec.recipient = rm ? rm[1].trim() : '';
      } else if (key === 'raw response') sec.raw = val;
    }

    const title = dir.replace(/-/g, ' ');
    const body = [];
    if (sec.initiator) {
      body.push('<h3>Initiator</h3>', '<p>' + esc(sec.initiator) + '</p>');
      if (sec.recipient) {
        body.push('<h3>Recipient</h3>', '<p>' + esc(sec.recipient) + '</p>');
      }
    } else if (sec.raw) {
      body.push('<p>' + esc(sec.raw) + '</p>');
    }

    const chapId = 'ch' + (chapters.length + 1);
    const xhtml = `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head><title>${esc(title)}</title></head>
<body>
<h2>${esc(title)}</h2>
${body.join('\n')}
</body>
</html>
`;
    zip.add('OEBPS/' + chapId + '.xhtml', xhtml);
    chapters.push({ id: chapId, title, file: chapId + '.xhtml' });
  }

  // content.opf
  let manifest = '';
  let spine = '';
  for (const ch of chapters) {
    manifest += '    <item id="' + ch.id + '" href="' + ch.file + '" media-type="application/xhtml+xml"/>\n';
    spine += '    <itemref idref="' + ch.id + '"/>\n';
  }

  const opf = `<?xml version="1.0" encoding="UTF-8"?>
<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:identifier id="book-id">urn:pawndiary:${date}</dc:identifier>
    <dc:title>Pawn Diary — ${date}</dc:title>
    <dc:creator>Pawn Diary Mod</dc:creator>
    <dc:language>en</dc:language>
    <meta property="dcterms:modified">${new Date().toISOString()}</meta>
  </metadata>
  <manifest>
    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
${manifest}  </manifest>
  <spine>
${spine}  </spine>
</package>
`;
  zip.add('OEBPS/content.opf', opf);

  // nav.xhtml
  let tocItems = '';
  for (const ch of chapters) {
    tocItems += '    <li><a href="' + ch.file + '">' + esc(ch.title) + '</a></li>\n';
  }

  const nav = `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
<head><title>Contents</title></head>
<body>
<nav epub:type="toc">
<h2>Contents</h2>
<ol>
${tocItems}</ol>
</nav>
</body>
</html>
`;
  zip.add('OEBPS/nav.xhtml', nav);

  const epubBuf = zip.build();
  const outPath = path.join(ROOT, 'pawn-diary-' + date + '.epub');
  fs.writeFileSync(outPath, epubBuf);
  console.log('EPUB written: ' + path.relative(ROOT, outPath) + '  (' + chapters.length + ' chapters, ' + (epubBuf.length / 1024).toFixed(1) + ' KB)');
}

function esc(s) {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

(async () => {
  const args = process.argv.slice(2);

  if (args.includes('--epub')) {
    buildEpub();
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
