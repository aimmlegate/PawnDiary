#!/usr/bin/env node
'use strict';

/**
 * Drift guard between the C# PromptAssembler and the JS assembler.js mirror.
 *
 * Renders golden/cases.json through assembler.js and asserts the output matches golden/expected.json
 * (which the C# dump tool produced from the SAME cases through the real PromptAssembler). If the two
 * implementations diverge, this fails and prints the offending field.
 *
 *   npm run golden        regenerate expected.json from C# (after changing PromptAssembler)
 *   npm run check-golden  verify assembler.js still matches it
 */

const fs = require('fs');
const path = require('path');
const assembler = require('./assembler');

function main() {
  const root = __dirname;
  const casesPath = path.join(root, 'golden', 'cases.json');
  const expectedPath = path.join(root, 'golden', 'expected.json');

  if (!fs.existsSync(casesPath)) {
    console.error(`Missing cases file: ${casesPath}`);
    process.exitCode = 1;
    return;
  }
  if (!fs.existsSync(expectedPath)) {
    console.error(`Missing golden file: ${expectedPath}\nGenerate it with: npm run golden`);
    process.exitCode = 1;
    return;
  }

  const cases = JSON.parse(fs.readFileSync(casesPath, 'utf8'));
  const expected = JSON.parse(fs.readFileSync(expectedPath, 'utf8'));
  const expectedById = new Map(expected.map((entry) => [entry.id, entry]));

  let mismatches = 0;

  for (const testCase of cases) {
    const got = assembler.render(testCase);
    const exp = expectedById.get(testCase.id);
    if (!exp) {
      console.error(`No C# golden entry for case '${testCase.id}' — regenerate with: npm run golden`);
      mismatches++;
      continue;
    }
    for (const key of ['systemPrompt', 'userPrompt']) {
      if (got[key] !== exp[key]) {
        mismatches++;
        console.error(`MISMATCH [${testCase.id}] ${key}`);
        console.error(`  C#: ${JSON.stringify(exp[key])}`);
        console.error(`  JS: ${JSON.stringify(got[key])}`);
      }
    }
  }

  if (cases.length !== expected.length) {
    console.error(`Case count differs: cases.json=${cases.length}, expected.json=${expected.length} — regenerate with: npm run golden`);
    mismatches++;
  }

  if (mismatches === 0) {
    console.log(`check-golden: OK — assembler.js matches the C# golden for all ${cases.length} cases.`);
  } else {
    console.error(`check-golden: FAILED with ${mismatches} mismatch(es). If the C# PromptAssembler changed on purpose, run "npm run golden" to refresh the golden, then re-check; otherwise fix assembler.js to match.`);
    process.exitCode = 1;
  }
}

main();
