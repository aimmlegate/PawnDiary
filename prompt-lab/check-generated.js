#!/usr/bin/env node
'use strict';

/**
 * Coverage guard for the XML-driven fixture generator in run.js.
 *
 * The golden test checks the shared JS assembler against the C# assembler. This check verifies the
 * generated prompt-lab matrix itself: every current XML prompt template and event-source prompt must
 * appear in an all-variants fixture set, and the repeated-pass path must preserve the same cases.
 */

const lab = require('./run');

function isBlank(value) {
  return value == null || String(value).trim() === '';
}

function lower(value) {
  return String(value || '').toLowerCase();
}

function unique(values) {
  return [...new Set(values.filter((value) => !isBlank(value)))].sort((a, b) => String(a).localeCompare(String(b)));
}

function containsAny(rendered, needle) {
  if (isBlank(needle)) {
    return true;
  }
  return rendered.some((entry) => entry.user.includes(needle));
}

function main() {
  const config = lab.loadConfig();
  config.generated = {
    ...config.generated,
    allVariants: true,
    includeGroups: 'all',
    includePersonas: 'all',
  };

  const promptData = lab.loadPromptData(config);
  const fixtures = lab.buildGeneratedFixtureSet(promptData, config, config.generated);
  const twoPassFixtures = lab.expandGenerationPasses(fixtures, 2);
  const rendered = fixtures.map((fixture) => ({
    fixture,
    system: lab.getSystemForFixture(fixture, config, promptData),
    user: lab.buildPromptText(fixture),
  }));

  const failures = [];

  if (fixtures.length === 0) {
    failures.push('Generated all-variants fixture set is empty.');
  }

  const emptyUsers = rendered.filter((entry) => isBlank(entry.user)).map((entry) => entry.fixture.id);
  if (emptyUsers.length > 0) {
    failures.push(`Generated fixtures with empty user prompts: ${emptyUsers.join(', ')}`);
  }

  const emptySystems = rendered.filter((entry) => isBlank(entry.system)).map((entry) => entry.fixture.id);
  if (emptySystems.length > 0) {
    failures.push(`Generated fixtures with empty system prompts: ${emptySystems.join(', ')}`);
  }

  const expectedTemplates = unique((promptData.promptTemplates || []).map((template) => template.templateKey));
  const coveredTemplates = new Set(fixtures.map((fixture) => fixture.templateKey).filter(Boolean));
  const missingTemplates = expectedTemplates.filter((key) => !coveredTemplates.has(key));
  if (missingTemplates.length > 0) {
    failures.push(`Generated fixtures do not cover template key(s): ${missingTemplates.join(', ')}`);
  }

  const eventPromptFailures = [];
  for (const eventPrompt of promptData.eventPrompts || []) {
    const key = eventPrompt.eventType || eventPrompt.defName;
    if (!containsAny(rendered, eventPrompt.prompt)) {
      eventPromptFailures.push(`${key}.prompt`);
    }
    if (!containsAny(rendered, eventPrompt.enhancement)) {
      eventPromptFailures.push(`${key}.enhancement`);
    }
  }
  if (eventPromptFailures.length > 0) {
    failures.push(`Generated fixtures do not render event prompt text: ${eventPromptFailures.join(', ')}`);
  }

  const instructionVariantFailures = [];
  for (const group of promptData.groups || []) {
    for (const instruction of group.instructions || []) {
      if (!containsAny(rendered, instruction)) {
        instructionVariantFailures.push(`${group.defName}.instructions`);
        break;
      }
    }
  }
  if (instructionVariantFailures.length > 0) {
    failures.push(`Generated fixtures do not render instruction variant pool(s): ${instructionVariantFailures.join(', ')}`);
  }

  const toneVariantFailures = [];
  for (const group of promptData.groups || []) {
    for (const tone of group.tones || []) {
      if (!containsAny(rendered, tone)) {
        toneVariantFailures.push(`${group.defName}.tones`);
        break;
      }
    }
  }
  if (toneVariantFailures.length > 0) {
    failures.push(`Generated fixtures do not render tone variant pool(s): ${toneVariantFailures.join(', ')}`);
  }

  const expectedMarkers = [
    'tale=',
    'mood_event=',
    'thought=',
    'inspiration=',
    'romance=',
    'work=',
    'hediff=',
    'mental_state=',
    'raid=',
    'quest=',
    'day_reflection=',
    'arrival_description=',
    'death_description=',
  ];
  const contextText = lower(fixtures.map((fixture) => fixture.gameContext || '').join('\n'));
  const missingMarkers = expectedMarkers.filter((marker) => !contextText.includes(marker));
  if (missingMarkers.length > 0) {
    failures.push(`Generated fixtures do not cover context marker(s): ${missingMarkers.join(', ')}`);
  }

  const variants = lab.promptEnchantmentVariantsFor(config);
  const firstPersonFixtures = fixtures.filter((fixture) => fixture.type === 'pair' || fixture.type === 'solo');
  const missingVariants = variants
    .map((variant) => variant.key)
    .filter((key) => !firstPersonFixtures.some((fixture) => fixture.version === key));
  if (missingVariants.length > 0) {
    failures.push(`Generated first-person fixtures do not cover prompt-enchantment variant(s): ${missingVariants.join(', ')}`);
  }

  if (twoPassFixtures.length !== fixtures.length * 2) {
    failures.push(`Two-pass expansion count mismatch: base=${fixtures.length}, expanded=${twoPassFixtures.length}`);
  }
  if (!twoPassFixtures.every((fixture) => /^pass-[12]-/.test(fixture.id))) {
    failures.push('Two-pass expansion did not prefix every fixture id with pass-1/pass-2.');
  }

  if (failures.length > 0) {
    for (const failure of failures) {
      console.error(`check-generated: ${failure}`);
    }
    process.exitCode = 1;
    return;
  }

  console.log(
    'check-generated: OK -- '
    + `${fixtures.length} all-variant case(s), `
    + `${twoPassFixtures.length} two-pass case(s), `
    + `${expectedTemplates.length} template(s), `
    + `${(promptData.eventPrompts || []).length} event prompt type(s), `
    + `${variants.length} prompt-enchantment variant(s).`,
  );
}

main();
