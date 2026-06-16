#!/usr/bin/env node
'use strict';

/**
 * Prompt lab harness for the PawnDiary XML-backed prompt format.
 * Builds OpenAI-compatible /chat/completions payloads from manual fixtures or
 * generated fixtures created from the mod's XML defs.
 */

const fs = require('fs');
const path = require('path');
const http = require('http');
const https = require('https');
const { URL } = require('url');

const ROOT = path.dirname(process.argv[1]);
const DEFAULT_CONFIG_PATH = path.join(ROOT, 'prompt-lab.config.json');
const DEFAULT_FIXTURE_DIR = path.join(ROOT, 'prompts', 'fixtures');
const DEFAULT_RESULT_RELATIVE_DIR = 'results';
const TITLE_TRAILER = 'Return one short title (3-8 words) for this diary entry. Output only the title -- no quotes, no period, no labels, no commentary.';
const TITLE_MAX_TOKENS = 40;

const DEFAULTS = {
  endpoint: 'http://localhost:1234/v1',
  model: 'local-model',
  apiKey: '',
  temperature: 0.8,
  maxTokens: 120,
  timeoutSeconds: 60,
  defaultSystemPrompt: path.join('prompts', '_system.txt'),
  defaultSystemPromptNeutral: path.join('prompts', '_system_neutral.txt'),
  defaultSystemPromptReflection: path.join('prompts', '_system_reflection.txt'),
  defaultSystemPromptTitle: path.join('prompts', '_system_title.txt'),
  promptDefFile: path.join('..', '1.6', 'Defs', 'DiaryPromptDef.xml'),
  personaDefFile: path.join('..', '1.6', 'Defs', 'DiaryPersonaDefs.xml'),
  interactionGroupDefFile: path.join('..', '1.6', 'Defs', 'DiaryInteractionGroupDefs.xml'),
  resultFolder: DEFAULT_RESULT_RELATIVE_DIR,
  generated: {
    includeGroups: 4,
    includePersonas: 4,
  },
  saveResults: false,
};

function safePathName(value) {
  return String(value || 'model')
    .toLowerCase()
    .replace(/[^a-z0-9._-]+/g, '_')
    .replace(/_+/g, '_')
    .replace(/^_+|_+$/g, '')
    .slice(0, 64) || 'model';
}

function parseArgs(argv) {
  const options = {
    configPath: DEFAULT_CONFIG_PATH,
    caseFilter: null,
    runAll: false,
    dryRun: false,
    saveResults: false,
    verbose: false,
    fromXml: false,
    skipTitle: false,
    endpoint: null,
    model: null,
    apiKey: null,
    includeGroups: null,
    includePersonas: null,
    fixtureDir: DEFAULT_FIXTURE_DIR,
    resultFolder: null,
    temperature: null,
    maxTokens: null,
    timeoutSeconds: null,
    help: false,
  };

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--config') {
      options.configPath = resolvePath(argv[i + 1]);
      i++;
      continue;
    }
    if (arg === '--case') {
      options.caseFilter = argv[i + 1];
      i++;
      continue;
    }
    if (arg === '--fixtures') {
      options.fixtureDir = resolvePath(argv[i + 1]);
      i++;
      continue;
    }
    if (arg === '--result-folder') {
      options.resultFolder = resolvePath(argv[i + 1]);
      i++;
      continue;
    }
    if (arg === '--all') {
      options.runAll = true;
      continue;
    }
    if (arg === '--dry-run') {
      options.dryRun = true;
      continue;
    }
    if (arg === '--save') {
      options.saveResults = true;
      continue;
    }
    if (arg === '--verbose') {
      options.verbose = true;
      continue;
    }
    if (arg === '--from-defs') {
      options.fromXml = true;
      continue;
    }
    if (arg === '--no-title') {
      options.skipTitle = true;
      continue;
    }
    if (arg === '--include-groups') {
      const value = parseInt(argv[i + 1], 10);
      options.includeGroups = Number.isFinite(value) ? Math.max(1, value) : null;
      i++;
      continue;
    }
    if (arg === '--include-personas') {
      const value = parseInt(argv[i + 1], 10);
      options.includePersonas = Number.isFinite(value) ? Math.max(1, value) : null;
      i++;
      continue;
    }
    if (arg === '--endpoint') {
      options.endpoint = argv[i + 1];
      i++;
      continue;
    }
    if (arg === '--model') {
      options.model = argv[i + 1];
      i++;
      continue;
    }
    if (arg === '--api-key') {
      options.apiKey = argv[i + 1];
      i++;
      continue;
    }
    if (arg === '--temperature') {
      const value = parseFloat(argv[i + 1]);
      options.temperature = Number.isFinite(value) ? value : null;
      i++;
      continue;
    }
    if (arg === '--max-tokens') {
      const value = parseInt(argv[i + 1], 10);
      options.maxTokens = Number.isFinite(value) ? value : null;
      i++;
      continue;
    }
    if (arg === '--timeout') {
      const value = parseInt(argv[i + 1], 10);
      options.timeoutSeconds = Number.isFinite(value) ? Math.max(1, value) : null;
      i++;
      continue;
    }
    if (arg === '--help' || arg === '-h') {
      options.help = true;
      continue;
    }
  }

  return options;
}

