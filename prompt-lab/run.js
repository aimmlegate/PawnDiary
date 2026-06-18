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
// Shared pure renderer, mirrored from the C# PromptAssembler and locked to it by the golden check
// (npm run check-golden). Generated fixtures render through this so the harness exercises the exact
// algorithm the mod ships, instead of a separate copy that could drift.
const assembler = require('./assembler');

const ROOT = path.dirname(process.argv[1]);
const DEFAULT_CONFIG_PATH = path.join(ROOT, 'prompt-lab.config.json');
const DEFAULT_FIXTURE_DIR = path.join(ROOT, 'prompts', 'fixtures');
const DEFAULT_RESULT_RELATIVE_DIR = 'results';
const DEFAULT_TITLE_USER_INSTRUCTION = 'Return one short title of three to eight words for this diary entry. Output only the title -- no quotes, no period, no labels, no commentary.';
const TITLE_MAX_TOKENS = 40;
const DEFAULT_PROMPT_ENCHANTMENT_VARIANTS = [
  {
    key: 'none',
    label: 'no prompt enchantment',
    promptEnchantment: '',
    pawnSummaryCue: 'health=healthy',
    setting: 'outdoors, overcast, temperate forest, doing steady colony work',
    toneSuffix: 'grounded',
  },
  {
    key: 'pain',
    label: 'moderate pain',
    promptEnchantment: 'high priority; moderate bruise in left arm; pain',
    pawnSummaryCue: 'health=moderate pain',
    setting: 'outdoors, cold rain, returning from perimeter work',
    toneSuffix: 'pain held back',
  },
  {
    key: 'blood-loss',
    label: 'major blood loss',
    promptEnchantment: 'high priority; major blood loss in torso; heavy bleeding, severe pain',
    pawnSummaryCue: 'health=major blood loss',
    setting: 'indoors, crowded medical room, waiting for treatment',
    toneSuffix: 'urgent',
  },
  {
    key: 'consciousness',
    label: 'critical consciousness',
    promptEnchantment: 'high priority; critical consciousness; near collapse, thoughts fragmented, barely awake',
    pawnSummaryCue: 'health=barely conscious',
    setting: 'indoors, dim barracks, half awake after treatment',
    toneSuffix: 'fragmented',
  },
  {
    key: 'fever',
    label: 'feverish sickness',
    promptEnchantment: 'high priority; major flu in whole body; fever, weakness, fogged awareness',
    pawnSummaryCue: 'health=feverish',
    setting: 'indoors, warm sickroom, medicine nearby',
    toneSuffix: 'feverish',
  },
  {
    key: 'intoxicated',
    label: 'intoxication',
    promptEnchantment: 'high priority; moderate alcohol high; dulled awareness, loose balance',
    pawnSummaryCue: 'health=intoxicated',
    setting: 'indoors, noisy rec room, late evening',
    toneSuffix: 'unsteady',
  },
  {
    key: 'sensory-loss',
    label: 'sensory loss',
    promptEnchantment: 'high priority; major blindness in both eyes; impaired sight, disoriented',
    pawnSummaryCue: 'health=impaired senses',
    setting: 'indoors, narrow hallway, moving by touch and memory',
    toneSuffix: 'disoriented',
  },
];

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
  promptTemplateDefFile: path.join('..', '1.6', 'Defs', 'DiaryPromptTemplateDefs.xml'),
  personaDefFile: path.join('..', '1.6', 'Defs', 'DiaryPersonaDefs.xml'),
  interactionGroupDefFile: path.join('..', '1.6', 'Defs', 'DiaryInteractionGroupDefs.xml'),
  keyedFile: path.join('..', 'Languages', 'English', 'Keyed', 'PawnDiary.xml'),
  resultFolder: DEFAULT_RESULT_RELATIVE_DIR,
  compactResults: false,
  generated: {
    includeGroups: 4,
    includePersonas: 4,
    excludeGroupDefNames: ['nsfw'],
    promptEnchantmentVariants: DEFAULT_PROMPT_ENCHANTMENT_VARIANTS,
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

function parsePositiveIntegerOrAll(value) {
  if (String(value || '').trim().toLowerCase() === 'all') {
    return 'all';
  }
  const parsed = parseInt(value, 10);
  return Number.isFinite(parsed) ? Math.max(1, parsed) : null;
}

function parseArgs(argv) {
  const options = {
    configPath: DEFAULT_CONFIG_PATH,
    caseFilter: null,
    dryRun: false,
    saveResults: false,
    compactResults: null,
    verbose: false,
    fromXml: false,
    allVariants: false,
    skipTitle: false,
    passes: 1,
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
    if (arg === '--dry-run') {
      options.dryRun = true;
      continue;
    }
    if (arg === '--save') {
      options.saveResults = true;
      continue;
    }
    if (arg === '--compact' || arg === '--compact-md') {
      options.compactResults = true;
      continue;
    }
    if (arg === '--full-md') {
      options.compactResults = false;
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
    if (arg === '--all-variants') {
      options.fromXml = true;
      options.allVariants = true;
      continue;
    }
    if (arg === '--no-title') {
      options.skipTitle = true;
      continue;
    }
    if (arg === '--passes') {
      const value = parseInt(argv[i + 1], 10);
      options.passes = Number.isFinite(value) ? Math.max(1, value) : 1;
      i++;
      continue;
    }
    if (arg === '--include-groups') {
      options.includeGroups = parsePositiveIntegerOrAll(argv[i + 1]);
      i++;
      continue;
    }
    if (arg === '--include-personas') {
      options.includePersonas = parsePositiveIntegerOrAll(argv[i + 1]);
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
    '  node run.js --from-defs [--save] [--case <id>]',
    '  node run.js --all-variants [--passes <n>] [--save] [--compact]',
    '  node run.js --case <id-or-file> [--fixtures <dir>] [--save]',
    '',
    'Options:',
    '  --config <path>              Config file path (defaults to prompt-lab.config.json)',
    '  --case <id-or-file>          Run one generated case or one fixture file',
    '  --fixtures <dir>             Manual fixture directory (default prompts/fixtures)',
    '  --result-folder <dir>         Save results under this root folder',
    '  --from-defs                   Build cases from XML defs',
    '  --all-variants                Build every XML event group across the prompt-enchantment matrix',
    '  --passes <n>                  Repeat each case with identical prompts for stability checks',
    '  --include-groups <n|all>      Number of XML interaction groups to include in sampled cases',
    '  --include-personas <n|all>    Number of XML personas used when building variants',
    '  --dry-run                     Print prompt payload only',
    '  --save                        Save outputs to prompt-lab/results/<model>/YYYY-mm-ddTHH-MM-SS.mmmZ.md',
    '  --compact, --compact-md       Save compact markdown: prompt + parsed result per case',
    '  --full-md                     Save the older verbose markdown format',
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
    titleUserInstruction: extractTag(raw, 'titleUserInstruction'),
  };
}

function extractBool(block, tagName, fallback) {
  const value = extractTag(block, tagName);
  if (value === '') return fallback;
  return /^true$/i.test(value.trim());
}

function parsePromptTemplateDefsFromXml(config) {
  const raw = readIfExists(resolvePath(config.promptTemplateDefFile));
  if (!raw) return [];

  const blocks = extractBlocks(raw, 'PawnDiary.DiaryPromptTemplateDef');
  return blocks.map((block) => {
    const fieldsBlock = extractTag(block, 'fields');
    const fieldBlocks = fieldsBlock ? extractBlocks(fieldsBlock, 'li') : [];
    const defName = extractTag(block, 'defName');
    return {
      defName,
      templateKey: extractTag(block, 'templateKey') || defName,
      systemPrompt: extractTag(block, 'systemPrompt'),
      finalInstruction: extractTag(block, 'finalInstruction'),
      recipientFinalInstruction: extractTag(block, 'recipientFinalInstruction'),
      includePromptEnchantment: extractBool(block, 'includePromptEnchantment', true),
      includePersona: extractBool(block, 'includePersona', true),
      appendDirectSpeechInstruction: extractBool(block, 'appendDirectSpeechInstruction', true),
      fields: fieldBlocks
        .map((fieldBlock) => ({
          enabled: extractBool(fieldBlock, 'enabled', true),
          label: extractTag(fieldBlock, 'label'),
          source: extractTag(fieldBlock, 'source'),
          contextKey: extractTag(fieldBlock, 'contextKey'),
        }))
        .filter((field) => field.enabled && field.label && field.source),
    };
  }).filter((template) => template.templateKey && template.fields.length > 0);
}

function parseKeyedPromptText(config) {
  const defaults = {
    pairInitiatorDirectSpeech: 'Initiator POV for {0}: quotation marks may contain only words {0} plausibly said. If {0} did not speak, use no quoted dialogue and write {0}\'s private reaction instead.',
    pairRecipientDirectSpeech: 'Recipient POV for {0}: quotation marks may contain only words {0} plausibly said. If {0} did not speak, use no quoted dialogue and write {0}\'s private reaction instead.',
    soloInteractionDirectSpeech: 'Single-POV interaction for {0}: quotation marks may contain only words {0} plausibly said. If {0} did not speak, use no quoted dialogue and write {0}\'s private reaction instead.',
    // Mirrors PawnDiary.Prompt.PersonaVoice: the persona voice block appended to the system prompt.
    personaVoice: "Write in this colonist's own voice — someone who {0} Let this voice shape word choice, rhythm, and which details they notice, not only what happens.",
  };
  const raw = readIfExists(resolvePath(config.keyedFile));
  if (!raw) return defaults;

  return {
    pairInitiatorDirectSpeech: extractTag(raw, 'PawnDiary.Prompt.PairDirectSpeechInstruction.Initiator') || defaults.pairInitiatorDirectSpeech,
    pairRecipientDirectSpeech: extractTag(raw, 'PawnDiary.Prompt.PairDirectSpeechInstruction.Recipient') || defaults.pairRecipientDirectSpeech,
    soloInteractionDirectSpeech: extractTag(raw, 'PawnDiary.Prompt.SoloInteractionDirectSpeechInstruction') || defaults.soloInteractionDirectSpeech,
    personaVoice: extractTag(raw, 'PawnDiary.Prompt.PersonaVoice') || defaults.personaVoice,
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
    const batchBlock = extractTag(block, 'batch');
    return {
      defName: extractTag(block, 'defName'),
      label: extractTag(block, 'label'),
      domain: extractTag(block, 'domain') || 'Interaction',
      order: Number.isFinite(orderValue) ? orderValue : 999,
      instruction: extractTag(block, 'instruction'),
      tone: extractTag(block, 'tone'),
      combat: /<combat>\s*true\s*<\/combat>/i.test(block),
      important: /<important>\s*true\s*<\/important>/i.test(block),
      catchAll: /<catchAll>\s*true\s*<\/catchAll>/i.test(block),
      hasBatch: !!batchBlock && !/<enabled>\s*false\s*<\/enabled>/i.test(batchBlock),
      batchMode: batchBlock ? (extractTag(batchBlock, 'mode') || 'PairEvent') : '',
      matchTokens: extractMultiTag(block, 'li'),
    };
  });
}

function loadPromptData(config) {
  return {
    promptDefs: parsePromptDefsFromXml(config) || {
      singlePovInstruction: 'Write one to three first-person diary sentences as this colonist, about the event above. If the notes are thin, react specifically to what happened rather than inventing detail. Output only the diary entry.',
      recipientFollowupInstruction: "Write one to three first-person diary sentences from the recipient's point of view, about the event above. The initiator's diary entry is hidden continuity context — do not write as if the recipient read it. If the notes are thin, react specifically to what happened rather than inventing detail. Output only the diary entry.",
      deathDescriptionInstruction: 'Write one to three complete third-person death-description sentences. Keep it brief. State how the colonist died using only the supplied facts. Output only the death description.',
      arrivalDescriptionInstruction: 'Write one to three complete third-person colony-arrival sentences. Keep it brief. Explain how this pawn joined the colony using only the supplied scenario, pawn, and joining facts. Output only the arrival description.',
      systemPrompt: "You write first-person diary entries for RimWorld colonists, one to three sentences in the colonist's own voice. Anchor each entry in one concrete sensation or detail from the supplied context, let mood/health/setting/tone color the subtext, invent nothing not supplied, and return only the diary text. (Persona voice is appended separately.)",
      systemPromptReflection: "You write end-of-day diary reflections for RimWorld colonists, first-person, two to four sentences in the colonist's voice. Anchor the reflection in one or two of the day's listed moments that still weigh on them, reflect on how the day felt, invent nothing not in the notes, and return only the diary text.",
      systemPromptNeutral: 'You write short, third-person factual notes about RimWorld colony events. Each note is one to three complete sentences. Use only supplied facts and return only the note text.',
      titleSystemPrompt: 'You write short, evocative titles (3 to 8 words) for RimWorld diary entries. Return only the title — no quotes, no period, no markdown, no labels, no commentary.',
      titleUserInstruction: DEFAULT_TITLE_USER_INSTRUCTION,
    },
    promptTemplates: parsePromptTemplateDefsFromXml(config),
    personas: parsePersonaDefsFromXml(config),
    groups: parseInteractionGroupsFromXml(config),
    keyedPromptText: parseKeyedPromptText(config),
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

  if (fixture.assembler) {
    return (assembler.render(fixture.assembler).userPrompt || '').trim();
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

function resultRootFolder(config) {
  const configured = config.resultFolder || DEFAULT_RESULT_RELATIVE_DIR;
  return path.isAbsolute(configured) ? configured : path.join(ROOT, configured);
}

// Mirrors PromptAssembler.ComposeSystem (run from DiaryPromptPlanner.Build): append the pawn's persona voice to the system
// prompt (so the voice governs HOW the entry is written) unless the template opts out via
// includePersona. The neutral death/arrival chronicles and the title flow stay persona-free.
function appendPersonaToSystem(baseSystem, template, personaRule, keyed) {
  if (template && template.includePersona === false) return baseSystem;
  const rule = cleanValue(personaRule);
  const voiceTemplate = keyed && keyed.personaVoice;
  if (isSkippable(rule) || isSkippable(voiceTemplate)) return baseSystem;
  const block = formatInstructionTemplate(voiceTemplate, [rule]);
  if (isSkippable(block)) return baseSystem;
  if (isSkippable(baseSystem)) return block;
  return `${String(baseSystem).trimEnd()}\n\n${block}`;
}

function getSystemForFixture(fixture, config, promptData) {
  if (fixture.systemText) return fixture.systemText;
  if (fixture.templateKey) {
    const baseSystem = fixture.system || systemPromptForTemplate(promptData, config, fixture.templateKey);
    const template = resolvedTemplate(promptData, fixture.templateKey);
    return appendPersonaToSystem(baseSystem, template, fixture.personaRule, promptData.keyedPromptText);
  }
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

function titleUserInstruction(promptData) {
  return promptData.promptDefs.titleUserInstruction || DEFAULT_TITLE_USER_INSTRUCTION;
}

function templateForKey(promptData, key) {
  const normalized = String(key || '').toLowerCase();
  const templates = promptData.promptTemplates || [];
  return templates.find((template) =>
    String(template.templateKey || '').toLowerCase() === normalized
    || String(template.defName || '').toLowerCase() === normalized);
}

function fallbackTemplateFields(key) {
  if (key === 'DeathDescription') {
    return [
      ['event', 'EventNoun'],
      ['deceased', 'DeathVictim'],
      ['what happened', 'NeutralText'],
      ['death facts', 'DeathFacts'],
      ['deceased pawn', 'DeathPawnSummary'],
      ['setting', 'DeathSetting'],
    ];
  }
  if (key === 'ArrivalDescription') {
    return [
      ['event', 'EventNoun'],
      ['colonist', 'ArrivalPawn'],
      ['what happened', 'NeutralText'],
      ['arrival facts', 'ArrivalFacts'],
      ['colonist pawn', 'PawnSummary'],
      ['setting', 'Setting'],
    ];
  }
  if (key === 'Title') {
    return [['entry', 'EntryText']];
  }
  // Persona is injected into the system prompt (see appendPersonaToSystem), not a user field.
  return [
    ['event', 'EventNoun'],
    ['pov', 'PovName'],
    ['what happened', 'PovText'],
    ['instruction', 'Instruction'],
    ['important health', 'PromptEnchantment'],
    ['setting', 'Setting'],
    ['my last opener (not repeat)', 'LastOpener'],
  ];
}

function resolvedTemplate(promptData, key) {
  return templateForKey(promptData, key) || {
    templateKey: key,
    includePromptEnchantment: key !== 'DeathDescription' && key !== 'ArrivalDescription' && key !== 'Title',
    includePersona: key !== 'DeathDescription' && key !== 'ArrivalDescription' && key !== 'Title',
    appendDirectSpeechInstruction: key !== 'DeathDescription' && key !== 'ArrivalDescription' && key !== 'Title' && key !== 'SoloDayReflection',
    fields: fallbackTemplateFields(key).map(([label, source]) => ({ label, source, enabled: true })),
  };
}

function templateKeyForFixture(group, context, hasOtherPawn) {
  const combat = !!(group && group.combat);
  const important = !!(group && group.important);
  const batched = hasContext(context, 'batch=');
  const dayReflection = isDayReflectionGroup(group, context);
  const internalState = hasAnyContext(context, ['mood_event=', 'thought=', 'inspiration=', 'work=', 'hediff=']);

  if (hasOtherPawn) {
    if (combat) return 'PairCombat';
    if (batched) return 'PairBatched';
    return important ? 'PairImportant' : 'PairDefault';
  }

  if (dayReflection) return 'SoloDayReflection';
  if (internalState) return 'SoloInternalState';
  if (batched) return 'SoloBatched';
  return important ? 'SoloImportant' : 'SoloDefault';
}

function systemPromptForTemplate(promptData, config, templateKey) {
  const template = resolvedTemplate(promptData, templateKey);
  if (!isSkippable(template.systemPrompt)) {
    return template.systemPrompt;
  }
  if (templateKey === 'DeathDescription' || templateKey === 'ArrivalDescription') {
    return promptData.promptDefs.systemPromptNeutral;
  }
  if (templateKey === 'SoloDayReflection') {
    return promptData.promptDefs.systemPromptReflection;
  }
  if (templateKey === 'Title') {
    return promptData.promptDefs.titleSystemPrompt || readIfExists(resolvePath(config.defaultSystemPromptTitle)) || '';
  }
  return promptData.promptDefs.systemPrompt;
}

function finalInstructionForTemplate(promptData, templateKey) {
  const template = resolvedTemplate(promptData, templateKey);
  if (!isSkippable(template.finalInstruction)) {
    return template.finalInstruction;
  }
  if (templateKey === 'Title') return titleUserInstruction(promptData);
  if (templateKey === 'DeathDescription') return promptData.promptDefs.deathDescriptionInstruction;
  if (templateKey === 'ArrivalDescription') return promptData.promptDefs.arrivalDescriptionInstruction;
  return promptData.promptDefs.singlePovInstruction;
}

function recipientInstructionForTemplate(promptData, templateKey) {
  const template = resolvedTemplate(promptData, templateKey);
  if (!isSkippable(template.recipientFinalInstruction)) {
    return template.recipientFinalInstruction;
  }
  return promptData.promptDefs.recipientFollowupInstruction;
}

// Maps the harness's loosely-named option bag (event/pov/other/...) onto the assembler's value names
// (eventNoun/povName/otherName/...), so generated fixtures can render through the shared assembler.
function toAssemblerValues(values) {
  const v = values || {};
  return {
    eventNoun: v.event,
    povName: v.pov,
    povRole: v.role,
    otherName: v.other,
    povText: v.povText,
    neutralText: v.neutralText,
    instruction: v.instruction,
    pawnSummary: v.pawnSummary,
    persona: v.persona,
    promptEnchantment: v.promptEnchantment,
    includePromptEnchantment: v.includePromptEnchantment,
    setting: v.setting,
    tone: v.tone,
    relationship: v.relationship,
    lastOpener: v.lastOpener,
    weapon: v.weapon,
    initiatorEntry: v.initiatorEntry,
    deathVictim: v.deathVictim,
    deathFacts: v.deathFacts,
    deathPawnSummary: v.deathPawnSummary,
    deathSetting: v.deathSetting,
    arrivalPawn: v.arrivalPawn,
    arrivalFacts: v.arrivalFacts,
    entryText: v.entryText,
    gameContext: v.gameContext,
  };
}

// Builds the shared-assembler input for a generated fixture: the template's field list and flags,
// the resolved values bag, and the fully-assembled final instruction.
function assemblerInputFor(template, values, finalInstruction) {
  return {
    templateKey: template.templateKey,
    fields: template.fields || [],
    includePersona: template.includePersona !== false,
    values: toAssemblerValues(values),
    finalInstruction,
  };
}

function fieldsFromTemplate(template, values) {
  const fields = {};
  const order = [];
  const assemblerValues = toAssemblerValues(values);
  const sourceFields = (template && template.fields) || [];

  for (const field of sourceFields) {
    if (!field || field.enabled === false) {
      continue;
    }
    const label = isSkippable(field.label) ? field.source : field.label;
    const value = assembler.resolveSource(field, assemblerValues);
    if (isSkippable(label) || isSkippable(value)) {
      continue;
    }
    fields[label] = cleanValue(value);
    order.push(label);
  }

  return { fields, order };
}

function domainOf(group) {
  return String((group && group.domain) || 'Interaction');
}

function defNameOf(group) {
  return String((group && group.defName) || 'default');
}

function generatedGroupLabel(group, fallback) {
  return isSkippable(group && group.label) ? fallback : group.label;
}

function hasContext(context, marker) {
  return String(context || '').toLowerCase().includes(String(marker || '').toLowerCase());
}

function hasAnyContext(context, markers) {
  return markers.some((marker) => hasContext(context, marker));
}

function isAmbientBatch(group) {
  return !!group
    && group.hasBatch
    && String(group.batchMode || '').toLowerCase() === 'ambientdaynote';
}

function isPairBatch(group) {
  return !!group
    && group.hasBatch
    && String(group.batchMode || '').toLowerCase() !== 'ambientdaynote';
}

function isDayReflectionGroup(group, context) {
  return defNameOf(group).toLowerCase() === 'dayreflection' || hasContext(context, 'day_reflection=');
}

function generatedContextForGroup(group, kind) {
  const defName = defNameOf(group);
  const label = cleanValue(generatedGroupLabel(group, defName));
  const domain = domainOf(group);

  if (isDayReflectionGroup(group, '')) {
    return `day_reflection=true; label=${label}`;
  }

  if (isAmbientBatch(group)) {
    return `batch=ambient_day_note; group=${defName}; label=${label}`;
  }

  if (kind === 'pair' && isPairBatch(group)) {
    return `batch=interaction; group=${defName}; label=${label}`;
  }

  if (domain === 'MentalState') {
    return `mental_state=${defName}; label=${label}`;
  }
  if (domain === 'Tale') {
    return `tale=${defName}; label=${label}`;
  }
  if (domain === 'MoodEvent') {
    return `mood_event=${defName}; label=${label}`;
  }
  if (domain === 'Thought') {
    return `thought=${defName}; label=${label}`;
  }
  if (domain === 'Inspiration') {
    return `inspiration=${defName}; label=${label}`;
  }
  if (domain === 'Work') {
    return `work=${defName}; label=${label}`;
  }
  if (domain === 'Hediff') {
    return `hediff=${defName}; label=${label}`;
  }

  return `def=${defName}; label=${label}`;
}

function appendInstructionText(instruction, extraInstruction) {
  if (isSkippable(extraInstruction)) {
    return instruction;
  }
  if (isSkippable(instruction)) {
    return extraInstruction;
  }
  return `${String(instruction).trimEnd()} ${String(extraInstruction).trim()}`;
}

function formatInstructionTemplate(template, args) {
  return String(template || '').replace(/\{(\d+)\}/g, (match, index) => {
    const value = args[Number.parseInt(index, 10)];
    return value == null ? match : value;
  });
}

function isInteractionPromptForLab(group, context) {
  if (hasAnyContext(context, [
    'batch=ambient_day_note',
    'arrival_description=',
    'death_description=',
    'dev_mock=',
    'mental_state=',
    'tale=',
    'mood_event=',
    'thought=',
    'inspiration=',
    'work=',
    'hediff=',
    'day_reflection=',
  ])) {
    return false;
  }

  return hasContext(context, 'batch=interaction')
    || (domainOf(group) === 'Interaction'
      && defNameOf(group).toLowerCase() !== 'arrival'
      && defNameOf(group).toLowerCase() !== 'dayreflection');
}

function pairInstructionForFixture(promptData, group, context, role, povName, otherName, baseInstruction) {
  if (!isInteractionPromptForLab(group, context)) {
    return baseInstruction;
  }

  const key = role === 'initiator' ? 'pairInitiatorDirectSpeech' : 'pairRecipientDirectSpeech';
  const template = promptData.keyedPromptText[key];
  return appendInstructionText(baseInstruction, formatInstructionTemplate(template, [povName, otherName]));
}

function soloInstructionForFixture(promptData, group, context, povName, baseInstruction) {
  if (!isInteractionPromptForLab(group, context)) {
    return baseInstruction;
  }

  const template = promptData.keyedPromptText.soloInteractionDirectSpeech;
  return appendInstructionText(baseInstruction, formatInstructionTemplate(template, [povName]));
}

function isGeneratedPairGroup(group) {
  const domain = domainOf(group);
  const defName = defNameOf(group).toLowerCase();
  if (domain === 'MentalState' && defName === 'socialfight') {
    return true;
  }
  return domain === 'Interaction'
    && defName !== 'arrival'
    && defName !== 'dayreflection'
    && !isAmbientBatch(group);
}

function isGeneratedSoloGroup(group) {
  return !!group && !isGeneratedPairGroup(group) && defNameOf(group).toLowerCase() !== 'arrival';
}

function buildPairFixture(promptData, group, options) {
  const context = generatedContextForGroup(group, 'pair');
  const templateKey = templateKeyForFixture(group, context, true);
  const template = resolvedTemplate(promptData, templateKey);
  const hiddenInitiatorEntry = options.role === 'recipient' ? options.initiatorEntry : '';
  const usesFollowupInstruction = options.role === 'recipient' && !isSkippable(hiddenInitiatorEntry);
  const baseInstruction = usesFollowupInstruction
    ? recipientInstructionForTemplate(promptData, templateKey)
    : finalInstructionForTemplate(promptData, templateKey);
  const append = template.appendDirectSpeechInstruction
    ? pairInstructionForFixture(
      promptData,
      group,
      context,
      options.role,
      options.pov,
      options.other,
      baseInstruction,
    )
    : baseInstruction;

  const assemblerInput = assemblerInputFor(template, {
    event: options.event,
    pov: options.pov,
    role: options.role,
    other: options.other,
    povText: options.whatYouSaw,
    instruction: options.instruction,
    pawnSummary: options.pawnSummary,
    persona: options.persona,
    promptEnchantment: options.promptEnchantment,
    includePromptEnchantment: template.includePromptEnchantment,
    setting: options.setting,
    tone: options.tone,
    relationship: options.relationship,
    lastOpener: options.lastOpener,
    weapon: options.weapon,
    initiatorEntry: hiddenInitiatorEntry,
  }, append);

  return {
    id: options.id,
    templateKey,
    assembler: assemblerInput,
    // Persona now rides in the system prompt (composed via the shared assembler), not the field list.
    personaRule: options.persona,
    type: 'pair',
    gameContext: context,
    version: options.version,
  };
}

function buildSoloFixture(promptData, group, options) {
  const context = generatedContextForGroup(group, 'solo');
  const templateKey = templateKeyForFixture(group, context, false);
  const template = resolvedTemplate(promptData, templateKey);
  const baseInstruction = finalInstructionForTemplate(promptData, templateKey);
  const append = template.appendDirectSpeechInstruction
    ? soloInstructionForFixture(
      promptData,
      group,
      context,
      options.pov,
      baseInstruction,
    )
    : baseInstruction;
  const assemblerInput = assemblerInputFor(template, {
    event: options.event,
    pov: options.pov,
    povText: options.whatHappened,
    instruction: options.instruction,
    pawnSummary: options.pawnSummary,
    persona: options.persona,
    promptEnchantment: options.promptEnchantment,
    includePromptEnchantment: template.includePromptEnchantment,
    setting: options.setting,
    tone: options.tone,
    relationship: '',
    lastOpener: options.lastOpener,
    weapon: options.weapon,
    initiatorEntry: '',
  }, append);

  return {
    id: options.id,
    templateKey,
    assembler: assemblerInput,
    // Persona now rides in the system prompt (composed via the shared assembler), not the field list.
    personaRule: options.persona,
    type: 'solo',
    gameContext: context,
    version: options.version,
  };
}

function buildStaticTemplateFixture(promptData, options) {
  const template = resolvedTemplate(promptData, options.templateKey);
  const rendered = fieldsFromTemplate(template, {
    event: options.event,
    neutralText: options.neutralText,
    pawnSummary: options.pawnSummary,
    setting: options.setting,
    deathVictim: options.deathVictim,
    deathFacts: options.deathFacts,
    deathPawnSummary: options.deathPawnSummary,
    deathSetting: options.deathSetting,
    arrivalPawn: options.arrivalPawn,
    arrivalFacts: options.arrivalFacts,
    entryText: options.entryText,
    includePromptEnchantment: template.includePromptEnchantment,
    gameContext: options.gameContext,
  });

  return {
    id: options.id,
    type: options.type,
    templateKey: options.templateKey,
    promptFieldOrder: rendered.order,
    promptFields: rendered.fields,
    append: finalInstructionForTemplate(promptData, options.templateKey),
    gameContext: options.gameContext || '',
    version: options.version,
  };
}

function buildGeneratedFixtureSet(promptData, config, options) {
  const excludedGroupNames = new Set((config.generated.excludeGroupDefNames || [])
    .map((name) => String(name || '').toLowerCase())
    .filter(Boolean));
  const allPersonas = personasFromData(promptData.personas);
  const allGroups = sortAndTrimGroups(promptData.groups, excludedGroupNames);
  if (options.allVariants) {
    return buildAllVariantFixtureSet(promptData, config, options, allGroups, allPersonas);
  }

  const groupLimit = resolveGeneratedLimit(options.includeGroups, config.generated.includeGroups, allGroups.length);
  const personaLimit = resolveGeneratedLimit(options.includePersonas, config.generated.includePersonas, allPersonas.length);
  const personas = pickRandom(allPersonas, personaLimit);
  const groups = allGroups.slice(0, groupLimit);
  const cases = [];

  const pairGroups = groups.filter(isGeneratedPairGroup);
  const soloGroups = groups.filter(isGeneratedSoloGroup);
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

    cases.push(buildPairFixture(promptData, group, {
      id: `pair-${group.defName}-initiator-v1`,
      event: label.toLowerCase(),
      pov: 'Cass',
      role: 'initiator',
      other: 'Juno',
      whatYouSaw: `A tense exchange started over a practical disagreement. ${label} shifted the room's mood.`,
      instruction: coreInstruction,
      pawnSummary: 'sex=female; life_stage=adult; mood=tense; thoughts=restless',
      persona: primaryPersona.rule,
      promptEnchantment: '',
      setting: 'outdoors, overcast, temperate forest, doing guard duty',
      tone,
      relationship: 'opinion=cold; because insulted recently; last wrote: I kept my mouth shut too long.',
      lastOpener: 'The quiet had lasted too long.',
      weapon: group.combat ? 'short spear' : '',
      initiatorEntry: '',
      version: 'v1',
    }));

    cases.push(buildPairFixture(promptData, group, {
      id: `pair-${group.defName}-initiator-v2`,
      event: label.toLowerCase(),
      pov: 'Cass',
      role: 'initiator',
      other: 'Juno',
      whatYouSaw: `They did not expect this in public. ${label} left everyone off balance for the rest of the watch.`,
      instruction: `${coreInstruction}. Keep it brief, with a practical register.`,
      pawnSummary: 'sex=female; life_stage=adult; mood=alert; thoughts=anticipating trouble',
      persona: primaryPersona.rule,
      promptEnchantment: 'high priority; moderate bruise in left arm; pain',
      setting: 'outdoors, rain, temperate forest, doing perimeter watch',
      tone: `${tone}; restrained`,
      relationship: 'opinion=wary; because tested boundaries; last wrote: I kept it professional.',
      lastOpener: 'I kept it professional.',
      weapon: group.combat ? 'short spear' : '',
      initiatorEntry: '',
      version: 'v2',
    }));

    cases.push(buildPairFixture(promptData, group, {
      id: `pair-${group.defName}-recipient-v1`,
      event: label.toLowerCase(),
      pov: 'Juno',
      role: 'recipient',
      other: 'Cass',
      whatYouSaw: `${label} was already half-decided before Cass spoke. Juno had to keep a calm face.`,
      instruction: coreInstruction,
      pawnSummary: 'sex=female; life_stage=adult; mood=cautious; thoughts=calculating',
      persona: secondaryPersona.rule,
      promptEnchantment: '',
      setting: 'outdoors, overcast, temperate forest, hauling medicine',
      tone,
      relationship: 'opinion=guarded; because practical loyalty; last wrote: I would rather finish the job.',
      lastOpener: 'A private note was due later.',
      weapon: group.combat ? 'short spear' : '',
      initiatorEntry: 'I pushed the point because waiting would only make the room colder.',
      version: 'v1',
    }));

    cases.push(buildPairFixture(promptData, group, {
      id: `pair-${group.defName}-recipient-v2`,
      event: label.toLowerCase(),
      pov: 'Juno',
      role: 'recipient',
      other: 'Cass',
      whatYouSaw: `${label} ended with a careful line that both could repeat publicly.`,
      instruction: `${coreInstruction}, but keep the emotional beat small and concrete.`,
      pawnSummary: 'sex=female; life_stage=adult; mood=steady; thoughts=resolved',
      persona: secondaryPersona.rule,
      promptEnchantment: 'high priority; major blood loss in torso; heavy bleeding, severe pain',
      setting: 'outdoors, rain, temperate forest, tending a wound',
      tone: `${tone}; restrained`,
      relationship: 'opinion=formal; because recent tension; last wrote: I let the smallest word do the work.',
      lastOpener: 'I let the smallest word do the work.',
      weapon: group.combat ? 'short spear' : '',
      initiatorEntry: 'I kept my voice low and still made sure Cass understood me.',
      version: 'v2',
    }));
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

    cases.push(buildSoloFixture(promptData, group, {
      id: `solo-${group.defName}-v1`,
      event: label.toLowerCase(),
      pov: i === 0 ? 'Cass' : 'Juno',
      whatHappened: `${label} hit suddenly and left a physical trace in mood and movement.`,
      instruction: `${baseInstruction}; keep it immediate and grounded.`,
      pawnSummary: `sex=${i === 0 ? 'female' : 'male'}; life_stage=adult; mood=alert; thoughts=complicated`,
      persona: persona.rule,
      promptEnchantment: 'high priority; critical consciousness; near collapse, thoughts fragmented, barely awake',
      setting: 'outdoors, clear, arid hills, doing field work',
      tone,
      lastOpener: 'Nothing needed to be said out loud.',
      weapon: '',
      version: 'v1',
    }));

    cases.push(buildSoloFixture(promptData, group, {
      id: `solo-${group.defName}-v2`,
      event: label.toLowerCase(),
      pov: i === 0 ? 'Cass' : 'Juno',
      whatHappened: `${label} unfolded over a long minute, then cooled into routine.`,
      instruction: `${baseInstruction} with a calm aftertaste.`,
      pawnSummary: `sex=${i === 0 ? 'female' : 'male'}; life_stage=adult; mood=measured; thoughts=reflective`,
      persona: persona.rule,
      promptEnchantment: '',
      setting: 'outdoors, windy, desert, returning to camp',
      tone: `${tone}; practical`,
      lastOpener: 'This is the part they will not remember well.',
      weapon: '',
      version: 'v2',
    }));
  }

  // Neutral descriptions (third-person, no persona).
  cases.push(buildStaticTemplateFixture(promptData, {
    id: 'arrival-colonist-v1',
    type: 'arrival',
    templateKey: 'ArrivalDescription',
    event: 'colonist arrival',
    arrivalPawn: 'Rowan',
    neutralText: 'Founders settled on a ruined outpost and began rebuilding.',
    arrivalFacts: 'arrival_pawn=Rowan; scenario=Stormbound Rescue; recruiter=warden Nia',
    pawnSummary: 'age=27; role=farmer; mood=hopeful; sex=female; health=healthy',
    setting: 'Biome=temperate; Weather=rain; Time=night',
    gameContext: 'arrival_description=true; arrival_pawn=Rowan; scenario=Stormbound Rescue; recruiter=warden Nia',
    version: 'v1',
  }));

  cases.push(buildStaticTemplateFixture(promptData, {
    id: 'arrival-colonist-v2',
    type: 'arrival',
    templateKey: 'ArrivalDescription',
    event: 'colonist arrival',
    arrivalPawn: 'Mira',
    neutralText: 'Joined the colony late as an emergency transfer after a caravan event.',
    arrivalFacts: 'arrival_pawn=Mira; scenario=Emergency Evac; recruiter=caravan captain',
    pawnSummary: 'age=25; role=smith; mood=alert; sex=female; health=healthy',
    setting: 'Biome=ice; Weather=blizzard; Time=night',
    gameContext: 'arrival_description=true; arrival_pawn=Mira; scenario=Emergency Evac; recruiter=caravan captain',
    version: 'v2',
  }));

  cases.push(buildStaticTemplateFixture(promptData, {
    id: 'death-colonist-v1',
    type: 'death',
    templateKey: 'DeathDescription',
    event: 'colonist death',
    deathVictim: 'Vale',
    neutralText: 'The colonist was cut down during a raid wave.',
    deathFacts: 'death_victim=Vale; death_victim_role=initiator; cause=knife wound; destroyed_parts=right arm; nearby=outer gate',
    deathPawnSummary: 'age=29; mood=exhausted; health=critical before death; sex=male',
    deathSetting: 'Biome=marsh; Weather=storm; Time=night',
    gameContext: 'death_description=true; death_victim=Vale; death_victim_role=initiator; cause=knife wound; destroyed_parts=right arm; nearby=outer gate',
    version: 'v1',
  }));

  cases.push(buildStaticTemplateFixture(promptData, {
    id: 'death-colonist-v2',
    type: 'death',
    templateKey: 'DeathDescription',
    event: 'colonist death',
    deathVictim: 'Arlo',
    neutralText: 'A sudden infection spread too fast for treatment.',
    deathFacts: 'death_victim=Arlo; death_victim_role=recipient; cause=toxin fever; organs=lungs; nearby=medical bay',
    deathPawnSummary: 'age=34; mood=pained; health=critical; sex=male',
    deathSetting: 'Biome=desert; Weather=clear; Time=dawn',
    gameContext: 'death_description=true; death_victim=Arlo; death_victim_role=recipient; cause=toxin fever; organs=lungs; nearby=medical bay',
    version: 'v2',
  }));

  // Title follow-ups use the same entry payload plus the XML-backed title instruction.
  cases.push(buildStaticTemplateFixture(promptData, {
    id: 'title-followup-v1',
    type: 'title',
    templateKey: 'Title',
    entryText: 'I snapped at him when the work order came in too late. The room went quiet, and then everyone blamed everyone.',
    version: 'v1',
  }));

  cases.push(buildStaticTemplateFixture(promptData, {
    id: 'title-followup-v2',
    type: 'title',
    templateKey: 'Title',
    entryText: 'The storm came while I was still on watch. I kept a lamp lit and made choices nobody asked me to make.',
    version: 'v2',
  }));

  return cases;
}

function buildAllVariantFixtureSet(promptData, config, options, groups, allPersonas) {
  const personaLimit = resolveGeneratedLimit(options.includePersonas, config.generated.includePersonas, allPersonas.length);
  const personas = pickRandom(allPersonas, personaLimit);
  const variants = promptEnchantmentVariantsFor(config);
  const sourceGroups = groups.length > 0
    ? groups
    : [{ defName: 'default', label: 'quiet colonist moment', instruction: 'a tense social moment', tone: 'tense but clear', domain: 'Interaction', combat: false }];
  const cases = [];
  const pairGroups = sourceGroups.filter(isGeneratedPairGroup);
  const soloGroups = sourceGroups.filter(isGeneratedSoloGroup);

  for (let i = 0; i < pairGroups.length; i++) {
    const group = pairGroups[i];
    const primaryPersona = pickPersona(personas, i);
    const secondaryPersona = pickPersona(personas, i + 1);
    const tone = isSkippable(group.tone) ? 'tense but clear' : group.tone;
    const label = isSkippable(group.label) ? `social event ${group.defName}` : group.label;
    const coreInstruction = isSkippable(group.instruction)
      ? 'a charged social exchange between two colonists'
      : group.instruction;

    for (const variant of variants) {
      cases.push(buildPairFixture(promptData, group, {
        id: `pair-${group.defName}-initiator-${variant.key}`,
        event: label.toLowerCase(),
        pov: 'Cass',
        role: 'initiator',
        other: 'Juno',
        whatYouSaw: `Cass started the ${label.toLowerCase()} after a practical disagreement. The fixed lab context is ${variant.label}.`,
        instruction: coreInstruction,
        pawnSummary: pawnSummaryForVariant('female', 'tense', 'restless', variant),
        persona: primaryPersona.rule,
        promptEnchantment: variant.promptEnchantment,
        setting: settingForVariant(variant, 'outdoors, overcast, temperate forest, doing guard duty'),
        tone: toneForVariant(tone, variant),
        relationship: 'opinion=cold; because insulted recently; last wrote: I kept my mouth shut too long.',
        lastOpener: 'The quiet had lasted too long.',
        weapon: group.combat ? 'short spear' : '',
        initiatorEntry: '',
        version: variant.key,
      }));

      cases.push(buildPairFixture(promptData, group, {
        id: `pair-${group.defName}-recipient-${variant.key}`,
        event: label.toLowerCase(),
        pov: 'Juno',
        role: 'recipient',
        other: 'Cass',
        whatYouSaw: `Juno received the ${label.toLowerCase()} with everyone close enough to notice. The fixed lab context is ${variant.label}.`,
        instruction: coreInstruction,
        pawnSummary: pawnSummaryForVariant('female', 'cautious', 'calculating', variant),
        persona: secondaryPersona.rule,
        promptEnchantment: variant.promptEnchantment,
        setting: settingForVariant(variant, 'outdoors, overcast, temperate forest, hauling medicine'),
        tone: toneForVariant(tone, variant),
        relationship: 'opinion=guarded; because practical loyalty; last wrote: I would rather finish the job.',
        lastOpener: 'A private note was due later.',
        weapon: group.combat ? 'short spear' : '',
        initiatorEntry: 'I pushed the point because waiting would only make the room colder.',
        version: variant.key,
      }));
    }
  }

  for (let i = 0; i < soloGroups.length; i++) {
    const group = soloGroups[i];
    const persona = pickPersona(personas, i);
    const tone = isSkippable(group.tone) ? 'nervous but grounded' : group.tone;
    const label = isSkippable(group.label) ? `solo moment ${group.defName}` : group.label;
    const baseInstruction = isSkippable(group.instruction)
      ? 'a private moment that changes the tone of the day'
      : group.instruction;

    for (const variant of variants) {
      cases.push(buildSoloFixture(promptData, group, {
        id: `solo-${group.defName}-${variant.key}`,
        event: label.toLowerCase(),
        pov: i % 2 === 0 ? 'Cass' : 'Juno',
        whatHappened: `${label} landed as a private colony moment. The fixed lab context is ${variant.label}.`,
        instruction: `${baseInstruction}; keep it immediate and grounded.`,
        pawnSummary: pawnSummaryForVariant(i % 2 === 0 ? 'female' : 'male', 'alert', 'complicated', variant),
        persona: persona.rule,
        promptEnchantment: variant.promptEnchantment,
        setting: settingForVariant(variant, 'outdoors, clear, arid hills, doing field work'),
        tone: toneForVariant(tone, variant),
        lastOpener: 'Nothing needed to be said out loud.',
        weapon: '',
        version: variant.key,
      }));
    }
  }

  appendStaticGeneratedFixtures(cases, promptData);
  return cases;
}

function appendStaticGeneratedFixtures(cases, promptData) {
  cases.push(buildStaticTemplateFixture(promptData, {
    id: 'arrival-colonist-v1',
    type: 'arrival',
    templateKey: 'ArrivalDescription',
    event: 'colonist arrival',
    arrivalPawn: 'Rowan',
    neutralText: 'Founders settled on a ruined outpost and began rebuilding.',
    arrivalFacts: 'arrival_pawn=Rowan; scenario=Stormbound Rescue; recruiter=warden Nia',
    pawnSummary: 'age=27; role=farmer; mood=hopeful; sex=female; health=healthy',
    setting: 'Biome=temperate; Weather=rain; Time=night',
    gameContext: 'arrival_description=true; arrival_pawn=Rowan; scenario=Stormbound Rescue; recruiter=warden Nia',
    version: 'v1',
  }));

  cases.push(buildStaticTemplateFixture(promptData, {
    id: 'arrival-colonist-v2',
    type: 'arrival',
    templateKey: 'ArrivalDescription',
    event: 'colonist arrival',
    arrivalPawn: 'Mira',
    neutralText: 'Joined the colony late as an emergency transfer after a caravan event.',
    arrivalFacts: 'arrival_pawn=Mira; scenario=Emergency Evac; recruiter=caravan captain',
    pawnSummary: 'age=25; role=smith; mood=alert; sex=female; health=healthy',
    setting: 'Biome=ice; Weather=blizzard; Time=night',
    gameContext: 'arrival_description=true; arrival_pawn=Mira; scenario=Emergency Evac; recruiter=caravan captain',
    version: 'v2',
  }));

  cases.push(buildStaticTemplateFixture(promptData, {
    id: 'death-colonist-v1',
    type: 'death',
    templateKey: 'DeathDescription',
    event: 'colonist death',
    deathVictim: 'Vale',
    neutralText: 'The colonist was cut down during a raid wave.',
    deathFacts: 'death_victim=Vale; death_victim_role=initiator; cause=knife wound; destroyed_parts=right arm; nearby=outer gate',
    deathPawnSummary: 'age=29; mood=exhausted; health=critical before death; sex=male',
    deathSetting: 'Biome=marsh; Weather=storm; Time=night',
    gameContext: 'death_description=true; death_victim=Vale; death_victim_role=initiator; cause=knife wound; destroyed_parts=right arm; nearby=outer gate',
    version: 'v1',
  }));

  cases.push(buildStaticTemplateFixture(promptData, {
    id: 'death-colonist-v2',
    type: 'death',
    templateKey: 'DeathDescription',
    event: 'colonist death',
    deathVictim: 'Arlo',
    neutralText: 'A sudden infection spread too fast for treatment.',
    deathFacts: 'death_victim=Arlo; death_victim_role=recipient; cause=toxin fever; organs=lungs; nearby=medical bay',
    deathPawnSummary: 'age=34; mood=pained; health=critical; sex=male',
    deathSetting: 'Biome=desert; Weather=clear; Time=dawn',
    gameContext: 'death_description=true; death_victim=Arlo; death_victim_role=recipient; cause=toxin fever; organs=lungs; nearby=medical bay',
    version: 'v2',
  }));

  cases.push(buildStaticTemplateFixture(promptData, {
    id: 'title-followup-v1',
    type: 'title',
    templateKey: 'Title',
    entryText: 'I snapped at him when the work order came in too late. The room went quiet, and then everyone blamed everyone.',
    version: 'v1',
  }));

  cases.push(buildStaticTemplateFixture(promptData, {
    id: 'title-followup-v2',
    type: 'title',
    templateKey: 'Title',
    entryText: 'The storm came while I was still on watch. I kept a lamp lit and made choices nobody asked me to make.',
    version: 'v2',
  }));
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

function isAllLimit(value) {
  return String(value || '').trim().toLowerCase() === 'all';
}

function resolveGeneratedLimit(optionValue, fallbackValue, totalCount) {
  if (totalCount <= 0) {
    return 0;
  }
  if (isAllLimit(optionValue) || isAllLimit(fallbackValue)) {
    return totalCount;
  }

  const candidate = Number.isFinite(optionValue)
    ? optionValue
    : (Number.isFinite(fallbackValue) ? fallbackValue : totalCount);
  return Math.max(1, Math.min(totalCount, Math.floor(candidate)));
}

function sortAndTrimGroups(groups, excludedGroupNames = new Set()) {
  return [...groups]
    .filter((group) => group && group.defName)
    .filter((group) => !excludedGroupNames.has(String(group.defName).toLowerCase()))
    .sort((a, b) => {
      const orderDiff = (a.order || 9999) - (b.order || 9999);
      if (orderDiff !== 0) {
        return orderDiff;
      }
      if (a.domain !== b.domain) {
        return String(a.domain).localeCompare(String(b.domain));
      }
      return String(a.defName || '').localeCompare(String(b.defName || ''));
    });
}

function pickRandom(entries, limit) {
  return [...entries].slice(0, Math.max(1, limit));
}

function promptEnchantmentVariantsFor(config) {
  const configured = config
    && config.generated
    && Array.isArray(config.generated.promptEnchantmentVariants)
    ? config.generated.promptEnchantmentVariants
    : DEFAULT_PROMPT_ENCHANTMENT_VARIANTS;
  const variants = configured
    .map((entry, index) => normalizePromptEnchantmentVariant(entry, index))
    .filter(Boolean);

  if (!variants.some((variant) => variant.promptEnchantment === '')) {
    variants.unshift(normalizePromptEnchantmentVariant(DEFAULT_PROMPT_ENCHANTMENT_VARIANTS[0], 0));
  }
  return variants.length > 0
    ? variants
    : [normalizePromptEnchantmentVariant(DEFAULT_PROMPT_ENCHANTMENT_VARIANTS[0], 0)];
}

function normalizePromptEnchantmentVariant(entry, index) {
  if (typeof entry === 'string') {
    return {
      key: safePathName(entry || `variant-${index + 1}`),
      label: entry || `variant ${index + 1}`,
      promptEnchantment: cleanValue(entry),
      pawnSummaryCue: '',
      setting: '',
      toneSuffix: '',
    };
  }

  if (!entry || typeof entry !== 'object') {
    return null;
  }

  const promptEnchantment = cleanValue(entry.promptEnchantment ?? entry.text ?? entry.value ?? '');
  const rawKey = entry.key || entry.id || entry.label || promptEnchantment || `variant-${index + 1}`;
  return {
    key: safePathName(rawKey),
    label: cleanValue(entry.label || rawKey || `variant ${index + 1}`),
    promptEnchantment,
    pawnSummaryCue: cleanValue(entry.pawnSummaryCue || ''),
    setting: cleanValue(entry.setting || ''),
    toneSuffix: cleanValue(entry.toneSuffix || ''),
  };
}

function pawnSummaryForVariant(sex, mood, thoughts, variant) {
  const parts = [
    `sex=${sex}`,
    'life_stage=adult',
    `mood=${mood}`,
    `thoughts=${thoughts}`,
  ];
  if (variant && !isSkippable(variant.pawnSummaryCue)) {
    parts.push(variant.pawnSummaryCue);
  }
  return parts.join('; ');
}

function settingForVariant(variant, fallback) {
  return variant && !isSkippable(variant.setting) ? variant.setting : fallback;
}

function toneForVariant(tone, variant) {
  if (!variant || isSkippable(variant.toneSuffix)) {
    return tone;
  }
  if (isSkippable(tone)) {
    return variant.toneSuffix;
  }
  return `${tone}; ${variant.toneSuffix}`;
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

function expandGenerationPasses(fixtures, passes) {
  const count = Number.isFinite(passes) ? Math.max(1, Math.floor(passes)) : 1;
  if (count <= 1) {
    return fixtures;
  }

  const width = String(count).length;
  const expanded = [];
  for (let pass = 1; pass <= count; pass++) {
    const passLabel = `pass-${String(pass).padStart(width, '0')}`;
    for (const fixture of fixtures) {
      expanded.push({
        ...fixture,
        id: `${passLabel}-${fixtureId(fixture, expanded.length)}`,
        pass,
      });
    }
  }
  return expanded;
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

function buildCompactMarkdownOutput(runConfig) {
  const chunks = [];
  chunks.push('# Prompt-lab compact results');
  chunks.push(`- Timestamp: ${runConfig.timestamp}`);
  chunks.push(`- Endpoint: ${runConfig.endpoint}`);
  chunks.push(`- Model: ${runConfig.model}`);
  chunks.push(`- Temperature: ${runConfig.temperature}`);
  chunks.push(`- Max tokens: ${runConfig.maxTokens}`);
  chunks.push(`- Cases: ${runConfig.entries.length}`);
  chunks.push('');

  for (const entry of runConfig.entries) {
    chunks.push(`## ${entry.id}`);
    chunks.push(`- status: ${entry.status}`);
    if (entry.error) {
      chunks.push(`- error: ${entry.error}`);
    }
    if (entry.title) {
      chunks.push(`- title: ${entry.title.trim()}`);
    }
    chunks.push('');
    chunks.push('### Prompt');
    chunks.push('```text');
    chunks.push(compactPromptText(entry));
    chunks.push('```');
    chunks.push('');
    chunks.push('### Parsed result');
    chunks.push('```text');
    chunks.push((entry.generated || '').trim() || '(empty)');
    chunks.push('```');
    chunks.push('');
  }

  return chunks.join('\n');
}

function compactPromptText(entry) {
  const parts = [];
  if (!isSkippable(entry.systemText)) {
    parts.push(`system:\n${entry.systemText.trim()}`);
  }
  parts.push(`user:\n${(entry.prompt || '').trim() || '(empty)'}`);
  return parts.join('\n\n');
}

function isSuccessfulResponse(result) {
  const status = Number.parseInt(result.status, 10);
  return Number.isFinite(status) && status >= 200 && status < 300;
}

function shouldRunTitle(fixture, options, mainResult) {
  if (options.skipTitle) return false;
  if (!mainResult || mainResult.error) return false;
  if (!mainResult.generated || !mainResult.generated.trim()) return false;
  if (isTitleFixture(fixture)) return false;
  if (fixture.skipTitle === true) return false;
  return true;
}

function isTitleFixture(fixture) {
  return String((fixture && fixture.systemMode) || '').toLowerCase() === 'title'
    || String((fixture && fixture.templateKey) || '').toLowerCase() === 'title'
    || String((fixture && fixture.type) || '').toLowerCase() === 'title';
}

async function runTitleFollowUp(fixture, mainText, requestConfig, config, promptData, options, runState) {
  const titleTemplate = resolvedTemplate(promptData, 'Title');
  const renderedTitle = fieldsFromTemplate(titleTemplate, { entryText: mainText });
  const titleFixture = {
    ...fixture,
    templateKey: 'Title',
    promptText: undefined,
    promptLines: undefined,
    promptFields: renderedTitle.fields,
    promptFieldOrder: renderedTitle.order,
    systemMode: undefined,
    system: null,
    systemText: null,
    append: finalInstructionForTemplate(promptData, 'Title'),
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
      if (isTitleFixture(fixture)) {
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
  if (args.allVariants) config.generated.allVariants = true;
  if (Number.isFinite(args.temperature)) config.temperature = args.temperature;
  if (Number.isFinite(args.maxTokens)) config.maxTokens = args.maxTokens;
  if (Number.isFinite(args.timeoutSeconds)) config.timeoutSeconds = args.timeoutSeconds;
  if (typeof args.saveResults === 'boolean') config.saveResults = args.saveResults;
  if (typeof args.compactResults === 'boolean') {
    config.compactResults = args.compactResults;
  } else if (args.allVariants) {
    config.compactResults = true;
  }

  const promptData = loadPromptData(config);
  let fixtures = [];

  if (args.fromXml) {
    fixtures = buildGeneratedFixtureSet(promptData, config, config.generated);
    if (args.caseFilter) {
      fixtures = fixtures.filter((entry) => shouldRunFixture(entry, args.caseFilter));
    }
  } else {
    const loaded = loadManualFixtures(args.fixtureDir);
    fixtures = args.caseFilter
      ? loaded.filter((entry) => shouldRunFixture(entry, args.caseFilter))
      : loaded;
  }

  fixtures = expandGenerationPasses(fixtures, args.passes);

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
  const folder = path.join(resultRootFolder(config), modelFolder);
  fs.mkdirSync(folder, { recursive: true });
  const outPath = path.join(folder, `${timestamp}.md`);
  const markdownConfig = {
    timestamp,
    endpoint: toChatUrl(config.endpoint || DEFAULTS.endpoint),
    model: args.model || config.model || DEFAULTS.model,
    temperature: args.temperature ?? config.temperature,
    maxTokens: args.maxTokens ?? config.maxTokens,
    entries: runEntries,
  };
  const md = config.compactResults
    ? buildCompactMarkdownOutput(markdownConfig)
    : buildMarkdownOutput(markdownConfig);
  fs.writeFileSync(outPath, md, 'utf8');
  console.log(`Saved markdown results to ${outPath}`);
}

main().catch((error) => {
  console.error('prompt-lab failed:', error.message || error);
  process.exitCode = 1;
});
