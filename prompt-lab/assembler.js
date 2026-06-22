'use strict';

/**
 * JS mirror of the C# PromptAssembler (Source/Generation/PromptAssembler.cs).
 *
 * This is the pure prompt-assembly algorithm: field order, the skip rules for empty/placeholder
 * values, the "label: value" join, the instruction trailer, source-token -> value mapping, and the
 * persona/system composition. It must stay byte-for-byte identical to the C# version; the golden
 * check (npm run check-golden) renders prompt-lab/golden/cases.json through both and asserts equality.
 *
 * Inputs use the same shape and field names as the C# types (PromptAssemblerInput / PromptValues),
 * e.g. `eventNoun`/`povName` rather than `event`/`pov` (`event` is a C# keyword).
 */

const PLACEHOLDERS = new Set(['none', 'n/a', 'unknown']);

function isBlank(value) {
  return value == null || String(value).trim() === '';
}

function eq(a, b) {
  return String(a || '').toLowerCase() === String(b || '').toLowerCase();
}

// Mirror of DiaryContextFields.Value: exact key lookup in a "k=v; k=v" string.
function contextValue(context, key) {
  if (isBlank(context) || isBlank(key)) {
    return '';
  }
  const expected = String(key).trim();
  const parts = String(context).split(';').filter((p) => p.length > 0);
  for (const rawPart of parts) {
    const part = rawPart.trim();
    const eqIndex = part.indexOf('=');
    if (eqIndex <= 0) {
      continue;
    }
    const partKey = part.substring(0, eqIndex).trim();
    if (partKey.toLowerCase() === expected.toLowerCase()) {
      return part.substring(eqIndex + 1).trim();
    }
  }
  return '';
}

// Mirror of PromptAssembler.ResolveSource.
function resolveSource(field, v) {
  if (!field || !v) {
    return '';
  }
  const source = field.source || '';
  if (eq(source, 'EventNoun')) return v.eventNoun;
  if (eq(source, 'PovName')) return v.povName;
  if (eq(source, 'PovRole')) return v.povRole;
  if (eq(source, 'OtherPawnName')) return v.otherName;
  if (eq(source, 'PovText') || eq(source, 'WhatHappened') || eq(source, 'WhatYouSaw')) return v.povText;
  if (eq(source, 'NeutralText')) return v.neutralText;
  if (eq(source, 'Instruction')) return v.instruction;
  if (eq(source, 'PawnSummary')) return v.pawnSummary;
  if (eq(source, 'Persona')) return v.persona;
  if (eq(source, 'EventPrompt')) return v.eventPrompt;
  if (eq(source, 'EventEnhancement')) return v.eventEnhancement;
  if (eq(source, 'PromptEnchantment')) return v.includePromptEnchantment === false ? '' : v.promptEnchantment;
  if (eq(source, 'Setting')) return v.setting;
  if (eq(source, 'Tone')) return v.tone;
  if (eq(source, 'Relationship')) return v.relationship;
  if (eq(source, 'LastOpener')) return v.lastOpener;
  if (eq(source, 'Weapon')) return v.weapon;
  if (eq(source, 'HiddenInitiatorEntry')) return v.initiatorEntry;
  if (eq(source, 'DeathVictim')) return v.deathVictim;
  if (eq(source, 'DeathFacts')) return v.deathFacts;
  if (eq(source, 'DeathPawnSummary')) return v.deathPawnSummary;
  if (eq(source, 'DeathSetting')) return v.deathSetting;
  if (eq(source, 'ArrivalPawn')) return v.arrivalPawn;
  if (eq(source, 'ArrivalFacts')) return v.arrivalFacts;
  if (eq(source, 'EntryText')) return v.entryText;
  if (eq(source, 'GameContext')) return contextValue(v.gameContext, field.contextKey);
  return '';
}

// Mirror of PromptAssembler.AppendField: keep only signal-bearing values.
function appendField(lines, label, value) {
  if (isBlank(value)) {
    return;
  }
  const trimmed = String(value).trim();
  if (PLACEHOLDERS.has(trimmed)) {
    return;
  }
  lines.push(`${label}: ${trimmed}`);
}

// Mirror of PromptAssembler.RenderUserPrompt.
function renderUserPrompt(fields, values, finalInstruction) {
  const lines = [];
  if (Array.isArray(fields)) {
    for (const field of fields) {
      if (!field || field.enabled === false) {
        continue;
      }
      const label = isBlank(field.label) ? field.source : field.label;
      appendField(lines, label, resolveSource(field, values || {}));
    }
  }

  const body = lines.join('\n');
  if (isBlank(finalInstruction)) {
    return body;
  }
  if (isBlank(body)) {
    return finalInstruction;
  }
  return `${body}\n\n${finalInstruction}`;
}

// Mirror of PromptAssembler.ComposeSystem.
function composeSystem(baseSystemPrompt, personaVoiceBlock, includePersona) {
  const baseText = baseSystemPrompt == null ? '' : String(baseSystemPrompt);
  if (includePersona === false || isBlank(personaVoiceBlock)) {
    return baseText;
  }
  if (isBlank(baseText)) {
    return personaVoiceBlock;
  }
  return `${baseText.replace(/\s+$/, '')}\n\n${personaVoiceBlock}`;
}

// Mirror of PromptAssembler.Render.
function render(input) {
  if (!input) {
    return { systemPrompt: '', userPrompt: '' };
  }
  const includePersona = input.includePersona !== false;
  return {
    systemPrompt: composeSystem(input.baseSystemPrompt, input.personaVoiceBlock, includePersona),
    userPrompt: renderUserPrompt(input.fields, input.values, input.finalInstruction),
  };
}

module.exports = {
  render,
  renderUserPrompt,
  composeSystem,
  resolveSource,
  contextValue,
};