function usage() {
  return [
    'Usage:',
    '  node run.js --from-defs [--save] [--case <id>] [--all]',
    '  node run.js --case <id-or-file> [--fixtures <dir>] [--save]',
    '',
    'Options:',
    '  --config <path>              Config file path (defaults to prompt-lab.config.json)',
    '  --case <id-or-file>          Run one generated case or one fixture file',
    '  --fixtures <dir>             Manual fixture directory (default prompts/fixtures)',
    '  --result-folder <dir>         Save results under this root folder',
    '  --all                        Run all manual fixtures',
    '  --from-defs                   Build cases from XML defs',
    '  --include-groups <n>         Number of XML interaction groups to include in generated cases',
    '  --include-personas <n>        Number of XML personas used when building variants',
    '  --dry-run                     Print prompt payload only',
    '  --save                        Save outputs to prompt-lab/results/<model>/YYYY-mm-ddTHH-MM-SS.mmmZ.md',
    '  --verbose                     Print request payloads',
    '  --no-title                    Skip follow-up title generation',
    '  --endpoint <url>              Override endpoint',
    '  --model <name>                Override model',
    '  --api-key <key>               Override API key',
    '  --temperature <float>         Override temperature',
    '  --max-tokens <int>            Override max_tokens',
    '  --timeout <seconds>           Override request timeout',
    '  --help                        Show help',
  ].join('\n');
}

function resolvePath(candidate) {
  if (!candidate) return candidate;
  return path.isAbsolute(candidate) ? candidate : path.join(ROOT, candidate);
}

function readIfExists(filePath) {
  if (!filePath || !fs.existsSync(filePath)) {
    return null;
  }
  return fs.readFileSync(filePath, 'utf8');
}

function readJson(filePath, fallback) {
  const raw = readIfExists(filePath);
  if (raw == null) {
    return fallback;
  }
  return JSON.parse(raw);
}

