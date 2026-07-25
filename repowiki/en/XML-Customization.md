# XML customization

Pawn Diary keeps event and prompt policy in XML so a separate RimWorld patch mod can change visible behavior without replacing the assembly. This page assumes that your patch mod already loads correctly; it only covers which Pawn Diary policy to patch and one current example.

## Which XML owns what

| XML file | Visible behavior it owns |
|---|---|
| [DiaryInteractionGroupDefs.xml](../../1.6/Defs/DiaryInteractionGroupDefs.xml) | event matching, labels, default toggles, importance, combat, tone, instruction, and batching |
| [DiaryEventPromptDefs.xml](../../1.6/Defs/DiaryEventPromptDefs.xml) | event/classifier-specific prompt guidance and model preference |
| [DiaryPromptTemplateDefs.xml](../../1.6/Defs/DiaryPromptTemplateDefs.xml) | template shapes and ordered prompt fields |
| [DiaryPromptEnchantmentDefs.xml](../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) | optional live-context prompt cues |
| [DiaryEventWindowDefs.xml](../../1.6/Defs/DiaryEventWindowDefs.xml) | bounded episodes and their page/context behavior |
| [DiaryObservedConditionDefs.xml](../../1.6/Defs/DiaryObservedConditionDefs.xml) | lasting observed states and prompt-only/page behavior |
| [DiarySignalPolicyDefs.xml](../../1.6/Defs/DiarySignalPolicyDefs.xml) | scan, admission, and suppression policy |
| [DiaryTuningDef.xml](../../1.6/Defs/DiaryTuningDef.xml) and [DiaryContextDetailDef.xml](../../1.6/Defs/DiaryContextDetailDef.xml) | pacing, budgets, caps, retention, and context selection |
| Persona, psychotype, humor, memory/continuity, culture, text-decoration, and UI-style Defs in [1.6/Defs](../../1.6/Defs/) | voice, continuity, and presentation |

The exhaustive reference pages show loaded instances without reproducing their schemas: [events](reference/Event-Catalog.md), [conditions and windows](reference/Observed-Conditions-and-Windows.md), and [prompt policy](reference/Prompt-Reference.md).

## Which value wins

For a setting or prompt choice that supports every layer, precedence is:

1. A saved player override.
2. The matching XML event or prompt policy.
3. The XML template or interaction-group fallback.
4. A defensive code fallback, used only when configuration is missing or invalid.

Not every field has a player-facing override, but an override that does exist is intentionally stronger than an XML default. This matters when testing a patch against an existing save.

Interaction groups are ordered. For a classified signal, the first eligible group whose exact name, prefix, suffix, segment, token, package, or catch-all policy matches owns the classification. Add or reorder matchers carefully; a broad earlier group can shadow a specific later one.

## Example: enable important quest-acceptance pages by default

The exact current group identifier is `questAccepted`. Pawn Diary ships it with `defaultEnabled` set to `false` and `important` set to `true`. The following operation makes the intended policy explicit in a separate patch mod:

```xml
<Patch>
  <Operation Class="PatchOperationSequence">
    <operations>
      <li Class="PatchOperationReplace">
        <xpath>Defs/PawnDiary.DiaryInteractionGroupDef[defName="questAccepted"]/defaultEnabled</xpath>
        <value>
          <defaultEnabled>true</defaultEnabled>
        </value>
      </li>
      <li Class="PatchOperationReplace">
        <xpath>Defs/PawnDiary.DiaryInteractionGroupDef[defName="questAccepted"]/important</xpath>
        <value>
          <important>true</important>
        </value>
      </li>
    </operations>
  </Operation>
</Patch>
```

For a player who has never overridden this Events row, quest acceptance now starts enabled and admitted pages use important-event treatment. The second operation preserves the shipped important classification while making the patch's intent clear.

An existing save may already contain an explicit enabled/disabled choice for `questAccepted`. That saved player override still wins, so reset or change the row in the Events tab when verifying the new default. The patch does not change quest completion/failure policy and does not guarantee a page when pawn eligibility, duplicate, chance, pacing, or API checks reject the event.

## Safe customization boundaries

Keep prompt text, matching policy, thresholds, odds, colors, tags, and similar tuning in XML. When matching optional DLC or mod content, prefer plain string identifiers and package gates; do not introduce an unguarded reference to a Def that may be absent. A patch that references DLC-owned Defs directly needs the appropriate `MayRequire` guard.

After changing a group, use prompt-test mode and the matching reference detail section to verify the selected group, instruction, tone, template, and expected outcome before spending tokens.