function escapeRegex(value) {
  return String(value).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function decodeXmlEntities(value) {
  return String(value || '')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'");
}

function extractTag(block, tag) {
  const regex = new RegExp(`<${escapeRegex(tag)}>([\\s\\S]*?)<\\/${escapeRegex(tag)}>`, 'i');
  const match = block.match(regex);
  if (!match) return '';
  return decodeXmlEntities(match[1].trim());
}

function extractMultiTag(block, tag) {
  const regex = new RegExp(`<${escapeRegex(tag)}>([\\s\\S]*?)<\\/${escapeRegex(tag)}>`, 'g');
  const values = [];
  let match = null;
  while ((match = regex.exec(block)) !== null) {
    const value = match[1].trim();
    if (value.length > 0) {
      values.push(decodeXmlEntities(value));
    }
  }
  return values;
}

function extractBlocks(xml, tagName) {
  const escaped = escapeRegex(tagName);
  const pattern = new RegExp(`<${escaped}>([\\s\\S]*?)<\\/${escaped}>`, 'gi');
  const blocks = [];
  let match;
  while ((match = pattern.exec(xml)) !== null) {
    blocks.push(match[1]);
  }
  return blocks;
}

function parsePromptDefsFromXml(config) {
  const raw = readIfExists(resolvePath(config.promptDefFile));
  if (!raw) return null;
  return {
    defName: extractTag(raw, 'defName'),
    singlePovInstruction: extractTag(raw, 'singlePovInstruction'),
    recipientFollowupInstruction: extractTag(raw, 'recipientFollowupInstruction'),
    deathDescriptionInstruction: extractTag(raw, 'deathDescriptionInstruction'),
    arrivalDescriptionInstruction: extractTag(raw, 'arrivalDescriptionInstruction'),
    systemPrompt: extractTag(raw, 'systemPrompt'),
    systemPromptReflection: extractTag(raw, 'systemPromptReflection'),
    systemPromptNeutral: extractTag(raw, 'systemPromptNeutral'),
    titleSystemPrompt: extractTag(raw, 'titleSystemPrompt'),
  };
}

function parsePersonaDefsFromXml(config) {
  const raw = readIfExists(resolvePath(config.personaDefFile));
  if (!raw) return [];

  const blocks = extractBlocks(raw, 'PawnDiary.DiaryPersonaDef');
  return blocks.map((block) => ({
    defName: extractTag(block, 'defName'),
    label: extractTag(block, 'label'),
    rule: extractTag(block, 'rule'),
    themes: extractMultiTag(block, 'li'),
  }));
}

function parseInteractionGroupsFromXml(config) {
  const raw = readIfExists(resolvePath(config.interactionGroupDefFile));
  if (!raw) return [];

  const blocks = extractBlocks(raw, 'PawnDiary.DiaryInteractionGroupDef');
  return blocks.map((block) => {
    const orderValue = parseInt(extractTag(block, 'order'), 10);
    return {
      defName: extractTag(block, 'defName'),
      label: extractTag(block, 'label'),
      domain: extractTag(block, 'domain') || 'Interaction',
      order: Number.isFinite(orderValue) ? orderValue : 999,
      instruction: extractTag(block, 'instruction'),
      tone: extractTag(block, 'tone'),
      combat: /\b<combat>\s*true\s*<\/combat>/i.test(block),
      important: /\b<important>\s*true\s*<\/important>/i.test(block),
      catchAll: /\b<catchAll>\s*true\s*<\/catchAll>/i.test(block),
      matchTokens: extractMultiTag(block, 'li'),
    };
  });
}

function loadPromptData(config) {
  return {
    promptDefs: parsePromptDefsFromXml(config) || {
      singlePovInstruction: "Write one short first-person diary entry from this pawn's point of view. Output only the diary entry.",
      recipientFollowupInstruction: "Write one short first-person diary entry from the recipient's point of view. The initiator diary entry is hidden continuity context; do not write as if the recipient read it. Output only the diary entry.",
      deathDescriptionInstruction: 'Write one short, third-person death description. State how the colonist died using only the supplied facts.',
      arrivalDescriptionInstruction: 'Write one short, third-person colony arrival description. Explain how this pawn joined the colony using only the supplied scenario, pawn, and joining facts.',
      systemPrompt: 'You write diary entries for RimWorld colonists. Each entry is first-person, in character. One to three sentences.',
      systemPromptReflection: 'You write end-of-day diary reflections for RimWorld colonists. Each is first-person, in character, looking back on the whole day.',
      systemPromptNeutral: 'You write short, third-person factual notes about RimWorld colony events. One to three sentences.',
      titleSystemPrompt: 'You write short, evocative titles (3 to 8 words) for RimWorld diary entries. Return only the title — no quotes, no period, no markdown, no labels, no commentary.',
    },
    personas: parsePersonaDefsFromXml(config),
    groups: parseInteractionGroupsFromXml(config),
  };
}

function isSkippable(value) {
  if (!value) return true;
  const normalized = String(value).trim().toLowerCase();
  return normalized.length === 0 || normalized === 'none' || normalized === 'n/a' || normalized === 'unknown';
}

function cleanValue(value) {
  if (value == null) return '';
  if (Array.isArray(value)) {
    return value.filter(Boolean).join(', ');
  }
  return String(value).trim();
}

function buildPromptText(fixture) {
  if (typeof fixture.promptText === 'string') {
    return appendTrailer(fixture.promptText.trim(), fixture.append);
  }

  if (Array.isArray(fixture.promptLines)) {
    return appendTrailer(fixture.promptLines.map((line) => String(line).trim()).join('\n'), fixture.append);
  }

  const fields = fixture.promptFields || {};
  const ordered = Array.isArray(fixture.promptFieldOrder) && fixture.promptFieldOrder.length > 0
    ? fixture.promptFieldOrder
    : Object.keys(fields);
  const lines = [];

  for (const key of ordered) {
    const raw = fields[key];
    if (isSkippable(raw)) {
      continue;
    }
    lines.push(`${key}: ${cleanValue(raw)}`);
  }
  return appendTrailer(lines.join('\n'), fixture.append);
}

function appendTrailer(prompt, trailer) {
  if (!trailer || isSkippable(trailer)) {
    return (prompt || '').trim();
  }
  return `${(prompt || '').trim()}\n\n${trailer.trim()}`;
}

function normalizeEndpoint(endpoint) {
  const base = String(endpoint || DEFAULTS.endpoint).trim();
  let result = base.replace(/\/+$/, '');
  if (result.toLowerCase().endsWith('/chat/completions')) {
    result = result.slice(0, -'/chat/completions'.length);
  }
  return result || DEFAULTS.endpoint;
}

function toChatUrl(endpoint) {
  return `${normalizeEndpoint(endpoint)}/chat/completions`;
}

function getSystemForFixture(fixture, config, promptData) {
  if (fixture.systemText) return fixture.systemText;
  const promptDefs = promptData.promptDefs;
  const selectedMode = String(fixture.systemMode || 'diary').toLowerCase();
  if (selectedMode === 'neutral') {
    return fixture.system || promptDefs.systemPromptNeutral;
  }
  if (selectedMode === 'reflection') {
    return fixture.system || promptDefs.systemPromptReflection;
  }
  if (selectedMode === 'title') {
    return fixture.system || promptDefs.titleSystemPrompt || readIfExists(resolvePath(config.defaultSystemPromptTitle)) || '';
  }
  return fixture.system || promptDefs.systemPrompt;
}

function resolveTextReference(value) {
  if (!value) return '';
  if (value.startsWith('file:')) {
    const filePath = value.slice(5).trim();
    const target = path.isAbsolute(filePath) ? filePath : path.join(ROOT, filePath);
    return readIfExists(target) || '';
  }
  const target = path.isAbsolute(value) ? value : path.join(ROOT, value);
  return readIfExists(target) || '';
}

function pickPersona(personas, index) {
  if (!personas || personas.length === 0) {
    return {
      defName: 'Default',
      label: 'default',
      rule: 'practical and grounded. short, direct sentences.',
    };
  }
  return personas[(index % personas.length) % personas.length];
}

function fixtureId(entry, index) {
  if (entry.id) return entry.id;
  if (entry.name) return entry.name.replace(/\\.[^.]+$/, '');
  return `fixture_${index + 1}`;
}

function shouldRunFixture(entry, filter) {
  if (!filter || filter === 'all') return true;
  const lowered = String(filter).toLowerCase();
  return (
    String(entry.id || '').toLowerCase() === lowered ||
    String(entry.name || '').toLowerCase() === lowered ||
    String(entry.filePath || '').toLowerCase() === lowered
  );
}

function buildGeneratedFixtureSet(promptData, config, options) {
  const groupLimit = Number.isFinite(options.includeGroups)
    ? Math.max(1, options.includeGroups)
    : config.generated.includeGroups;
  const personaLimit = Number.isFinite(options.includePersonas)
    ? Math.max(1, options.includePersonas)
    : config.generated.includePersonas;

  const personas = pickRandom(personasFromData(promptData.personas), personaLimit);
  const groups = sortAndTrimGroups(promptData.groups).slice(0, Math.max(1, groupLimit));
  const cases = [];

  const pairGroups = groups.filter((group) => {
    if (group.domain === 'Interaction') return true;
    return group.domain === 'MentalState' && group.defName === 'socialfight';
  });
  const soloGroups = groups.filter((group) => !pairGroups.includes(group));
  const fallbackGroup = groups[0] || { defName: 'default', label: 'quiet colonist moment', instruction: 'a tense social moment', tone: 'tense but clear', domain: 'Interaction', combat: false };

  // Paired prompts (initiator + recipient), two versions each.
  for (let i = 0; i < pairGroups.length; i++) {
    const group = pairGroups[i];
    const primaryPersona = pickPersona(personas, i);
    const secondaryPersona = pickPersona(personas, i + 1);
    const tone = isSkippable(group.tone) ? 'tense but clear' : group.tone;
    const label = isSkippable(group.label) ? `social event ${group.defName}` : group.label;
    const coreInstruction = isSkippable(group.instruction)
      ? 'a charged social exchange between two colonists'
      : group.instruction;

    const pairBase = {
      promptFieldOrder: [
        'event', 'pov', 'role', 'with', 'what you saw', 'instruction', 'you', 'persona',
        'setting', 'atmosphere', 'tone', 'relationship', 'my last opener (not repeat)', 'burning passion', 'weapon',
      ],
      append: promptData.promptDefs.singlePovInstruction,
      systemMode: 'diary',
      type: 'pair',
    };

    cases.push({
      ...pairBase,
      id: `pair-${group.defName}-initiator-v1`,
      promptFields: {
        event: label.toLowerCase(),
        pov: 'Cass',
        role: 'initiator',
        with: 'Juno',
        'what you saw': `A tense exchange started over a practical disagreement. ${label} shifted the room's mood.`,
        instruction: coreInstruction,
        you: 'age=31; mood=tense; health=healthy; thoughts=restless',
        persona: primaryPersona.rule,
        setting: 'Biome=temperate forest; Weather=overcast; Time=sunset',
        atmosphere: tone,
        tone: tone,
        relationship: 'shared history: familiar, protective distance',
        'my last opener (not repeat)': 'The quiet had lasted too long.',
        'burning passion': group.important ? 'anger 7/10' : '',
        weapon: group.combat ? 'short spear' : '',
      },
      version: 'v1',
    });

    cases.push({
      ...pairBase,
      id: `pair-${group.defName}-initiator-v2`,
      promptFields: {
        event: label.toLowerCase(),
        pov: 'Cass',
        role: 'initiator',
        with: 'Juno',
        'what you saw': `They did not expect this in public. ${label} left everyone off balance for the rest of the watch.`,
        instruction: `${coreInstruction}. Keep it brief, with a practical register.`,
        you: 'age=31; mood=alert; health=healthy; thoughts=anticipating trouble',
        persona: primaryPersona.rule,
        setting: 'Biome=temperate forest; Weather=rain; Time=dusk',
        atmosphere: `${tone}; damp and sharp`,
        tone: `${tone}; restrained`,
        relationship: 'shared history: familiar but testing boundaries',
        'my last opener (not repeat)': 'I kept it professional.',
        'burning passion': group.important ? 'frustration 5/10' : '',
        weapon: group.combat ? 'short spear' : '',
      },
      version: 'v2',
    });

    cases.push({
      ...pairBase,
      id: `pair-${group.defName}-recipient-v1`,
      promptFields: {
        event: label.toLowerCase(),
        pov: 'Juno',
        role: 'recipient',
        with: 'Cass',
        'what you saw': `${label} was already half-decided before Cass spoke. She had to keep a calm face.`,
        instruction: coreInstruction,
        you: 'age=28; mood=cautious; health=healthy; thoughts=calculating',
        persona: secondaryPersona.rule,
        setting: 'Biome=temperate forest; Weather=overcast; Time=sunset',
        atmosphere: tone,
        tone: tone,
        relationship: 'shared history: practical loyalty',
        'my last opener (not repeat)': 'A private note was due later.',
        'burning passion': group.important ? 'pressure 6/10' : '',
        weapon: group.combat ? 'short spear' : '',
      },
      append: promptData.promptDefs.recipientFollowupInstruction,
      version: 'v1',
    });

    cases.push({
      ...pairBase,
      id: `pair-${group.defName}-recipient-v2`,
      promptFields: {
        event: label.toLowerCase(),
        pov: 'Juno',
        role: 'recipient',
        with: 'Cass',
        'what you saw': `${label} ended with a careful line that both could repeat publicly.`,
        instruction: `${coreInstruction}, but keep the emotional beat small and concrete.`,
        you: 'age=28; mood=steady; health=healthy; thoughts=resolved',
        persona: secondaryPersona.rule,
        setting: 'Biome=temperate forest; Weather=rain; Time=dusk',
        atmosphere: `${tone}; controlled`,
        tone: `${tone}; restrained`,
        relationship: 'shared history: practical and formal',
        'my last opener (not repeat)': 'I let the smallest word do the work.',
        'burning passion': '',
        weapon: group.combat ? 'short spear' : '',
      },
      append: promptData.promptDefs.recipientFollowupInstruction,
      version: 'v2',
    });
  }

  // Solo prompts from non-interaction and non-catchall groups, two versions.
  const soloSources = soloGroups.length > 0 ? soloGroups : [fallbackGroup];
  for (let i = 0; i < Math.min(soloSources.length, 2); i++) {
    const group = soloSources[i];
    const persona = pickPersona(personas, i);
    const tone = isSkippable(group.tone) ? 'nervous but grounded' : group.tone;
    const label = isSkippable(group.label) ? `solo moment ${group.defName}` : group.label;
    const baseInstruction = isSkippable(group.instruction)
      ? 'a private moment that changes the tone of the day'
      : group.instruction;

    const soloBase = {
      systemMode: group.defName === 'dayreflection' ? 'reflection' : 'diary',
      promptFieldOrder: [
        'event', 'pov', 'what happened', 'instruction', 'you', 'persona', 'setting',
        'atmosphere', 'tone', 'relationship', 'my last opener (not repeat)', 'burning passion',
      ],
      append: promptData.promptDefs.singlePovInstruction,
      type: 'solo',
    };

    cases.push({
      ...soloBase,
      id: `solo-${group.defName}-v1`,
      promptFields: {
        event: label.toLowerCase(),
        pov: i === 0 ? 'Cass' : 'Juno',
        'what happened': `${label} hit suddenly and left a physical trace in mood and movement.`,
        instruction: `${baseInstruction}; keep it immediate and grounded.`,
        you: `age=${30 + i}; mood=alert; health=healthy; thoughts=complicated`,
        persona: persona.rule,
        setting: 'Biome=arid hills; Weather=clear; Time=midday',
        atmosphere: tone,
        tone: tone,
        relationship: 'self: only',
        'my last opener (not repeat)': 'Nothing needed to be said out loud.',
        'burning passion': group.important ? 'strain 4/10' : '',
      },
      version: 'v1',
    });

    cases.push({
      ...soloBase,
      id: `solo-${group.defName}-v2`,
      promptFields: {
        event: label.toLowerCase(),
        pov: i === 0 ? 'Cass' : 'Juno',
        'what happened': `${label} unfolded over a long minute, then cooled into routine.`,
        instruction: `${baseInstruction} with a calm aftertaste.`,
        you: `age=${31 + i}; mood=measured; health=healthy; thoughts=reflective`,
        persona: persona.rule,
        setting: 'Biome=desert; Weather=windy; Time=dusk',
        atmosphere: `${tone}; practical`,
        tone: `${tone}; practical`,
        relationship: 'self: only',
        'my last opener (not repeat)': 'This is the part they will not remember well.',
        'burning passion': group.important ? 'relief 3/10' : '',
      },
      append: promptData.promptDefs.singlePovInstruction,
      version: 'v2',
    });
  }

  // Neutral descriptions (third-person, no persona).
  cases.push({
    id: 'arrival-colonist-v1',
    type: 'arrival',
    systemMode: 'neutral',
    promptText: null,
    promptFieldOrder: ['event', 'colonist', 'what happened', 'arrival facts', 'colonist pawn', 'setting'],
    promptFields: {
      event: 'colonist arrival',
      colonist: 'Rowan',
      'what happened': 'Founders settled on a ruined outpost and began rebuilding.',
      'arrival facts': 'arrival_pawn=Rowan; scenario=Stormbound Rescue; recruiter=warden Nia',
      'colonist pawn': 'age=27; role=farmer; mood=hopeful; sex=female; health=healthy',
      setting: 'Biome=temperate; Weather=rain; Time=night',
    },
    append: promptData.promptDefs.arrivalDescriptionInstruction,
    version: 'v1',
  });

  cases.push({
    id: 'arrival-colonist-v2',
    type: 'arrival',
    systemMode: 'neutral',
    promptText: null,
    promptFieldOrder: ['event', 'colonist', 'what happened', 'arrival facts', 'colonist pawn', 'setting'],
    promptFields: {
      event: 'colonist arrival',
      colonist: 'Mira',
      'what happened': 'Joined the colony late as an emergency transfer after a caravan event.',
      'arrival facts': 'arrival_pawn=Mira; scenario=Emergency Evac; recruiter=caravan captain',
      'colonist pawn': 'age=25; role=smith; mood=alert; sex=female; health=healthy',
      setting: 'Biome=ice; Weather=blizzard; Time=night',
    },
    append: promptData.promptDefs.arrivalDescriptionInstruction,
    version: 'v2',
  });

  cases.push({
    id: 'death-colonist-v1',
    type: 'death',
    systemMode: 'neutral',
    promptText: null,
    promptFieldOrder: ['event', 'deceased', 'what happened', 'death facts', 'deceased pawn', 'setting'],
    promptFields: {
      event: 'colonist death',
      deceased: 'Vale',
      'what happened': 'The colonist was cut down during a raid wave.',
      'death facts': 'death_victim=Vale; death_victim_role=initiator; cause=knife wound; destroyed_parts=right arm; nearby=outer gate',
      'deceased pawn': 'age=29; mood=exhausted; health=critical before death; sex=male',
      setting: 'Biome=marsh; Weather=storm; Time=night',
    },
    append: promptData.promptDefs.deathDescriptionInstruction,
    version: 'v1',
  });

  cases.push({
    id: 'death-colonist-v2',
    type: 'death',
    systemMode: 'neutral',
    promptText: null,
    promptFieldOrder: ['event', 'deceased', 'what happened', 'death facts', 'deceased pawn', 'setting'],
    promptFields: {
      event: 'colonist death',
      deceased: 'Arlo',
      'what happened': 'A sudden infection spread too fast for treatment.',
      'death facts': 'death_victim=Arlo; death_victim_role=recipient; cause=toxin fever; organs=lungs; nearby=medical bay',
      'deceased pawn': 'age=34; mood=pained; health=critical; sex=male',
      setting: 'Biome=desert; Weather=clear; Time=dawn',
    },
    append: promptData.promptDefs.deathDescriptionInstruction,
    version: 'v2',
  });

  // Title follow-ups use the same entry payload plus fixed title trailer.
  cases.push({
    id: 'title-followup-v1',
    type: 'title',
    systemMode: 'title',
    promptText: 'I snapped at him when the work order came in too late. The room went quiet, and then everyone blamed everyone.',
    append: TITLE_TRAILER,
    version: 'v1',
  });

  cases.push({
    id: 'title-followup-v2',
    type: 'title',
    systemMode: 'title',
    promptText: 'The storm came while I was still on watch. I kept a lamp lit and made choices nobody asked me to make.',
    append: TITLE_TRAILER,
    version: 'v2',
  });

  return cases;
}

function personasFromData(personas) {
  if (!personas || personas.length === 0) {
    return [{
      defName: 'Fallback',
      label: 'fallback',
      rule: 'practical and grounded; clear, short statements.',
    }];
  }
  return personas;
}

function sortAndTrimGroups(groups) {
  return [...groups]
    .filter((group) => group && group.defName)
    .sort((a, b) => {
      if (a.domain !== b.domain) {
        return String(a.domain).localeCompare(String(b.domain));
      }
      return (a.order || 9999) - (b.order || 9999);
    });
}

function pickRandom(entries, limit) {
  return [...entries].slice(0, Math.max(1, limit));
}

function buildRequestConfig(fixture, options, config) {
  return {
    endpoint: fixture.endpoint || options.endpoint || config.endpoint,
    model: fixture.model || options.model || config.model,
    apiKey: fixture.apiKey || options.apiKey || config.apiKey || '',
    temperature: Number.isFinite(fixture.temperature) ? fixture.temperature : options.temperature ?? config.temperature,
    maxTokens: Number.isFinite(fixture.maxTokens) ? fixture.maxTokens : options.maxTokens ?? config.maxTokens,
    timeoutSeconds: Number.isFinite(fixture.timeoutSeconds)
      ? fixture.timeoutSeconds
      : options.timeoutSeconds ?? config.timeoutSeconds,
  };
}

function buildPayload(fixture, systemText, requestSettings) {
  const prompt = buildPromptText(fixture);
  const messages = [];
  if (systemText && systemText.trim() !== '') {
    messages.push({ role: 'system', content: systemText });
  }
  messages.push({ role: 'user', content: prompt });

  return {
    model: requestSettings.model,
    messages,
    temperature: requestSettings.temperature,
    max_tokens: requestSettings.maxTokens,
  };
}

function parseModelResponse(body) {
  const json = JSON.parse(body);
  if (!json || !Array.isArray(json.choices) || json.choices.length === 0) {
    return { generated: '', error: 'Missing choices in response.' };
  }
  const first = json.choices[0];
  const content = first && first.message ? first.message.content : '';
  if (typeof content === 'string' && content.trim() !== '') {
    return { generated: content, error: null };
  }
  return { generated: '', error: 'No usable content in response.' };
}

function postJson(url, payload, apiKey, timeoutMs) {
  const body = JSON.stringify(payload);
  const requestUrl = new URL(url);
  const transport = requestUrl.protocol === 'https:' ? https : http;

  return new Promise((resolve, reject) => {
    const req = transport.request({
      protocol: requestUrl.protocol,
      hostname: requestUrl.hostname,
      port: requestUrl.port,
      path: `${requestUrl.pathname}${requestUrl.search}`,
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(body),
        Accept: 'application/json',
      },
    }, (res) => {
      const chunks = [];
      res.on('data', (chunk) => chunks.push(chunk));
      res.on('end', () => {
        resolve({
          statusCode: res.statusCode || 0,
          body: Buffer.concat(chunks).toString('utf8'),
        });
      });
    });

    req.on('error', (error) => reject(error));
    req.setTimeout(timeoutMs, () => req.destroy(new Error(`Request timeout after ${timeoutMs}ms`)));
    if (apiKey && apiKey.trim() !== '') {
      req.setHeader('Authorization', `Bearer ${apiKey.trim()}`);
    }
    req.write(body);
    req.end();
  });
}

function loadManualFixtures(dir) {
  if (!fs.existsSync(dir)) return [];

  return fs
    .readdirSync(dir, { withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith('.json'))
    .filter((entry) => !entry.name.startsWith('_'))
    .map((entry) => {
      const filePath = path.join(dir, entry.name);
      const data = readJson(filePath, null);
      return data ? { ...data, name: entry.name, filePath } : null;
    })
    .filter(Boolean);
}

function buildMarkdownOutput(runConfig) {
  const chunks = [];
  chunks.push('# Prompt-lab run result');
  chunks.push(`- Timestamp: ${runConfig.timestamp}`);
  chunks.push(`- Endpoint: ${runConfig.endpoint}`);
  chunks.push(`- Model: ${runConfig.model}`);
  chunks.push(`- Temperature: ${runConfig.temperature}`);
  chunks.push(`- Max tokens: ${runConfig.maxTokens}`);
  chunks.push('');

  for (const entry of runConfig.entries) {
    chunks.push(`## ${entry.id}`);
    chunks.push(`- status: ${entry.status}`);
    chunks.push(`- error: ${entry.error || 'none'}`);
    if (entry.model) {
      chunks.push(`- fixture model override: ${entry.model}`);
    }
    chunks.push('');
    chunks.push('### System');
    chunks.push('```');
    chunks.push(entry.systemText || '(none)');
    chunks.push('```');
    chunks.push('');
    chunks.push('### User prompt');
    chunks.push('```');
    chunks.push(entry.prompt || '(empty)');
    chunks.push('```');
    chunks.push('');
    chunks.push('### Response');
    chunks.push('```');
    chunks.push((entry.generated || '').trim() || '(empty)');
    chunks.push('```');
    if (
      entry.titleStatus !== undefined ||
      entry.title !== undefined ||
      entry.titleError !== undefined
    ) {
      chunks.push('');
      chunks.push('### Title');
      chunks.push(`- status: ${entry.titleStatus || 'not-generated'}`);
      if (entry.titleModel) {
        chunks.push(`- title model override: ${entry.titleModel}`);
      }
      if (entry.titleError) {
        chunks.push(`- error: ${entry.titleError}`);
      }
      chunks.push('');
      chunks.push('```');
      chunks.push((entry.title || '').trim() || '(not generated)');
      chunks.push('```');
    }
    chunks.push('');
  }

  return chunks.join('\n');
}

function isSuccessfulResponse(result) {
  const status = Number.parseInt(result.status, 10);
  return Number.isFinite(status) && status >= 200 && status < 300;
}

function shouldRunTitle(fixture, options, mainResult) {
  if (options.skipTitle) return false;
  if (!mainResult || mainResult.error) return false;
  if (!mainResult.generated || !mainResult.generated.trim()) return false;
  const selectedMode = String(fixture.systemMode || 'diary').toLowerCase();
  if (selectedMode === 'title') return false;
  if (fixture.skipTitle === true) return false;
  return true;
}

async function runTitleFollowUp(fixture, mainText, requestConfig, config, promptData, options, runState) {
  const titleFixture = {
    ...fixture,
    promptText: mainText,
    promptLines: undefined,
    promptFields: undefined,
    promptFieldOrder: undefined,
    systemMode: 'title',
    system: null,
    systemText: null,
    append: TITLE_TRAILER,
    model: fixture.model,
    endpoint: fixture.endpoint,
    apiKey: fixture.apiKey,
    temperature: fixture.temperature,
    maxTokens: fixture.maxTokens,
    timeoutSeconds: fixture.timeoutSeconds,
  };

  const titleConfig = {
    ...requestConfig,
    maxTokens: TITLE_MAX_TOKENS,
  };

  const titleSystemText = getSystemForFixture(titleFixture, config, promptData);
  const titlePromptText = buildPromptText(titleFixture);
  const titlePayload = buildPayload(titleFixture, titleSystemText, titleConfig);
  const titleEndpointUrl = toChatUrl(titleConfig.endpoint);

  const id = fixtureId(fixture, runState.index);
  const result = {
    titleStatus: 'not-run',
    title: '',
    titleModel: titleConfig.model,
    titleSystemText,
    titlePrompt: titlePromptText,
    titleError: null,
  };

  if (options.verbose) {
    console.log(`\n[${id} title]`);
    console.log('endpoint:', titleEndpointUrl);
    console.log('model:', titleConfig.model);
    console.log('system:', titleSystemText || '(none)');
    console.log('user:\n', titlePromptText);
    console.log('payload:', JSON.stringify(titlePayload, null, 2));
  }

  try {
    const response = await postJson(
      titleEndpointUrl,
      titlePayload,
      titleConfig.apiKey,
      Math.max(1000, titleConfig.timeoutSeconds * 1000),
    );
    result.titleStatus = String(response.statusCode || 0);
    if (response.statusCode < 200 || response.statusCode >= 300) {
      result.titleError = `HTTP ${response.statusCode}: ${response.body ? response.body.substring(0, 500) : 'empty response body'}`;
      return result;
    }

    const parsed = parseModelResponse(response.body);
    result.title = (parsed.generated || '').trim();
    result.titleError = parsed.error;
    return result;
  } catch (error) {
    result.titleStatus = 'error';
    result.titleError = error.message || String(error);
    return result;
  }
}

async function runOneFixture(fixture, config, promptData, options, runState) {
  const id = fixtureId(fixture, runState.index);
  const systemText = getSystemForFixture(fixture, config, promptData);
  const promptText = buildPromptText(fixture);
  const requestConfig = buildRequestConfig(fixture, options, config);
  const payload = buildPayload(fixture, systemText, requestConfig);
  const endpointUrl = toChatUrl(requestConfig.endpoint);

  const resultEntry = {
    id,
    status: 'not-run',
    endpoint: endpointUrl,
    model: requestConfig.model,
    temperature: requestConfig.temperature,
    maxTokens: requestConfig.maxTokens,
    systemText,
    prompt: promptText,
    generated: '',
    error: null,
  };

  if (options.verbose || options.dryRun) {
    console.log(`\n[${id}]`);
    console.log('endpoint:', endpointUrl);
    console.log('model:', requestConfig.model);
    console.log('system:', systemText || '(none)');
    console.log('user:\n', promptText);
    console.log('payload:', JSON.stringify(payload, null, 2));
  }

  if (options.dryRun) {
    resultEntry.status = 'dry-run';
    return resultEntry;
  }

  try {
    const response = await postJson(
      endpointUrl,
      payload,
      requestConfig.apiKey,
      Math.max(1000, requestConfig.timeoutSeconds * 1000)
    );
    resultEntry.status = String(response.statusCode || 0);
    if (response.statusCode < 200 || response.statusCode >= 300) {
      resultEntry.error = `HTTP ${response.statusCode}: ${response.body ? response.body.substring(0, 500) : 'empty response body'}`;
      return resultEntry;
    }
    const parsed = parseModelResponse(response.body);
    resultEntry.generated = parsed.generated || '';
    resultEntry.error = parsed.error;
    if (!isSuccessfulResponse(resultEntry)) {
      resultEntry.titleStatus = 'not-generated';
      resultEntry.titleError = resultEntry.error || 'Main response unavailable';
      return resultEntry;
    }

    if (shouldRunTitle(fixture, options, resultEntry)) {
      try {
        const titleResult = await runTitleFollowUp(
          fixture,
          resultEntry.generated,
          requestConfig,
          config,
          promptData,
          options,
          runState,
        );
        resultEntry.titleStatus = titleResult.titleStatus;
        resultEntry.title = titleResult.title;
        resultEntry.titleModel = titleResult.titleModel;
        resultEntry.titleSystemText = titleResult.titleSystemText;
        resultEntry.titlePrompt = titleResult.titlePrompt;
        resultEntry.titleError = titleResult.titleError;
      } catch (titleError) {
        resultEntry.titleStatus = 'error';
        resultEntry.titleError = titleError.message || String(titleError);
      }
    } else if (options.skipTitle) {
      resultEntry.titleStatus = 'disabled';
      resultEntry.titleError = 'Title generation skipped because --no-title was set';
    } else {
      resultEntry.titleStatus = 'not-generated';
      resultEntry.title = '';
      if (fixture.systemMode && String(fixture.systemMode).toLowerCase() === 'title') {
        resultEntry.titleError = 'Skipped: fixture is already title mode';
      } else if (!resultEntry.generated || !resultEntry.generated.trim()) {
        resultEntry.titleError = 'Skipped: empty main response';
      } else {
        resultEntry.titleError = resultEntry.error || 'Skipped';
      }
    }
    return resultEntry;
  } catch (error) {
    resultEntry.error = error.message || String(error);
    resultEntry.status = 'error';
    return resultEntry;
  }
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    console.log(usage());
    return;
  }

  const userConfig = readJson(resolvePath(args.configPath), {});
  const config = {
    ...DEFAULTS,
    ...userConfig,
  };
  config.generated = { ...DEFAULTS.generated, ...(config.generated || {}) };

  if (args.resultFolder) config.resultFolder = args.resultFolder;
  if (args.endpoint) config.endpoint = args.endpoint;
  if (args.model) config.model = args.model;
  if (args.apiKey) config.apiKey = args.apiKey;
  if (args.includeGroups) config.generated.includeGroups = args.includeGroups;
  if (args.includePersonas) config.generated.includePersonas = args.includePersonas;
  if (Number.isFinite(args.temperature)) config.temperature = args.temperature;
  if (Number.isFinite(args.maxTokens)) config.maxTokens = args.maxTokens;
  if (Number.isFinite(args.timeoutSeconds)) config.timeoutSeconds = args.timeoutSeconds;
  if (typeof args.saveResults === 'boolean') config.saveResults = args.saveResults;

  const promptData = loadPromptData(config);
  let fixtures = [];

  if (args.fromXml) {
    fixtures = buildGeneratedFixtureSet(promptData, config, config.generated);
    if (args.caseFilter) {
      fixtures = fixtures.filter((entry) => shouldRunFixture(entry, args.caseFilter));
    }
  } else {
    const loaded = loadManualFixtures(args.fixtureDir);
    if (args.caseFilter) {
      fixtures = loaded.filter((entry) => shouldRunFixture(entry, args.caseFilter));
    } else if (args.runAll) {
      fixtures = loaded;
    } else {
      fixtures = loaded;
    }
  }

  if (!fixtures || fixtures.length === 0) {
    console.log('No cases matched your filters.');
    return;
  }

  const runEntries = [];
  for (let i = 0; i < fixtures.length; i++) {
    const runState = { index: i };
    const entry = await runOneFixture(fixtures[i], config, promptData, args, runState);
    runEntries.push(entry);
    const generatedPreview = entry.generated ? `\n${entry.generated.split('\n').slice(0, 6).join('\n')}` : '';
    const titlePreview = entry.title ? `\nTitle: ${entry.title}` : '';
    console.log(`[${entry.id}] ${entry.status}${entry.error ? ` ERROR: ${entry.error}` : ''}${generatedPreview}${titlePreview}`);
  }

  if (!config.saveResults) return;

  const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
  const modelFolder = safePathName(args.model || config.model || 'default');
  const folder = path.join(ROOT, config.resultFolder || DEFAULT_RESULT_RELATIVE_DIR, modelFolder);
  fs.mkdirSync(folder, { recursive: true });
  const outPath = path.join(folder, `${timestamp}.md`);
  const md = buildMarkdownOutput({
    timestamp,
    endpoint: toChatUrl(config.endpoint || DEFAULTS.endpoint),
    model: args.model || config.model || DEFAULTS.model,
    temperature: args.temperature ?? config.temperature,
    maxTokens: args.maxTokens ?? config.maxTokens,
    entries: runEntries,
  });
  fs.writeFileSync(outPath, md, 'utf8');
  console.log(`Saved markdown results to ${outPath}`);
}

main().catch((error) => {
  console.error('prompt-lab failed:', error.message || error);
  process.exitCode = 1;
});
