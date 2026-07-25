# Prompt reference

This is the exhaustive prompt-test companion: **15 template shapes**, **70 event-prompt policies**, and **52 live-context prompt enchantments**. The template field tables preserve XML order. “Required when present” is the current context-budget classification; every field is still omitted when disabled, blank, or equal to `none`, `n/a`, or `unknown`.

## Template index

| Template key | Def | Page shape / selection | Fields | Persona/style | XML token cap |
|---|---|---|---:|---:|---:|
| `PairDefault` | `DiaryPromptTemplate_PairDefault` | Ordinary admitted two-pawn event. | 41 | yes | 0 |
| `PairImportant` | `DiaryPromptTemplate_PairImportant` | Important two-pawn event; also used by rich paired DLC routes. | 88 | yes | 200 |
| `PairCombat` | `DiaryPromptTemplate_PairCombat` | Two-pawn event classified as combat. | 34 | yes | 200 |
| `PairBatched` | `DiaryPromptTemplate_PairBatched` | Flushed pair interaction batch. | 31 | yes | 0 |
| `SoloDefault` | `DiaryPromptTemplate_SoloDefault` | Ordinary admitted one-POV event. | 57 | yes | 0 |
| `SoloImportant` | `DiaryPromptTemplate_SoloImportant` | Important solo, fan-out, or source-owned page. | 138 | yes | 200 |
| `SoloInternalState` | `DiaryPromptTemplate_SoloInternalState` | Thought, mood, mental, inspiration, or similar internal-state page. | 42 | yes | 0 |
| `SoloBatched` | `DiaryPromptTemplate_SoloBatched` | Flushed solo Tale or ambient batch. | 42 | yes | 0 |
| `SoloDayReflection` | `DiaryPromptTemplate_SoloDayReflection` | End-of-day reflection. | 40 | yes | 0 |
| `SoloQuadrumReflection` | `DiaryPromptTemplate_SoloQuadrumReflection` | Quadrum reflection boundary. | 16 | yes | 350 |
| `SoloArcReflection` | `DiaryPromptTemplate_SoloArcReflection` | Year/arc reflection boundary. | 19 | yes | 420 |
| `SoloBeliefReflection` | `DiaryPromptTemplate_SoloBeliefReflection` | Ideology belief-reflection boundary. | 15 | yes | 360 |
| `DeathDescription` | `DiaryPromptTemplate_DeathDescription` | Neutral death-description request. | 15 | no | 0 |
| `ArrivalDescription` | `DiaryPromptTemplate_ArrivalDescription` | Neutral arrival-description request. | 8 | no | 0 |
| `Title` | `DiaryPromptTemplate_Title` | Separate bounded title request after a main page succeeds. | 1 | no | 0 |

<!-- repowiki:template {"defName":"DiaryPromptTemplate_PairDefault","templateKey":"PairDefault","includePersona":true,"includePromptEnchantment":true,"appendDirectSpeechInstruction":true,"maxTokens":0,"systemPromptSource":"fallback","finalInstructionSource":"fallback","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"pov","source":"PovName","contextKey":""},{"enabled":true,"label":"role","source":"PovRole","contextKey":""},{"enabled":true,"label":"with","source":"OtherPawnName","contextKey":""},{"enabled":true,"label":"what you saw","source":"PovText","contextKey":""},{"enabled":true,"label":"instruction","source":"Instruction","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"external prompt instruction","source":"GameContext","contextKey":"external_prompt_instruction"},{"enabled":true,"label":"external prompt fragment","source":"GameContext","contextKey":"external_prompt_fragment"},{"enabled":true,"label":"ritual role","source":"GameContext","contextKey":"ritual_role"},{"enabled":true,"label":"ritual title","source":"GameContext","contextKey":"ritual_title"},{"enabled":true,"label":"ability","source":"GameContext","contextKey":"ability_label"},{"enabled":true,"label":"ability category","source":"GameContext","contextKey":"ability_category"},{"enabled":true,"label":"ability target","source":"GameContext","contextKey":"ability_target"},{"enabled":false,"label":"ability recharge ticks (long recharge = rarer, weightier use)","source":"GameContext","contextKey":"ability_cooldown_ticks"},{"enabled":true,"label":"raid arrival mode","source":"GameContext","contextKey":"arrival_mode"},{"enabled":true,"label":"raid strategy","source":"GameContext","contextKey":"strategy"},{"enabled":false,"label":"raid points","source":"GameContext","contextKey":"points"},{"enabled":true,"label":"royal title","source":"GameContext","contextKey":"royal_title"},{"enabled":true,"label":"ideoligion role","source":"GameContext","contextKey":"ideological_role"},{"enabled":true,"label":"important context","source":"PromptEnchantment","contextKey":""},{"enabled":true,"label":"setting","source":"Setting","contextKey":""},{"enabled":true,"label":"tone","source":"Tone","contextKey":""},{"enabled":true,"label":"relationship","source":"Relationship","contextKey":""},{"enabled":true,"label":"my last opening line (do not reuse)","source":"LastOpener","contextKey":""},{"enabled":true,"label":"how my previous entry ended (continuity; do not retell it)","source":"PreviousEntryEnding","contextKey":""},{"enabled":true,"label":"narrative context","source":"NarrativeContext","contextKey":""},{"enabled":true,"label":"relevant past","source":"MemoryContext","contextKey":""},{"enabled":true,"label":"belief context","source":"BeliefContext","contextKey":""},{"enabled":true,"label":"pollution band","source":"GameContext","contextKey":"pollution_band"},{"enabled":true,"label":"pollution transition","source":"GameContext","contextKey":"pollution_transition"},{"enabled":true,"label":"map","source":"GameContext","contextKey":"map_label"},{"enabled":true,"label":"context facet","source":"GameContext","contextKey":"facet"},{"enabled":true,"label":"psychic bond","source":"GameContext","contextKey":"psychic_bond"},{"enabled":true,"label":"first bonded pawn","source":"GameContext","contextKey":"bond_first_pawn_name"},{"enabled":true,"label":"second bonded pawn","source":"GameContext","contextKey":"bond_second_pawn_name"},{"enabled":true,"label":"verified rupture cause","source":"GameContext","contextKey":"cause"},{"enabled":true,"label":"deathrest","source":"GameContext","contextKey":"deathrest"},{"enabled":true,"label":"deathrest severity","source":"GameContext","contextKey":"completion_band"},{"enabled":true,"label":"key relationships","source":"Identity","contextKey":""}]} -->
## Template: `PairDefault`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_PairDefault` |
| Selection | Ordinary admitted two-pawn event. |
| Persona/style block | yes |
| Live prompt enchantment allowed | yes |
| Direct-speech instruction appended | yes |
| System prompt source | `fallback` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `fallback`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | pov | `PovName` | — | yes | required when present |
| 3 | role | `PovRole` | — | yes | required when present |
| 4 | with | `OtherPawnName` | — | yes | required when present |
| 5 | what you saw | `PovText` | — | yes | required when present |
| 6 | instruction | `Instruction` | — | yes | required when present |
| 7 | event prompt | `EventPrompt` | — | yes | required when present |
| 8 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 9 | external prompt instruction | `GameContext` | `external_prompt_instruction` | yes | required when present |
| 10 | external prompt fragment | `GameContext` | `external_prompt_fragment` | yes | required when present |
| 11 | ritual role | `GameContext` | `ritual_role` | yes | optional/budgeted |
| 12 | ritual title | `GameContext` | `ritual_title` | yes | optional/budgeted |
| 13 | ability | `GameContext` | `ability_label` | yes | optional/budgeted |
| 14 | ability category | `GameContext` | `ability_category` | yes | optional/budgeted |
| 15 | ability target | `GameContext` | `ability_target` | yes | optional/budgeted |
| 16 | ability recharge ticks (long recharge = rarer, weightier use) | `GameContext` | `ability_cooldown_ticks` | no | optional/budgeted |
| 17 | raid arrival mode | `GameContext` | `arrival_mode` | yes | optional/budgeted |
| 18 | raid strategy | `GameContext` | `strategy` | yes | optional/budgeted |
| 19 | raid points | `GameContext` | `points` | no | optional/budgeted |
| 20 | royal title | `GameContext` | `royal_title` | yes | optional/budgeted |
| 21 | ideoligion role | `GameContext` | `ideological_role` | yes | optional/budgeted |
| 22 | important context | `PromptEnchantment` | — | yes | optional/budgeted |
| 23 | setting | `Setting` | — | yes | optional/budgeted |
| 24 | tone | `Tone` | — | yes | optional/budgeted |
| 25 | relationship | `Relationship` | — | yes | optional/budgeted |
| 26 | my last opening line (do not reuse) | `LastOpener` | — | yes | optional/budgeted |
| 27 | how my previous entry ended (continuity; do not retell it) | `PreviousEntryEnding` | — | yes | optional/budgeted |
| 28 | narrative context | `NarrativeContext` | — | yes | optional/budgeted |
| 29 | relevant past | `MemoryContext` | — | yes | required when present |
| 30 | belief context | `BeliefContext` | — | yes | optional/budgeted |
| 31 | pollution band | `GameContext` | `pollution_band` | yes | required when present |
| 32 | pollution transition | `GameContext` | `pollution_transition` | yes | required when present |
| 33 | map | `GameContext` | `map_label` | yes | optional/budgeted |
| 34 | context facet | `GameContext` | `facet` | yes | required when present |
| 35 | psychic bond | `GameContext` | `psychic_bond` | yes | required when present |
| 36 | first bonded pawn | `GameContext` | `bond_first_pawn_name` | yes | required when present |
| 37 | second bonded pawn | `GameContext` | `bond_second_pawn_name` | yes | required when present |
| 38 | verified rupture cause | `GameContext` | `cause` | yes | required when present |
| 39 | deathrest | `GameContext` | `deathrest` | yes | required when present |
| 40 | deathrest severity | `GameContext` | `completion_band` | yes | required when present |
| 41 | key relationships | `Identity` | — | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_PairDefault`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_PairImportant","templateKey":"PairImportant","includePersona":true,"includePromptEnchantment":true,"appendDirectSpeechInstruction":true,"maxTokens":200,"systemPromptSource":"xml","finalInstructionSource":"xml","recipientFinalInstructionSource":"xml","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"pov","source":"PovName","contextKey":""},{"enabled":true,"label":"role","source":"PovRole","contextKey":""},{"enabled":true,"label":"with","source":"OtherPawnName","contextKey":""},{"enabled":true,"label":"what you saw","source":"PovText","contextKey":""},{"enabled":true,"label":"instruction","source":"Instruction","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"external prompt instruction","source":"GameContext","contextKey":"external_prompt_instruction"},{"enabled":true,"label":"external prompt fragment","source":"GameContext","contextKey":"external_prompt_fragment"},{"enabled":true,"label":"ritual role","source":"GameContext","contextKey":"ritual_role"},{"enabled":true,"label":"ritual title","source":"GameContext","contextKey":"ritual_title"},{"enabled":true,"label":"ability","source":"GameContext","contextKey":"ability_label"},{"enabled":true,"label":"ability category","source":"GameContext","contextKey":"ability_category"},{"enabled":true,"label":"ability target","source":"GameContext","contextKey":"ability_target"},{"enabled":false,"label":"ability recharge ticks (long recharge = rarer, weightier use)","source":"GameContext","contextKey":"ability_cooldown_ticks"},{"enabled":true,"label":"raid arrival mode","source":"GameContext","contextKey":"arrival_mode"},{"enabled":true,"label":"raid strategy","source":"GameContext","contextKey":"strategy"},{"enabled":false,"label":"raid points","source":"GameContext","contextKey":"points"},{"enabled":true,"label":"royal title","source":"GameContext","contextKey":"royal_title"},{"enabled":true,"label":"ideoligion role","source":"GameContext","contextKey":"ideological_role"},{"enabled":true,"label":"important context","source":"PromptEnchantment","contextKey":""},{"enabled":true,"label":"setting","source":"Setting","contextKey":""},{"enabled":true,"label":"tone","source":"Tone","contextKey":""},{"enabled":true,"label":"relationship","source":"Relationship","contextKey":""},{"enabled":true,"label":"my last opening line (do not reuse)","source":"LastOpener","contextKey":""},{"enabled":true,"label":"initiator\u0027s private entry (you never read it; keep continuity, never mention it)","source":"HiddenInitiatorEntry","contextKey":""},{"enabled":true,"label":"how my previous entry ended (continuity; do not retell it)","source":"PreviousEntryEnding","contextKey":""},{"enabled":true,"label":"narrative context","source":"NarrativeContext","contextKey":""},{"enabled":true,"label":"birthday age","source":"GameContext","contextKey":"birthday_age"},{"enabled":true,"label":"opportunity at growth","source":"GameContext","contextKey":"opportunity_description"},{"enabled":false,"label":"chosen trait","source":"GameContext","contextKey":"selected_trait"},{"enabled":true,"label":"chosen trait meaning","source":"GameContext","contextKey":"selected_trait_description"},{"enabled":true,"label":"interest 1","source":"GameContext","contextKey":"new_interest_1"},{"enabled":true,"label":"interest 1 change","source":"GameContext","contextKey":"interest_change_1"},{"enabled":true,"label":"interest 2","source":"GameContext","contextKey":"new_interest_2"},{"enabled":true,"label":"interest 2 change","source":"GameContext","contextKey":"interest_change_2"},{"enabled":true,"label":"interest 3","source":"GameContext","contextKey":"new_interest_3"},{"enabled":true,"label":"interest 3 change","source":"GameContext","contextKey":"interest_change_3"},{"enabled":true,"label":"interest 4","source":"GameContext","contextKey":"new_interest_4"},{"enabled":true,"label":"interest 4 change","source":"GameContext","contextKey":"interest_change_4"},{"enabled":true,"label":"observed upbringing","source":"GameContext","contextKey":"observed_upbringing_description"},{"enabled":true,"label":"previous name","source":"GameContext","contextKey":"previous_name"},{"enabled":true,"label":"current name","source":"GameContext","contextKey":"current_name"},{"enabled":true,"label":"new responsibilities","source":"GameContext","contextKey":"new_responsibilities"},{"enabled":true,"label":"supporting adult","source":"GameContext","contextKey":"supporter_name"},{"enabled":true,"label":"supporting adult role","source":"GameContext","contextKey":"supporter_role"},{"enabled":true,"label":"initiator family role","source":"GameContext","contextKey":"initiator_family_role"},{"enabled":true,"label":"recipient family role","source":"GameContext","contextKey":"recipient_family_role"},{"enabled":true,"label":"child","source":"GameContext","contextKey":"child_name"},{"enabled":true,"label":"birth outcome","source":"GameContext","contextKey":"birth_outcome"},{"enabled":true,"label":"birth method","source":"GameContext","contextKey":"birth_method"},{"enabled":true,"label":"birther","source":"GameContext","contextKey":"birther_name"},{"enabled":true,"label":"genetic mother","source":"GameContext","contextKey":"genetic_mother_name"},{"enabled":true,"label":"father","source":"GameContext","contextKey":"father_name"},{"enabled":true,"label":"doctor","source":"GameContext","contextKey":"doctor_name"},{"enabled":true,"label":"birther died","source":"GameContext","contextKey":"birther_died"},{"enabled":true,"label":"ritual birth","source":"GameContext","contextKey":"ritual_birth"},{"enabled":true,"label":"journey phase","source":"GameContext","contextKey":"journey_phase"},{"enabled":true,"label":"journey reason","source":"GameContext","contextKey":"journey_reason"},{"enabled":true,"label":"secondary journey reason","source":"GameContext","contextKey":"journey_secondary_reason"},{"enabled":true,"label":"journey duration","source":"GameContext","contextKey":"journey_duration"},{"enabled":true,"label":"journey role","source":"GameContext","contextKey":"pov_journey_role"},{"enabled":true,"label":"ship","source":"GameContext","contextKey":"ship_name"},{"enabled":true,"label":"origin","source":"GameContext","contextKey":"origin"},{"enabled":true,"label":"destination","source":"GameContext","contextKey":"destination"},{"enabled":true,"label":"destination layer","source":"GameContext","contextKey":"destination_layer"},{"enabled":true,"label":"destination biome","source":"GameContext","contextKey":"destination_biome"},{"enabled":true,"label":"destination site","source":"GameContext","contextKey":"destination_site"},{"enabled":true,"label":"pilot","source":"GameContext","contextKey":"pilot"},{"enabled":true,"label":"copilot","source":"GameContext","contextKey":"copilot"},{"enabled":true,"label":"crew count","source":"GameContext","contextKey":"crew_count"},{"enabled":true,"label":"rough landing","source":"GameContext","contextKey":"rough_landing"},{"enabled":true,"label":"launch quality","source":"GameContext","contextKey":"launch_quality"},{"enabled":true,"label":"landing outcome","source":"GameContext","contextKey":"landing_outcome"},{"enabled":true,"label":"relevant past","source":"MemoryContext","contextKey":""},{"enabled":true,"label":"belief context","source":"BeliefContext","contextKey":""},{"enabled":true,"label":"pollution band","source":"GameContext","contextKey":"pollution_band"},{"enabled":true,"label":"pollution transition","source":"GameContext","contextKey":"pollution_transition"},{"enabled":true,"label":"map","source":"GameContext","contextKey":"map_label"},{"enabled":true,"label":"context facet","source":"GameContext","contextKey":"facet"},{"enabled":true,"label":"psychic bond","source":"GameContext","contextKey":"psychic_bond"},{"enabled":true,"label":"first bonded pawn","source":"GameContext","contextKey":"bond_first_pawn_name"},{"enabled":true,"label":"second bonded pawn","source":"GameContext","contextKey":"bond_second_pawn_name"},{"enabled":true,"label":"verified rupture cause","source":"GameContext","contextKey":"cause"},{"enabled":true,"label":"deathrest","source":"GameContext","contextKey":"deathrest"},{"enabled":true,"label":"deathrest severity","source":"GameContext","contextKey":"completion_band"},{"enabled":true,"label":"key relationships","source":"Identity","contextKey":""}]} -->
## Template: `PairImportant`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_PairImportant` |
| Selection | Important two-pawn event; also used by rich paired DLC routes. |
| Persona/style block | yes |
| Live prompt enchantment allowed | yes |
| Direct-speech instruction appended | yes |
| System prompt source | `xml` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `xml`; paired-recipient source `xml` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | pov | `PovName` | — | yes | required when present |
| 3 | role | `PovRole` | — | yes | required when present |
| 4 | with | `OtherPawnName` | — | yes | required when present |
| 5 | what you saw | `PovText` | — | yes | required when present |
| 6 | instruction | `Instruction` | — | yes | required when present |
| 7 | event prompt | `EventPrompt` | — | yes | required when present |
| 8 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 9 | external prompt instruction | `GameContext` | `external_prompt_instruction` | yes | required when present |
| 10 | external prompt fragment | `GameContext` | `external_prompt_fragment` | yes | required when present |
| 11 | ritual role | `GameContext` | `ritual_role` | yes | optional/budgeted |
| 12 | ritual title | `GameContext` | `ritual_title` | yes | optional/budgeted |
| 13 | ability | `GameContext` | `ability_label` | yes | optional/budgeted |
| 14 | ability category | `GameContext` | `ability_category` | yes | optional/budgeted |
| 15 | ability target | `GameContext` | `ability_target` | yes | optional/budgeted |
| 16 | ability recharge ticks (long recharge = rarer, weightier use) | `GameContext` | `ability_cooldown_ticks` | no | optional/budgeted |
| 17 | raid arrival mode | `GameContext` | `arrival_mode` | yes | optional/budgeted |
| 18 | raid strategy | `GameContext` | `strategy` | yes | optional/budgeted |
| 19 | raid points | `GameContext` | `points` | no | optional/budgeted |
| 20 | royal title | `GameContext` | `royal_title` | yes | optional/budgeted |
| 21 | ideoligion role | `GameContext` | `ideological_role` | yes | optional/budgeted |
| 22 | important context | `PromptEnchantment` | — | yes | optional/budgeted |
| 23 | setting | `Setting` | — | yes | optional/budgeted |
| 24 | tone | `Tone` | — | yes | optional/budgeted |
| 25 | relationship | `Relationship` | — | yes | optional/budgeted |
| 26 | my last opening line (do not reuse) | `LastOpener` | — | yes | optional/budgeted |
| 27 | initiator's private entry (you never read it; keep continuity, never mention it) | `HiddenInitiatorEntry` | — | yes | optional/budgeted |
| 28 | how my previous entry ended (continuity; do not retell it) | `PreviousEntryEnding` | — | yes | optional/budgeted |
| 29 | narrative context | `NarrativeContext` | — | yes | optional/budgeted |
| 30 | birthday age | `GameContext` | `birthday_age` | yes | required when present |
| 31 | opportunity at growth | `GameContext` | `opportunity_description` | yes | required when present |
| 32 | chosen trait | `GameContext` | `selected_trait` | no | required when present |
| 33 | chosen trait meaning | `GameContext` | `selected_trait_description` | yes | optional/budgeted |
| 34 | interest 1 | `GameContext` | `new_interest_1` | yes | required when present |
| 35 | interest 1 change | `GameContext` | `interest_change_1` | yes | required when present |
| 36 | interest 2 | `GameContext` | `new_interest_2` | yes | required when present |
| 37 | interest 2 change | `GameContext` | `interest_change_2` | yes | required when present |
| 38 | interest 3 | `GameContext` | `new_interest_3` | yes | required when present |
| 39 | interest 3 change | `GameContext` | `interest_change_3` | yes | required when present |
| 40 | interest 4 | `GameContext` | `new_interest_4` | yes | required when present |
| 41 | interest 4 change | `GameContext` | `interest_change_4` | yes | required when present |
| 42 | observed upbringing | `GameContext` | `observed_upbringing_description` | yes | optional/budgeted |
| 43 | previous name | `GameContext` | `previous_name` | yes | required when present |
| 44 | current name | `GameContext` | `current_name` | yes | required when present |
| 45 | new responsibilities | `GameContext` | `new_responsibilities` | yes | required when present |
| 46 | supporting adult | `GameContext` | `supporter_name` | yes | optional/budgeted |
| 47 | supporting adult role | `GameContext` | `supporter_role` | yes | required when present |
| 48 | initiator family role | `GameContext` | `initiator_family_role` | yes | required when present |
| 49 | recipient family role | `GameContext` | `recipient_family_role` | yes | required when present |
| 50 | child | `GameContext` | `child_name` | yes | required when present |
| 51 | birth outcome | `GameContext` | `birth_outcome` | yes | required when present |
| 52 | birth method | `GameContext` | `birth_method` | yes | required when present |
| 53 | birther | `GameContext` | `birther_name` | yes | optional/budgeted |
| 54 | genetic mother | `GameContext` | `genetic_mother_name` | yes | optional/budgeted |
| 55 | father | `GameContext` | `father_name` | yes | optional/budgeted |
| 56 | doctor | `GameContext` | `doctor_name` | yes | optional/budgeted |
| 57 | birther died | `GameContext` | `birther_died` | yes | required when present |
| 58 | ritual birth | `GameContext` | `ritual_birth` | yes | required when present |
| 59 | journey phase | `GameContext` | `journey_phase` | yes | required when present |
| 60 | journey reason | `GameContext` | `journey_reason` | yes | required when present |
| 61 | secondary journey reason | `GameContext` | `journey_secondary_reason` | yes | required when present |
| 62 | journey duration | `GameContext` | `journey_duration` | yes | required when present |
| 63 | journey role | `GameContext` | `pov_journey_role` | yes | required when present |
| 64 | ship | `GameContext` | `ship_name` | yes | required when present |
| 65 | origin | `GameContext` | `origin` | yes | required when present |
| 66 | destination | `GameContext` | `destination` | yes | required when present |
| 67 | destination layer | `GameContext` | `destination_layer` | yes | optional/budgeted |
| 68 | destination biome | `GameContext` | `destination_biome` | yes | optional/budgeted |
| 69 | destination site | `GameContext` | `destination_site` | yes | optional/budgeted |
| 70 | pilot | `GameContext` | `pilot` | yes | optional/budgeted |
| 71 | copilot | `GameContext` | `copilot` | yes | optional/budgeted |
| 72 | crew count | `GameContext` | `crew_count` | yes | optional/budgeted |
| 73 | rough landing | `GameContext` | `rough_landing` | yes | optional/budgeted |
| 74 | launch quality | `GameContext` | `launch_quality` | yes | optional/budgeted |
| 75 | landing outcome | `GameContext` | `landing_outcome` | yes | required when present |
| 76 | relevant past | `MemoryContext` | — | yes | required when present |
| 77 | belief context | `BeliefContext` | — | yes | optional/budgeted |
| 78 | pollution band | `GameContext` | `pollution_band` | yes | required when present |
| 79 | pollution transition | `GameContext` | `pollution_transition` | yes | required when present |
| 80 | map | `GameContext` | `map_label` | yes | optional/budgeted |
| 81 | context facet | `GameContext` | `facet` | yes | required when present |
| 82 | psychic bond | `GameContext` | `psychic_bond` | yes | required when present |
| 83 | first bonded pawn | `GameContext` | `bond_first_pawn_name` | yes | required when present |
| 84 | second bonded pawn | `GameContext` | `bond_second_pawn_name` | yes | required when present |
| 85 | verified rupture cause | `GameContext` | `cause` | yes | required when present |
| 86 | deathrest | `GameContext` | `deathrest` | yes | required when present |
| 87 | deathrest severity | `GameContext` | `completion_band` | yes | required when present |
| 88 | key relationships | `Identity` | — | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_PairImportant`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_PairCombat","templateKey":"PairCombat","includePersona":true,"includePromptEnchantment":true,"appendDirectSpeechInstruction":true,"maxTokens":200,"systemPromptSource":"xml","finalInstructionSource":"xml","recipientFinalInstructionSource":"xml","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"pov","source":"PovName","contextKey":""},{"enabled":true,"label":"role","source":"PovRole","contextKey":""},{"enabled":true,"label":"with","source":"OtherPawnName","contextKey":""},{"enabled":true,"label":"what you saw","source":"PovText","contextKey":""},{"enabled":true,"label":"instruction","source":"Instruction","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"external prompt instruction","source":"GameContext","contextKey":"external_prompt_instruction"},{"enabled":true,"label":"external prompt fragment","source":"GameContext","contextKey":"external_prompt_fragment"},{"enabled":true,"label":"ritual role","source":"GameContext","contextKey":"ritual_role"},{"enabled":true,"label":"ritual title","source":"GameContext","contextKey":"ritual_title"},{"enabled":true,"label":"ability","source":"GameContext","contextKey":"ability_label"},{"enabled":true,"label":"ability category","source":"GameContext","contextKey":"ability_category"},{"enabled":true,"label":"ability target","source":"GameContext","contextKey":"ability_target"},{"enabled":false,"label":"ability recharge ticks (long recharge = rarer, weightier use)","source":"GameContext","contextKey":"ability_cooldown_ticks"},{"enabled":true,"label":"raid arrival mode","source":"GameContext","contextKey":"arrival_mode"},{"enabled":true,"label":"raid strategy","source":"GameContext","contextKey":"strategy"},{"enabled":false,"label":"raid points","source":"GameContext","contextKey":"points"},{"enabled":true,"label":"royal title","source":"GameContext","contextKey":"royal_title"},{"enabled":true,"label":"ideoligion role","source":"GameContext","contextKey":"ideological_role"},{"enabled":true,"label":"you","source":"PawnSummary","contextKey":""},{"enabled":true,"label":"important context","source":"PromptEnchantment","contextKey":""},{"enabled":true,"label":"setting","source":"Setting","contextKey":""},{"enabled":true,"label":"tone","source":"Tone","contextKey":""},{"enabled":true,"label":"relationship","source":"Relationship","contextKey":""},{"enabled":true,"label":"my last opening line (do not reuse)","source":"LastOpener","contextKey":""},{"enabled":true,"label":"weapon","source":"Weapon","contextKey":""},{"enabled":true,"label":"initiator\u0027s private entry (you never read it; keep continuity, never mention it)","source":"HiddenInitiatorEntry","contextKey":""},{"enabled":true,"label":"how my previous entry ended (continuity; do not retell it)","source":"PreviousEntryEnding","contextKey":""},{"enabled":true,"label":"narrative context","source":"NarrativeContext","contextKey":""},{"enabled":true,"label":"relevant past","source":"MemoryContext","contextKey":""},{"enabled":true,"label":"belief context","source":"BeliefContext","contextKey":""},{"enabled":true,"label":"combat beats","source":"GameContext","contextKey":"battle_beats"}]} -->
## Template: `PairCombat`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_PairCombat` |
| Selection | Two-pawn event classified as combat. |
| Persona/style block | yes |
| Live prompt enchantment allowed | yes |
| Direct-speech instruction appended | yes |
| System prompt source | `xml` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `xml`; paired-recipient source `xml` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | pov | `PovName` | — | yes | required when present |
| 3 | role | `PovRole` | — | yes | required when present |
| 4 | with | `OtherPawnName` | — | yes | required when present |
| 5 | what you saw | `PovText` | — | yes | required when present |
| 6 | instruction | `Instruction` | — | yes | required when present |
| 7 | event prompt | `EventPrompt` | — | yes | required when present |
| 8 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 9 | external prompt instruction | `GameContext` | `external_prompt_instruction` | yes | required when present |
| 10 | external prompt fragment | `GameContext` | `external_prompt_fragment` | yes | required when present |
| 11 | ritual role | `GameContext` | `ritual_role` | yes | optional/budgeted |
| 12 | ritual title | `GameContext` | `ritual_title` | yes | optional/budgeted |
| 13 | ability | `GameContext` | `ability_label` | yes | optional/budgeted |
| 14 | ability category | `GameContext` | `ability_category` | yes | optional/budgeted |
| 15 | ability target | `GameContext` | `ability_target` | yes | optional/budgeted |
| 16 | ability recharge ticks (long recharge = rarer, weightier use) | `GameContext` | `ability_cooldown_ticks` | no | optional/budgeted |
| 17 | raid arrival mode | `GameContext` | `arrival_mode` | yes | optional/budgeted |
| 18 | raid strategy | `GameContext` | `strategy` | yes | optional/budgeted |
| 19 | raid points | `GameContext` | `points` | no | optional/budgeted |
| 20 | royal title | `GameContext` | `royal_title` | yes | optional/budgeted |
| 21 | ideoligion role | `GameContext` | `ideological_role` | yes | optional/budgeted |
| 22 | you | `PawnSummary` | — | yes | optional/budgeted |
| 23 | important context | `PromptEnchantment` | — | yes | optional/budgeted |
| 24 | setting | `Setting` | — | yes | optional/budgeted |
| 25 | tone | `Tone` | — | yes | optional/budgeted |
| 26 | relationship | `Relationship` | — | yes | optional/budgeted |
| 27 | my last opening line (do not reuse) | `LastOpener` | — | yes | optional/budgeted |
| 28 | weapon | `Weapon` | — | yes | optional/budgeted |
| 29 | initiator's private entry (you never read it; keep continuity, never mention it) | `HiddenInitiatorEntry` | — | yes | optional/budgeted |
| 30 | how my previous entry ended (continuity; do not retell it) | `PreviousEntryEnding` | — | yes | optional/budgeted |
| 31 | narrative context | `NarrativeContext` | — | yes | optional/budgeted |
| 32 | relevant past | `MemoryContext` | — | yes | required when present |
| 33 | belief context | `BeliefContext` | — | yes | optional/budgeted |
| 34 | combat beats | `GameContext` | `battle_beats` | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_PairCombat`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_PairBatched","templateKey":"PairBatched","includePersona":true,"includePromptEnchantment":true,"appendDirectSpeechInstruction":true,"maxTokens":0,"systemPromptSource":"fallback","finalInstructionSource":"fallback","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"pov","source":"PovName","contextKey":""},{"enabled":true,"label":"role","source":"PovRole","contextKey":""},{"enabled":true,"label":"with","source":"OtherPawnName","contextKey":""},{"enabled":true,"label":"what you saw","source":"PovText","contextKey":""},{"enabled":true,"label":"instruction","source":"Instruction","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"external prompt instruction","source":"GameContext","contextKey":"external_prompt_instruction"},{"enabled":true,"label":"external prompt fragment","source":"GameContext","contextKey":"external_prompt_fragment"},{"enabled":true,"label":"ritual role","source":"GameContext","contextKey":"ritual_role"},{"enabled":true,"label":"ritual title","source":"GameContext","contextKey":"ritual_title"},{"enabled":true,"label":"ability","source":"GameContext","contextKey":"ability_label"},{"enabled":true,"label":"ability category","source":"GameContext","contextKey":"ability_category"},{"enabled":true,"label":"ability target","source":"GameContext","contextKey":"ability_target"},{"enabled":false,"label":"ability recharge ticks (long recharge = rarer, weightier use)","source":"GameContext","contextKey":"ability_cooldown_ticks"},{"enabled":true,"label":"raid arrival mode","source":"GameContext","contextKey":"arrival_mode"},{"enabled":true,"label":"raid strategy","source":"GameContext","contextKey":"strategy"},{"enabled":false,"label":"raid points","source":"GameContext","contextKey":"points"},{"enabled":true,"label":"royal title","source":"GameContext","contextKey":"royal_title"},{"enabled":true,"label":"ideoligion role","source":"GameContext","contextKey":"ideological_role"},{"enabled":true,"label":"important context","source":"PromptEnchantment","contextKey":""},{"enabled":true,"label":"setting","source":"Setting","contextKey":""},{"enabled":true,"label":"tone","source":"Tone","contextKey":""},{"enabled":true,"label":"my last opening line (do not reuse)","source":"LastOpener","contextKey":""},{"enabled":true,"label":"how my previous entry ended (continuity; do not retell it)","source":"PreviousEntryEnding","contextKey":""},{"enabled":true,"label":"narrative context","source":"NarrativeContext","contextKey":""},{"enabled":true,"label":"relevant past","source":"MemoryContext","contextKey":""},{"enabled":true,"label":"belief context","source":"BeliefContext","contextKey":""},{"enabled":true,"label":"key relationships","source":"Identity","contextKey":""},{"enabled":true,"label":"you","source":"MoodSnapshot","contextKey":""}]} -->
## Template: `PairBatched`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_PairBatched` |
| Selection | Flushed pair interaction batch. |
| Persona/style block | yes |
| Live prompt enchantment allowed | yes |
| Direct-speech instruction appended | yes |
| System prompt source | `fallback` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `fallback`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | pov | `PovName` | — | yes | required when present |
| 3 | role | `PovRole` | — | yes | required when present |
| 4 | with | `OtherPawnName` | — | yes | required when present |
| 5 | what you saw | `PovText` | — | yes | required when present |
| 6 | instruction | `Instruction` | — | yes | required when present |
| 7 | event prompt | `EventPrompt` | — | yes | required when present |
| 8 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 9 | external prompt instruction | `GameContext` | `external_prompt_instruction` | yes | required when present |
| 10 | external prompt fragment | `GameContext` | `external_prompt_fragment` | yes | required when present |
| 11 | ritual role | `GameContext` | `ritual_role` | yes | optional/budgeted |
| 12 | ritual title | `GameContext` | `ritual_title` | yes | optional/budgeted |
| 13 | ability | `GameContext` | `ability_label` | yes | optional/budgeted |
| 14 | ability category | `GameContext` | `ability_category` | yes | optional/budgeted |
| 15 | ability target | `GameContext` | `ability_target` | yes | optional/budgeted |
| 16 | ability recharge ticks (long recharge = rarer, weightier use) | `GameContext` | `ability_cooldown_ticks` | no | optional/budgeted |
| 17 | raid arrival mode | `GameContext` | `arrival_mode` | yes | optional/budgeted |
| 18 | raid strategy | `GameContext` | `strategy` | yes | optional/budgeted |
| 19 | raid points | `GameContext` | `points` | no | optional/budgeted |
| 20 | royal title | `GameContext` | `royal_title` | yes | optional/budgeted |
| 21 | ideoligion role | `GameContext` | `ideological_role` | yes | optional/budgeted |
| 22 | important context | `PromptEnchantment` | — | yes | optional/budgeted |
| 23 | setting | `Setting` | — | yes | optional/budgeted |
| 24 | tone | `Tone` | — | yes | optional/budgeted |
| 25 | my last opening line (do not reuse) | `LastOpener` | — | yes | optional/budgeted |
| 26 | how my previous entry ended (continuity; do not retell it) | `PreviousEntryEnding` | — | yes | optional/budgeted |
| 27 | narrative context | `NarrativeContext` | — | yes | optional/budgeted |
| 28 | relevant past | `MemoryContext` | — | yes | required when present |
| 29 | belief context | `BeliefContext` | — | yes | optional/budgeted |
| 30 | key relationships | `Identity` | — | yes | optional/budgeted |
| 31 | you | `MoodSnapshot` | — | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_PairBatched`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_SoloDefault","templateKey":"SoloDefault","includePersona":true,"includePromptEnchantment":true,"appendDirectSpeechInstruction":true,"maxTokens":0,"systemPromptSource":"fallback","finalInstructionSource":"fallback","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"pov","source":"PovName","contextKey":""},{"enabled":true,"label":"what happened","source":"PovText","contextKey":""},{"enabled":true,"label":"instruction","source":"Instruction","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"external prompt instruction","source":"GameContext","contextKey":"external_prompt_instruction"},{"enabled":true,"label":"external prompt fragment","source":"GameContext","contextKey":"external_prompt_fragment"},{"enabled":true,"label":"quest name","source":"GameContext","contextKey":"quest_label"},{"enabled":true,"label":"quest lifecycle","source":"GameContext","contextKey":"quest_signal"},{"enabled":true,"label":"quest faction","source":"GameContext","contextKey":"quest_faction"},{"enabled":true,"label":"quest rewards","source":"GameContext","contextKey":"quest_rewards"},{"enabled":true,"label":"progression kind","source":"GameContext","contextKey":"progression_kind"},{"enabled":true,"label":"skill","source":"GameContext","contextKey":"skill"},{"enabled":false,"label":"skill level","source":"GameContext","contextKey":"skill_level"},{"enabled":false,"label":"previous skill milestone","source":"GameContext","contextKey":"previous_skill_milestone"},{"enabled":true,"label":"passion","source":"GameContext","contextKey":"passion"},{"enabled":false,"label":"psylink level","source":"GameContext","contextKey":"psylink_level"},{"enabled":false,"label":"previous psylink level","source":"GameContext","contextKey":"previous_psylink_level"},{"enabled":true,"label":"xenotype","source":"GameContext","contextKey":"xenotype"},{"enabled":true,"label":"previous xenotype","source":"GameContext","contextKey":"previous_xenotype"},{"enabled":true,"label":"major xenotype","source":"GameContext","contextKey":"major_xenotype"},{"enabled":true,"label":"title","source":"GameContext","contextKey":"title"},{"enabled":true,"label":"previous title","source":"GameContext","contextKey":"previous_title"},{"enabled":true,"label":"ritual role","source":"GameContext","contextKey":"ritual_role"},{"enabled":true,"label":"ritual title","source":"GameContext","contextKey":"ritual_title"},{"enabled":true,"label":"ability","source":"GameContext","contextKey":"ability_label"},{"enabled":true,"label":"ability category","source":"GameContext","contextKey":"ability_category"},{"enabled":true,"label":"ability target","source":"GameContext","contextKey":"ability_target"},{"enabled":false,"label":"ability recharge ticks (long recharge = rarer, weightier use)","source":"GameContext","contextKey":"ability_cooldown_ticks"},{"enabled":true,"label":"raid arrival mode","source":"GameContext","contextKey":"arrival_mode"},{"enabled":true,"label":"raid strategy","source":"GameContext","contextKey":"strategy"},{"enabled":false,"label":"raid points","source":"GameContext","contextKey":"points"},{"enabled":true,"label":"royal title","source":"GameContext","contextKey":"royal_title"},{"enabled":true,"label":"ideoligion role","source":"GameContext","contextKey":"ideological_role"},{"enabled":true,"label":"important context","source":"PromptEnchantment","contextKey":""},{"enabled":true,"label":"setting","source":"Setting","contextKey":""},{"enabled":true,"label":"tone","source":"Tone","contextKey":""},{"enabled":true,"label":"my last opening line (do not reuse)","source":"LastOpener","contextKey":""},{"enabled":true,"label":"how my previous entry ended (continuity; do not retell it)","source":"PreviousEntryEnding","contextKey":""},{"enabled":false,"label":"trait","source":"GameContext","contextKey":"trait"},{"enabled":true,"label":"trait description","source":"GameContext","contextKey":"trait_description"},{"enabled":true,"label":"narrative context","source":"NarrativeContext","contextKey":""},{"enabled":true,"label":"relevant past","source":"MemoryContext","contextKey":""},{"enabled":true,"label":"belief context","source":"BeliefContext","contextKey":""},{"enabled":true,"label":"pollution band","source":"GameContext","contextKey":"pollution_band"},{"enabled":true,"label":"pollution transition","source":"GameContext","contextKey":"pollution_transition"},{"enabled":true,"label":"map","source":"GameContext","contextKey":"map_label"},{"enabled":true,"label":"context facet","source":"GameContext","contextKey":"facet"},{"enabled":true,"label":"psychic bond","source":"GameContext","contextKey":"psychic_bond"},{"enabled":true,"label":"first bonded pawn","source":"GameContext","contextKey":"bond_first_pawn_name"},{"enabled":true,"label":"second bonded pawn","source":"GameContext","contextKey":"bond_second_pawn_name"},{"enabled":true,"label":"verified rupture cause","source":"GameContext","contextKey":"cause"},{"enabled":true,"label":"deathrest","source":"GameContext","contextKey":"deathrest"},{"enabled":true,"label":"deathrest severity","source":"GameContext","contextKey":"completion_band"},{"enabled":true,"label":"odyssey site category","source":"GameContext","contextKey":"odyssey_site_category"},{"enabled":true,"label":"major odyssey destination","source":"GameContext","contextKey":"odyssey_major_destination"}]} -->
## Template: `SoloDefault`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_SoloDefault` |
| Selection | Ordinary admitted one-POV event. |
| Persona/style block | yes |
| Live prompt enchantment allowed | yes |
| Direct-speech instruction appended | yes |
| System prompt source | `fallback` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `fallback`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | pov | `PovName` | — | yes | required when present |
| 3 | what happened | `PovText` | — | yes | required when present |
| 4 | instruction | `Instruction` | — | yes | required when present |
| 5 | event prompt | `EventPrompt` | — | yes | required when present |
| 6 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 7 | external prompt instruction | `GameContext` | `external_prompt_instruction` | yes | required when present |
| 8 | external prompt fragment | `GameContext` | `external_prompt_fragment` | yes | required when present |
| 9 | quest name | `GameContext` | `quest_label` | yes | required when present |
| 10 | quest lifecycle | `GameContext` | `quest_signal` | yes | required when present |
| 11 | quest faction | `GameContext` | `quest_faction` | yes | optional/budgeted |
| 12 | quest rewards | `GameContext` | `quest_rewards` | yes | optional/budgeted |
| 13 | progression kind | `GameContext` | `progression_kind` | yes | optional/budgeted |
| 14 | skill | `GameContext` | `skill` | yes | optional/budgeted |
| 15 | skill level | `GameContext` | `skill_level` | no | optional/budgeted |
| 16 | previous skill milestone | `GameContext` | `previous_skill_milestone` | no | optional/budgeted |
| 17 | passion | `GameContext` | `passion` | yes | optional/budgeted |
| 18 | psylink level | `GameContext` | `psylink_level` | no | required when present |
| 19 | previous psylink level | `GameContext` | `previous_psylink_level` | no | required when present |
| 20 | xenotype | `GameContext` | `xenotype` | yes | optional/budgeted |
| 21 | previous xenotype | `GameContext` | `previous_xenotype` | yes | optional/budgeted |
| 22 | major xenotype | `GameContext` | `major_xenotype` | yes | optional/budgeted |
| 23 | title | `GameContext` | `title` | yes | required when present |
| 24 | previous title | `GameContext` | `previous_title` | yes | required when present |
| 25 | ritual role | `GameContext` | `ritual_role` | yes | optional/budgeted |
| 26 | ritual title | `GameContext` | `ritual_title` | yes | optional/budgeted |
| 27 | ability | `GameContext` | `ability_label` | yes | optional/budgeted |
| 28 | ability category | `GameContext` | `ability_category` | yes | optional/budgeted |
| 29 | ability target | `GameContext` | `ability_target` | yes | optional/budgeted |
| 30 | ability recharge ticks (long recharge = rarer, weightier use) | `GameContext` | `ability_cooldown_ticks` | no | optional/budgeted |
| 31 | raid arrival mode | `GameContext` | `arrival_mode` | yes | optional/budgeted |
| 32 | raid strategy | `GameContext` | `strategy` | yes | optional/budgeted |
| 33 | raid points | `GameContext` | `points` | no | optional/budgeted |
| 34 | royal title | `GameContext` | `royal_title` | yes | optional/budgeted |
| 35 | ideoligion role | `GameContext` | `ideological_role` | yes | optional/budgeted |
| 36 | important context | `PromptEnchantment` | — | yes | optional/budgeted |
| 37 | setting | `Setting` | — | yes | optional/budgeted |
| 38 | tone | `Tone` | — | yes | optional/budgeted |
| 39 | my last opening line (do not reuse) | `LastOpener` | — | yes | optional/budgeted |
| 40 | how my previous entry ended (continuity; do not retell it) | `PreviousEntryEnding` | — | yes | optional/budgeted |
| 41 | trait | `GameContext` | `trait` | no | optional/budgeted |
| 42 | trait description | `GameContext` | `trait_description` | yes | optional/budgeted |
| 43 | narrative context | `NarrativeContext` | — | yes | optional/budgeted |
| 44 | relevant past | `MemoryContext` | — | yes | required when present |
| 45 | belief context | `BeliefContext` | — | yes | optional/budgeted |
| 46 | pollution band | `GameContext` | `pollution_band` | yes | required when present |
| 47 | pollution transition | `GameContext` | `pollution_transition` | yes | required when present |
| 48 | map | `GameContext` | `map_label` | yes | optional/budgeted |
| 49 | context facet | `GameContext` | `facet` | yes | required when present |
| 50 | psychic bond | `GameContext` | `psychic_bond` | yes | required when present |
| 51 | first bonded pawn | `GameContext` | `bond_first_pawn_name` | yes | required when present |
| 52 | second bonded pawn | `GameContext` | `bond_second_pawn_name` | yes | required when present |
| 53 | verified rupture cause | `GameContext` | `cause` | yes | required when present |
| 54 | deathrest | `GameContext` | `deathrest` | yes | required when present |
| 55 | deathrest severity | `GameContext` | `completion_band` | yes | required when present |
| 56 | odyssey site category | `GameContext` | `odyssey_site_category` | yes | optional/budgeted |
| 57 | major odyssey destination | `GameContext` | `odyssey_major_destination` | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_SoloDefault`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_SoloImportant","templateKey":"SoloImportant","includePersona":true,"includePromptEnchantment":true,"appendDirectSpeechInstruction":true,"maxTokens":200,"systemPromptSource":"xml","finalInstructionSource":"xml","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"pov","source":"PovName","contextKey":""},{"enabled":true,"label":"what happened","source":"PovText","contextKey":""},{"enabled":true,"label":"instruction","source":"Instruction","contextKey":""},{"enabled":true,"label":"you","source":"PawnSummary","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"external prompt instruction","source":"GameContext","contextKey":"external_prompt_instruction"},{"enabled":true,"label":"external prompt fragment","source":"GameContext","contextKey":"external_prompt_fragment"},{"enabled":true,"label":"quest name","source":"GameContext","contextKey":"quest_label"},{"enabled":true,"label":"quest lifecycle","source":"GameContext","contextKey":"quest_signal"},{"enabled":true,"label":"quest faction","source":"GameContext","contextKey":"quest_faction"},{"enabled":true,"label":"quest rewards","source":"GameContext","contextKey":"quest_rewards"},{"enabled":true,"label":"progression kind","source":"GameContext","contextKey":"progression_kind"},{"enabled":true,"label":"skill","source":"GameContext","contextKey":"skill"},{"enabled":false,"label":"skill level","source":"GameContext","contextKey":"skill_level"},{"enabled":false,"label":"previous skill milestone","source":"GameContext","contextKey":"previous_skill_milestone"},{"enabled":true,"label":"passion","source":"GameContext","contextKey":"passion"},{"enabled":false,"label":"psylink level","source":"GameContext","contextKey":"psylink_level"},{"enabled":false,"label":"previous psylink level","source":"GameContext","contextKey":"previous_psylink_level"},{"enabled":true,"label":"xenotype","source":"GameContext","contextKey":"xenotype"},{"enabled":true,"label":"previous xenotype","source":"GameContext","contextKey":"previous_xenotype"},{"enabled":true,"label":"major xenotype","source":"GameContext","contextKey":"major_xenotype"},{"enabled":true,"label":"title","source":"GameContext","contextKey":"title"},{"enabled":true,"label":"previous title","source":"GameContext","contextKey":"previous_title"},{"enabled":true,"label":"ritual role","source":"GameContext","contextKey":"ritual_role"},{"enabled":true,"label":"ritual title","source":"GameContext","contextKey":"ritual_title"},{"enabled":true,"label":"ability","source":"GameContext","contextKey":"ability_label"},{"enabled":true,"label":"ability category","source":"GameContext","contextKey":"ability_category"},{"enabled":true,"label":"ability target","source":"GameContext","contextKey":"ability_target"},{"enabled":false,"label":"ability recharge ticks (long recharge = rarer, weightier use)","source":"GameContext","contextKey":"ability_cooldown_ticks"},{"enabled":true,"label":"raid arrival mode","source":"GameContext","contextKey":"arrival_mode"},{"enabled":true,"label":"raid strategy","source":"GameContext","contextKey":"strategy"},{"enabled":false,"label":"raid points","source":"GameContext","contextKey":"points"},{"enabled":true,"label":"royal title","source":"GameContext","contextKey":"royal_title"},{"enabled":true,"label":"ideoligion role","source":"GameContext","contextKey":"ideological_role"},{"enabled":true,"label":"important context","source":"PromptEnchantment","contextKey":""},{"enabled":true,"label":"setting","source":"Setting","contextKey":""},{"enabled":true,"label":"tone","source":"Tone","contextKey":""},{"enabled":true,"label":"my last opening line (do not reuse)","source":"LastOpener","contextKey":""},{"enabled":true,"label":"how my previous entry ended (continuity; do not retell it)","source":"PreviousEntryEnding","contextKey":""},{"enabled":false,"label":"trait","source":"GameContext","contextKey":"trait"},{"enabled":true,"label":"trait description","source":"GameContext","contextKey":"trait_description"},{"enabled":true,"label":"narrative context","source":"NarrativeContext","contextKey":""},{"enabled":true,"label":"birthday age","source":"GameContext","contextKey":"birthday_age"},{"enabled":true,"label":"opportunity at growth","source":"GameContext","contextKey":"opportunity_description"},{"enabled":false,"label":"chosen trait","source":"GameContext","contextKey":"selected_trait"},{"enabled":true,"label":"chosen trait meaning","source":"GameContext","contextKey":"selected_trait_description"},{"enabled":true,"label":"interest 1","source":"GameContext","contextKey":"new_interest_1"},{"enabled":true,"label":"interest 1 change","source":"GameContext","contextKey":"interest_change_1"},{"enabled":true,"label":"interest 2","source":"GameContext","contextKey":"new_interest_2"},{"enabled":true,"label":"interest 2 change","source":"GameContext","contextKey":"interest_change_2"},{"enabled":true,"label":"interest 3","source":"GameContext","contextKey":"new_interest_3"},{"enabled":true,"label":"interest 3 change","source":"GameContext","contextKey":"interest_change_3"},{"enabled":true,"label":"interest 4","source":"GameContext","contextKey":"new_interest_4"},{"enabled":true,"label":"interest 4 change","source":"GameContext","contextKey":"interest_change_4"},{"enabled":true,"label":"observed upbringing","source":"GameContext","contextKey":"observed_upbringing_description"},{"enabled":true,"label":"previous name","source":"GameContext","contextKey":"previous_name"},{"enabled":true,"label":"current name","source":"GameContext","contextKey":"current_name"},{"enabled":true,"label":"new responsibilities","source":"GameContext","contextKey":"new_responsibilities"},{"enabled":true,"label":"supporting adult","source":"GameContext","contextKey":"supporter_name"},{"enabled":true,"label":"supporting adult role","source":"GameContext","contextKey":"supporter_role"},{"enabled":true,"label":"initiator family role","source":"GameContext","contextKey":"initiator_family_role"},{"enabled":true,"label":"recipient family role","source":"GameContext","contextKey":"recipient_family_role"},{"enabled":true,"label":"child","source":"GameContext","contextKey":"child_name"},{"enabled":true,"label":"birth outcome","source":"GameContext","contextKey":"birth_outcome"},{"enabled":true,"label":"birth method","source":"GameContext","contextKey":"birth_method"},{"enabled":true,"label":"birther","source":"GameContext","contextKey":"birther_name"},{"enabled":true,"label":"genetic mother","source":"GameContext","contextKey":"genetic_mother_name"},{"enabled":true,"label":"father","source":"GameContext","contextKey":"father_name"},{"enabled":true,"label":"doctor","source":"GameContext","contextKey":"doctor_name"},{"enabled":true,"label":"birther died","source":"GameContext","contextKey":"birther_died"},{"enabled":true,"label":"ritual birth","source":"GameContext","contextKey":"ritual_birth"},{"enabled":true,"label":"journey phase","source":"GameContext","contextKey":"journey_phase"},{"enabled":true,"label":"journey reason","source":"GameContext","contextKey":"journey_reason"},{"enabled":true,"label":"secondary journey reason","source":"GameContext","contextKey":"journey_secondary_reason"},{"enabled":true,"label":"journey duration","source":"GameContext","contextKey":"journey_duration"},{"enabled":true,"label":"journey role","source":"GameContext","contextKey":"pov_journey_role"},{"enabled":true,"label":"ship","source":"GameContext","contextKey":"ship_name"},{"enabled":true,"label":"origin","source":"GameContext","contextKey":"origin"},{"enabled":true,"label":"destination","source":"GameContext","contextKey":"destination"},{"enabled":true,"label":"destination layer","source":"GameContext","contextKey":"destination_layer"},{"enabled":true,"label":"destination biome","source":"GameContext","contextKey":"destination_biome"},{"enabled":true,"label":"destination site","source":"GameContext","contextKey":"destination_site"},{"enabled":true,"label":"pilot","source":"GameContext","contextKey":"pilot"},{"enabled":true,"label":"copilot","source":"GameContext","contextKey":"copilot"},{"enabled":true,"label":"crew count","source":"GameContext","contextKey":"crew_count"},{"enabled":true,"label":"rough landing","source":"GameContext","contextKey":"rough_landing"},{"enabled":true,"label":"launch quality","source":"GameContext","contextKey":"launch_quality"},{"enabled":true,"label":"landing outcome","source":"GameContext","contextKey":"landing_outcome"},{"enabled":true,"label":"persona weapon","source":"GameContext","contextKey":"persona_weapon_name"},{"enabled":true,"label":"bond event","source":"GameContext","contextKey":"persona_weapon"},{"enabled":true,"label":"previous bond state","source":"GameContext","contextKey":"bond_previous_state"},{"enabled":true,"label":"new bond state","source":"GameContext","contextKey":"bond_new_state"},{"enabled":true,"label":"separation duration","source":"GameContext","contextKey":"bond_separation_duration"},{"enabled":true,"label":"bond duration","source":"GameContext","contextKey":"bond_duration"},{"enabled":true,"label":"previous bonded pawn","source":"GameContext","contextKey":"bond_previous_pawn"},{"enabled":true,"label":"bond ending cause","source":"GameContext","contextKey":"bond_end_cause"},{"enabled":false,"label":"persona trait 1","source":"GameContext","contextKey":"persona_trait_1"},{"enabled":true,"label":"persona trait 1 meaning","source":"GameContext","contextKey":"persona_trait_description_1"},{"enabled":false,"label":"persona trait 2","source":"GameContext","contextKey":"persona_trait_2"},{"enabled":true,"label":"persona trait 2 meaning","source":"GameContext","contextKey":"persona_trait_description_2"},{"enabled":true,"label":"persona milestone","source":"GameContext","contextKey":"persona_milestone"},{"enabled":false,"label":"source tale","source":"GameContext","contextKey":"tale_source_def"},{"enabled":true,"label":"source tale name","source":"GameContext","contextKey":"tale_source_label"},{"enabled":true,"label":"killer tale role","source":"GameContext","contextKey":"tale_killer_role"},{"enabled":true,"label":"victim tale role","source":"GameContext","contextKey":"tale_victim_role"},{"enabled":true,"label":"royal mutation pawn","source":"GameContext","contextKey":"royal_mutation_pawn"},{"enabled":true,"label":"royal cause","source":"GameContext","contextKey":"royal_cause"},{"enabled":true,"label":"royal transition","source":"GameContext","contextKey":"royal_transition"},{"enabled":true,"label":"royal faction","source":"GameContext","contextKey":"royal_faction"},{"enabled":true,"label":"psylink cause","source":"GameContext","contextKey":"psylink_cause"},{"enabled":true,"label":"new royal duties","source":"GameContext","contextKey":"royal_duty_changes"},{"enabled":true,"label":"deceased title holder","source":"GameContext","contextKey":"succession_deceased"},{"enabled":true,"label":"royal heir","source":"GameContext","contextKey":"succession_heir"},{"enabled":true,"label":"inherited title","source":"GameContext","contextKey":"succession_title"},{"enabled":true,"label":"succession faction","source":"GameContext","contextKey":"succession_faction"},{"enabled":true,"label":"permit","source":"GameContext","contextKey":"permit_label"},{"enabled":false,"label":"permit family","source":"GameContext","contextKey":"permit_family"},{"enabled":true,"label":"permit faction","source":"GameContext","contextKey":"permit_faction"},{"enabled":true,"label":"permit title","source":"GameContext","contextKey":"permit_title"},{"enabled":true,"label":"permit setting","source":"GameContext","contextKey":"permit_setting"},{"enabled":true,"label":"used during cooldown","source":"GameContext","contextKey":"used_during_cooldown"},{"enabled":true,"label":"relevant past","source":"MemoryContext","contextKey":""},{"enabled":true,"label":"belief context","source":"BeliefContext","contextKey":""},{"enabled":true,"label":"pollution band","source":"GameContext","contextKey":"pollution_band"},{"enabled":true,"label":"pollution transition","source":"GameContext","contextKey":"pollution_transition"},{"enabled":true,"label":"map","source":"GameContext","contextKey":"map_label"},{"enabled":true,"label":"context facet","source":"GameContext","contextKey":"facet"},{"enabled":true,"label":"psychic bond","source":"GameContext","contextKey":"psychic_bond"},{"enabled":true,"label":"first bonded pawn","source":"GameContext","contextKey":"bond_first_pawn_name"},{"enabled":true,"label":"second bonded pawn","source":"GameContext","contextKey":"bond_second_pawn_name"},{"enabled":true,"label":"verified rupture cause","source":"GameContext","contextKey":"cause"},{"enabled":true,"label":"deathrest","source":"GameContext","contextKey":"deathrest"},{"enabled":true,"label":"deathrest severity","source":"GameContext","contextKey":"completion_band"},{"enabled":true,"label":"odyssey site category","source":"GameContext","contextKey":"odyssey_site_category"},{"enabled":true,"label":"major odyssey destination","source":"GameContext","contextKey":"odyssey_major_destination"},{"enabled":true,"label":"combat beats","source":"GameContext","contextKey":"battle_beats"}]} -->
## Template: `SoloImportant`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_SoloImportant` |
| Selection | Important solo, fan-out, or source-owned page. |
| Persona/style block | yes |
| Live prompt enchantment allowed | yes |
| Direct-speech instruction appended | yes |
| System prompt source | `xml` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `xml`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | pov | `PovName` | — | yes | required when present |
| 3 | what happened | `PovText` | — | yes | required when present |
| 4 | instruction | `Instruction` | — | yes | required when present |
| 5 | you | `PawnSummary` | — | yes | optional/budgeted |
| 6 | event prompt | `EventPrompt` | — | yes | required when present |
| 7 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 8 | external prompt instruction | `GameContext` | `external_prompt_instruction` | yes | required when present |
| 9 | external prompt fragment | `GameContext` | `external_prompt_fragment` | yes | required when present |
| 10 | quest name | `GameContext` | `quest_label` | yes | required when present |
| 11 | quest lifecycle | `GameContext` | `quest_signal` | yes | required when present |
| 12 | quest faction | `GameContext` | `quest_faction` | yes | optional/budgeted |
| 13 | quest rewards | `GameContext` | `quest_rewards` | yes | optional/budgeted |
| 14 | progression kind | `GameContext` | `progression_kind` | yes | optional/budgeted |
| 15 | skill | `GameContext` | `skill` | yes | optional/budgeted |
| 16 | skill level | `GameContext` | `skill_level` | no | optional/budgeted |
| 17 | previous skill milestone | `GameContext` | `previous_skill_milestone` | no | optional/budgeted |
| 18 | passion | `GameContext` | `passion` | yes | optional/budgeted |
| 19 | psylink level | `GameContext` | `psylink_level` | no | required when present |
| 20 | previous psylink level | `GameContext` | `previous_psylink_level` | no | required when present |
| 21 | xenotype | `GameContext` | `xenotype` | yes | optional/budgeted |
| 22 | previous xenotype | `GameContext` | `previous_xenotype` | yes | optional/budgeted |
| 23 | major xenotype | `GameContext` | `major_xenotype` | yes | optional/budgeted |
| 24 | title | `GameContext` | `title` | yes | required when present |
| 25 | previous title | `GameContext` | `previous_title` | yes | required when present |
| 26 | ritual role | `GameContext` | `ritual_role` | yes | optional/budgeted |
| 27 | ritual title | `GameContext` | `ritual_title` | yes | optional/budgeted |
| 28 | ability | `GameContext` | `ability_label` | yes | optional/budgeted |
| 29 | ability category | `GameContext` | `ability_category` | yes | optional/budgeted |
| 30 | ability target | `GameContext` | `ability_target` | yes | optional/budgeted |
| 31 | ability recharge ticks (long recharge = rarer, weightier use) | `GameContext` | `ability_cooldown_ticks` | no | optional/budgeted |
| 32 | raid arrival mode | `GameContext` | `arrival_mode` | yes | optional/budgeted |
| 33 | raid strategy | `GameContext` | `strategy` | yes | optional/budgeted |
| 34 | raid points | `GameContext` | `points` | no | optional/budgeted |
| 35 | royal title | `GameContext` | `royal_title` | yes | optional/budgeted |
| 36 | ideoligion role | `GameContext` | `ideological_role` | yes | optional/budgeted |
| 37 | important context | `PromptEnchantment` | — | yes | optional/budgeted |
| 38 | setting | `Setting` | — | yes | optional/budgeted |
| 39 | tone | `Tone` | — | yes | optional/budgeted |
| 40 | my last opening line (do not reuse) | `LastOpener` | — | yes | optional/budgeted |
| 41 | how my previous entry ended (continuity; do not retell it) | `PreviousEntryEnding` | — | yes | optional/budgeted |
| 42 | trait | `GameContext` | `trait` | no | optional/budgeted |
| 43 | trait description | `GameContext` | `trait_description` | yes | optional/budgeted |
| 44 | narrative context | `NarrativeContext` | — | yes | optional/budgeted |
| 45 | birthday age | `GameContext` | `birthday_age` | yes | required when present |
| 46 | opportunity at growth | `GameContext` | `opportunity_description` | yes | required when present |
| 47 | chosen trait | `GameContext` | `selected_trait` | no | required when present |
| 48 | chosen trait meaning | `GameContext` | `selected_trait_description` | yes | optional/budgeted |
| 49 | interest 1 | `GameContext` | `new_interest_1` | yes | required when present |
| 50 | interest 1 change | `GameContext` | `interest_change_1` | yes | required when present |
| 51 | interest 2 | `GameContext` | `new_interest_2` | yes | required when present |
| 52 | interest 2 change | `GameContext` | `interest_change_2` | yes | required when present |
| 53 | interest 3 | `GameContext` | `new_interest_3` | yes | required when present |
| 54 | interest 3 change | `GameContext` | `interest_change_3` | yes | required when present |
| 55 | interest 4 | `GameContext` | `new_interest_4` | yes | required when present |
| 56 | interest 4 change | `GameContext` | `interest_change_4` | yes | required when present |
| 57 | observed upbringing | `GameContext` | `observed_upbringing_description` | yes | optional/budgeted |
| 58 | previous name | `GameContext` | `previous_name` | yes | required when present |
| 59 | current name | `GameContext` | `current_name` | yes | required when present |
| 60 | new responsibilities | `GameContext` | `new_responsibilities` | yes | required when present |
| 61 | supporting adult | `GameContext` | `supporter_name` | yes | optional/budgeted |
| 62 | supporting adult role | `GameContext` | `supporter_role` | yes | required when present |
| 63 | initiator family role | `GameContext` | `initiator_family_role` | yes | required when present |
| 64 | recipient family role | `GameContext` | `recipient_family_role` | yes | required when present |
| 65 | child | `GameContext` | `child_name` | yes | required when present |
| 66 | birth outcome | `GameContext` | `birth_outcome` | yes | required when present |
| 67 | birth method | `GameContext` | `birth_method` | yes | required when present |
| 68 | birther | `GameContext` | `birther_name` | yes | optional/budgeted |
| 69 | genetic mother | `GameContext` | `genetic_mother_name` | yes | optional/budgeted |
| 70 | father | `GameContext` | `father_name` | yes | optional/budgeted |
| 71 | doctor | `GameContext` | `doctor_name` | yes | optional/budgeted |
| 72 | birther died | `GameContext` | `birther_died` | yes | required when present |
| 73 | ritual birth | `GameContext` | `ritual_birth` | yes | required when present |
| 74 | journey phase | `GameContext` | `journey_phase` | yes | required when present |
| 75 | journey reason | `GameContext` | `journey_reason` | yes | required when present |
| 76 | secondary journey reason | `GameContext` | `journey_secondary_reason` | yes | required when present |
| 77 | journey duration | `GameContext` | `journey_duration` | yes | required when present |
| 78 | journey role | `GameContext` | `pov_journey_role` | yes | required when present |
| 79 | ship | `GameContext` | `ship_name` | yes | required when present |
| 80 | origin | `GameContext` | `origin` | yes | required when present |
| 81 | destination | `GameContext` | `destination` | yes | required when present |
| 82 | destination layer | `GameContext` | `destination_layer` | yes | optional/budgeted |
| 83 | destination biome | `GameContext` | `destination_biome` | yes | optional/budgeted |
| 84 | destination site | `GameContext` | `destination_site` | yes | optional/budgeted |
| 85 | pilot | `GameContext` | `pilot` | yes | optional/budgeted |
| 86 | copilot | `GameContext` | `copilot` | yes | optional/budgeted |
| 87 | crew count | `GameContext` | `crew_count` | yes | optional/budgeted |
| 88 | rough landing | `GameContext` | `rough_landing` | yes | optional/budgeted |
| 89 | launch quality | `GameContext` | `launch_quality` | yes | optional/budgeted |
| 90 | landing outcome | `GameContext` | `landing_outcome` | yes | required when present |
| 91 | persona weapon | `GameContext` | `persona_weapon_name` | yes | required when present |
| 92 | bond event | `GameContext` | `persona_weapon` | yes | required when present |
| 93 | previous bond state | `GameContext` | `bond_previous_state` | yes | required when present |
| 94 | new bond state | `GameContext` | `bond_new_state` | yes | required when present |
| 95 | separation duration | `GameContext` | `bond_separation_duration` | yes | required when present |
| 96 | bond duration | `GameContext` | `bond_duration` | yes | required when present |
| 97 | previous bonded pawn | `GameContext` | `bond_previous_pawn` | yes | required when present |
| 98 | bond ending cause | `GameContext` | `bond_end_cause` | yes | required when present |
| 99 | persona trait 1 | `GameContext` | `persona_trait_1` | no | optional/budgeted |
| 100 | persona trait 1 meaning | `GameContext` | `persona_trait_description_1` | yes | optional/budgeted |
| 101 | persona trait 2 | `GameContext` | `persona_trait_2` | no | optional/budgeted |
| 102 | persona trait 2 meaning | `GameContext` | `persona_trait_description_2` | yes | optional/budgeted |
| 103 | persona milestone | `GameContext` | `persona_milestone` | yes | required when present |
| 104 | source tale | `GameContext` | `tale_source_def` | no | required when present |
| 105 | source tale name | `GameContext` | `tale_source_label` | yes | required when present |
| 106 | killer tale role | `GameContext` | `tale_killer_role` | yes | required when present |
| 107 | victim tale role | `GameContext` | `tale_victim_role` | yes | required when present |
| 108 | royal mutation pawn | `GameContext` | `royal_mutation_pawn` | yes | required when present |
| 109 | royal cause | `GameContext` | `royal_cause` | yes | required when present |
| 110 | royal transition | `GameContext` | `royal_transition` | yes | required when present |
| 111 | royal faction | `GameContext` | `royal_faction` | yes | required when present |
| 112 | psylink cause | `GameContext` | `psylink_cause` | yes | required when present |
| 113 | new royal duties | `GameContext` | `royal_duty_changes` | yes | optional/budgeted |
| 114 | deceased title holder | `GameContext` | `succession_deceased` | yes | required when present |
| 115 | royal heir | `GameContext` | `succession_heir` | yes | required when present |
| 116 | inherited title | `GameContext` | `succession_title` | yes | required when present |
| 117 | succession faction | `GameContext` | `succession_faction` | yes | required when present |
| 118 | permit | `GameContext` | `permit_label` | yes | required when present |
| 119 | permit family | `GameContext` | `permit_family` | no | required when present |
| 120 | permit faction | `GameContext` | `permit_faction` | yes | required when present |
| 121 | permit title | `GameContext` | `permit_title` | yes | required when present |
| 122 | permit setting | `GameContext` | `permit_setting` | yes | optional/budgeted |
| 123 | used during cooldown | `GameContext` | `used_during_cooldown` | yes | required when present |
| 124 | relevant past | `MemoryContext` | — | yes | required when present |
| 125 | belief context | `BeliefContext` | — | yes | optional/budgeted |
| 126 | pollution band | `GameContext` | `pollution_band` | yes | required when present |
| 127 | pollution transition | `GameContext` | `pollution_transition` | yes | required when present |
| 128 | map | `GameContext` | `map_label` | yes | optional/budgeted |
| 129 | context facet | `GameContext` | `facet` | yes | required when present |
| 130 | psychic bond | `GameContext` | `psychic_bond` | yes | required when present |
| 131 | first bonded pawn | `GameContext` | `bond_first_pawn_name` | yes | required when present |
| 132 | second bonded pawn | `GameContext` | `bond_second_pawn_name` | yes | required when present |
| 133 | verified rupture cause | `GameContext` | `cause` | yes | required when present |
| 134 | deathrest | `GameContext` | `deathrest` | yes | required when present |
| 135 | deathrest severity | `GameContext` | `completion_band` | yes | required when present |
| 136 | odyssey site category | `GameContext` | `odyssey_site_category` | yes | optional/budgeted |
| 137 | major odyssey destination | `GameContext` | `odyssey_major_destination` | yes | optional/budgeted |
| 138 | combat beats | `GameContext` | `battle_beats` | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_SoloImportant`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_SoloInternalState","templateKey":"SoloInternalState","includePersona":true,"includePromptEnchantment":true,"appendDirectSpeechInstruction":true,"maxTokens":0,"systemPromptSource":"fallback","finalInstructionSource":"fallback","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"pov","source":"PovName","contextKey":""},{"enabled":true,"label":"what happened","source":"PovText","contextKey":""},{"enabled":true,"label":"instruction","source":"Instruction","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"external prompt instruction","source":"GameContext","contextKey":"external_prompt_instruction"},{"enabled":true,"label":"external prompt fragment","source":"GameContext","contextKey":"external_prompt_fragment"},{"enabled":true,"label":"progression kind","source":"GameContext","contextKey":"progression_kind"},{"enabled":true,"label":"skill","source":"GameContext","contextKey":"skill"},{"enabled":false,"label":"skill level","source":"GameContext","contextKey":"skill_level"},{"enabled":false,"label":"previous skill milestone","source":"GameContext","contextKey":"previous_skill_milestone"},{"enabled":true,"label":"passion","source":"GameContext","contextKey":"passion"},{"enabled":false,"label":"psylink level","source":"GameContext","contextKey":"psylink_level"},{"enabled":false,"label":"previous psylink level","source":"GameContext","contextKey":"previous_psylink_level"},{"enabled":true,"label":"xenotype","source":"GameContext","contextKey":"xenotype"},{"enabled":true,"label":"previous xenotype","source":"GameContext","contextKey":"previous_xenotype"},{"enabled":true,"label":"major xenotype","source":"GameContext","contextKey":"major_xenotype"},{"enabled":true,"label":"title","source":"GameContext","contextKey":"title"},{"enabled":true,"label":"previous title","source":"GameContext","contextKey":"previous_title"},{"enabled":true,"label":"ritual role","source":"GameContext","contextKey":"ritual_role"},{"enabled":true,"label":"ritual title","source":"GameContext","contextKey":"ritual_title"},{"enabled":true,"label":"ability","source":"GameContext","contextKey":"ability_label"},{"enabled":true,"label":"ability category","source":"GameContext","contextKey":"ability_category"},{"enabled":true,"label":"ability target","source":"GameContext","contextKey":"ability_target"},{"enabled":false,"label":"ability recharge ticks (long recharge = rarer, weightier use)","source":"GameContext","contextKey":"ability_cooldown_ticks"},{"enabled":true,"label":"raid arrival mode","source":"GameContext","contextKey":"arrival_mode"},{"enabled":true,"label":"raid strategy","source":"GameContext","contextKey":"strategy"},{"enabled":false,"label":"raid points","source":"GameContext","contextKey":"points"},{"enabled":true,"label":"royal title","source":"GameContext","contextKey":"royal_title"},{"enabled":true,"label":"ideoligion role","source":"GameContext","contextKey":"ideological_role"},{"enabled":true,"label":"important context","source":"PromptEnchantment","contextKey":""},{"enabled":true,"label":"setting","source":"Setting","contextKey":""},{"enabled":true,"label":"tone","source":"Tone","contextKey":""},{"enabled":true,"label":"my last opening line (do not reuse)","source":"LastOpener","contextKey":""},{"enabled":true,"label":"how my previous entry ended (continuity; do not retell it)","source":"PreviousEntryEnding","contextKey":""},{"enabled":false,"label":"trait","source":"GameContext","contextKey":"trait"},{"enabled":true,"label":"trait description","source":"GameContext","contextKey":"trait_description"},{"enabled":true,"label":"narrative context","source":"NarrativeContext","contextKey":""},{"enabled":true,"label":"relevant past","source":"MemoryContext","contextKey":""},{"enabled":true,"label":"belief context","source":"BeliefContext","contextKey":""},{"enabled":true,"label":"you","source":"MoodSnapshot","contextKey":""}]} -->
## Template: `SoloInternalState`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_SoloInternalState` |
| Selection | Thought, mood, mental, inspiration, or similar internal-state page. |
| Persona/style block | yes |
| Live prompt enchantment allowed | yes |
| Direct-speech instruction appended | yes |
| System prompt source | `fallback` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `fallback`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | pov | `PovName` | — | yes | required when present |
| 3 | what happened | `PovText` | — | yes | required when present |
| 4 | instruction | `Instruction` | — | yes | required when present |
| 5 | event prompt | `EventPrompt` | — | yes | required when present |
| 6 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 7 | external prompt instruction | `GameContext` | `external_prompt_instruction` | yes | required when present |
| 8 | external prompt fragment | `GameContext` | `external_prompt_fragment` | yes | required when present |
| 9 | progression kind | `GameContext` | `progression_kind` | yes | optional/budgeted |
| 10 | skill | `GameContext` | `skill` | yes | optional/budgeted |
| 11 | skill level | `GameContext` | `skill_level` | no | optional/budgeted |
| 12 | previous skill milestone | `GameContext` | `previous_skill_milestone` | no | optional/budgeted |
| 13 | passion | `GameContext` | `passion` | yes | optional/budgeted |
| 14 | psylink level | `GameContext` | `psylink_level` | no | required when present |
| 15 | previous psylink level | `GameContext` | `previous_psylink_level` | no | required when present |
| 16 | xenotype | `GameContext` | `xenotype` | yes | optional/budgeted |
| 17 | previous xenotype | `GameContext` | `previous_xenotype` | yes | optional/budgeted |
| 18 | major xenotype | `GameContext` | `major_xenotype` | yes | optional/budgeted |
| 19 | title | `GameContext` | `title` | yes | required when present |
| 20 | previous title | `GameContext` | `previous_title` | yes | required when present |
| 21 | ritual role | `GameContext` | `ritual_role` | yes | optional/budgeted |
| 22 | ritual title | `GameContext` | `ritual_title` | yes | optional/budgeted |
| 23 | ability | `GameContext` | `ability_label` | yes | optional/budgeted |
| 24 | ability category | `GameContext` | `ability_category` | yes | optional/budgeted |
| 25 | ability target | `GameContext` | `ability_target` | yes | optional/budgeted |
| 26 | ability recharge ticks (long recharge = rarer, weightier use) | `GameContext` | `ability_cooldown_ticks` | no | optional/budgeted |
| 27 | raid arrival mode | `GameContext` | `arrival_mode` | yes | optional/budgeted |
| 28 | raid strategy | `GameContext` | `strategy` | yes | optional/budgeted |
| 29 | raid points | `GameContext` | `points` | no | optional/budgeted |
| 30 | royal title | `GameContext` | `royal_title` | yes | optional/budgeted |
| 31 | ideoligion role | `GameContext` | `ideological_role` | yes | optional/budgeted |
| 32 | important context | `PromptEnchantment` | — | yes | optional/budgeted |
| 33 | setting | `Setting` | — | yes | optional/budgeted |
| 34 | tone | `Tone` | — | yes | optional/budgeted |
| 35 | my last opening line (do not reuse) | `LastOpener` | — | yes | optional/budgeted |
| 36 | how my previous entry ended (continuity; do not retell it) | `PreviousEntryEnding` | — | yes | optional/budgeted |
| 37 | trait | `GameContext` | `trait` | no | optional/budgeted |
| 38 | trait description | `GameContext` | `trait_description` | yes | optional/budgeted |
| 39 | narrative context | `NarrativeContext` | — | yes | optional/budgeted |
| 40 | relevant past | `MemoryContext` | — | yes | required when present |
| 41 | belief context | `BeliefContext` | — | yes | optional/budgeted |
| 42 | you | `MoodSnapshot` | — | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_SoloInternalState`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_SoloBatched","templateKey":"SoloBatched","includePersona":true,"includePromptEnchantment":true,"appendDirectSpeechInstruction":true,"maxTokens":0,"systemPromptSource":"fallback","finalInstructionSource":"fallback","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"pov","source":"PovName","contextKey":""},{"enabled":true,"label":"what happened","source":"PovText","contextKey":""},{"enabled":true,"label":"instruction","source":"Instruction","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"external prompt instruction","source":"GameContext","contextKey":"external_prompt_instruction"},{"enabled":true,"label":"external prompt fragment","source":"GameContext","contextKey":"external_prompt_fragment"},{"enabled":true,"label":"progression kind","source":"GameContext","contextKey":"progression_kind"},{"enabled":true,"label":"skill","source":"GameContext","contextKey":"skill"},{"enabled":false,"label":"skill level","source":"GameContext","contextKey":"skill_level"},{"enabled":false,"label":"previous skill milestone","source":"GameContext","contextKey":"previous_skill_milestone"},{"enabled":true,"label":"passion","source":"GameContext","contextKey":"passion"},{"enabled":false,"label":"psylink level","source":"GameContext","contextKey":"psylink_level"},{"enabled":false,"label":"previous psylink level","source":"GameContext","contextKey":"previous_psylink_level"},{"enabled":true,"label":"xenotype","source":"GameContext","contextKey":"xenotype"},{"enabled":true,"label":"previous xenotype","source":"GameContext","contextKey":"previous_xenotype"},{"enabled":true,"label":"major xenotype","source":"GameContext","contextKey":"major_xenotype"},{"enabled":true,"label":"title","source":"GameContext","contextKey":"title"},{"enabled":true,"label":"previous title","source":"GameContext","contextKey":"previous_title"},{"enabled":true,"label":"ritual role","source":"GameContext","contextKey":"ritual_role"},{"enabled":true,"label":"ritual title","source":"GameContext","contextKey":"ritual_title"},{"enabled":true,"label":"ability","source":"GameContext","contextKey":"ability_label"},{"enabled":true,"label":"ability category","source":"GameContext","contextKey":"ability_category"},{"enabled":true,"label":"ability target","source":"GameContext","contextKey":"ability_target"},{"enabled":false,"label":"ability recharge ticks (long recharge = rarer, weightier use)","source":"GameContext","contextKey":"ability_cooldown_ticks"},{"enabled":true,"label":"raid arrival mode","source":"GameContext","contextKey":"arrival_mode"},{"enabled":true,"label":"raid strategy","source":"GameContext","contextKey":"strategy"},{"enabled":false,"label":"raid points","source":"GameContext","contextKey":"points"},{"enabled":true,"label":"royal title","source":"GameContext","contextKey":"royal_title"},{"enabled":true,"label":"ideoligion role","source":"GameContext","contextKey":"ideological_role"},{"enabled":true,"label":"important context","source":"PromptEnchantment","contextKey":""},{"enabled":true,"label":"setting","source":"Setting","contextKey":""},{"enabled":true,"label":"tone","source":"Tone","contextKey":""},{"enabled":true,"label":"my last opening line (do not reuse)","source":"LastOpener","contextKey":""},{"enabled":true,"label":"how my previous entry ended (continuity; do not retell it)","source":"PreviousEntryEnding","contextKey":""},{"enabled":false,"label":"trait","source":"GameContext","contextKey":"trait"},{"enabled":true,"label":"trait description","source":"GameContext","contextKey":"trait_description"},{"enabled":true,"label":"narrative context","source":"NarrativeContext","contextKey":""},{"enabled":true,"label":"relevant past","source":"MemoryContext","contextKey":""},{"enabled":true,"label":"belief context","source":"BeliefContext","contextKey":""},{"enabled":true,"label":"you","source":"MoodSnapshot","contextKey":""}]} -->
## Template: `SoloBatched`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_SoloBatched` |
| Selection | Flushed solo Tale or ambient batch. |
| Persona/style block | yes |
| Live prompt enchantment allowed | yes |
| Direct-speech instruction appended | yes |
| System prompt source | `fallback` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `fallback`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | pov | `PovName` | — | yes | required when present |
| 3 | what happened | `PovText` | — | yes | required when present |
| 4 | instruction | `Instruction` | — | yes | required when present |
| 5 | event prompt | `EventPrompt` | — | yes | required when present |
| 6 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 7 | external prompt instruction | `GameContext` | `external_prompt_instruction` | yes | required when present |
| 8 | external prompt fragment | `GameContext` | `external_prompt_fragment` | yes | required when present |
| 9 | progression kind | `GameContext` | `progression_kind` | yes | optional/budgeted |
| 10 | skill | `GameContext` | `skill` | yes | optional/budgeted |
| 11 | skill level | `GameContext` | `skill_level` | no | optional/budgeted |
| 12 | previous skill milestone | `GameContext` | `previous_skill_milestone` | no | optional/budgeted |
| 13 | passion | `GameContext` | `passion` | yes | optional/budgeted |
| 14 | psylink level | `GameContext` | `psylink_level` | no | required when present |
| 15 | previous psylink level | `GameContext` | `previous_psylink_level` | no | required when present |
| 16 | xenotype | `GameContext` | `xenotype` | yes | optional/budgeted |
| 17 | previous xenotype | `GameContext` | `previous_xenotype` | yes | optional/budgeted |
| 18 | major xenotype | `GameContext` | `major_xenotype` | yes | optional/budgeted |
| 19 | title | `GameContext` | `title` | yes | required when present |
| 20 | previous title | `GameContext` | `previous_title` | yes | required when present |
| 21 | ritual role | `GameContext` | `ritual_role` | yes | optional/budgeted |
| 22 | ritual title | `GameContext` | `ritual_title` | yes | optional/budgeted |
| 23 | ability | `GameContext` | `ability_label` | yes | optional/budgeted |
| 24 | ability category | `GameContext` | `ability_category` | yes | optional/budgeted |
| 25 | ability target | `GameContext` | `ability_target` | yes | optional/budgeted |
| 26 | ability recharge ticks (long recharge = rarer, weightier use) | `GameContext` | `ability_cooldown_ticks` | no | optional/budgeted |
| 27 | raid arrival mode | `GameContext` | `arrival_mode` | yes | optional/budgeted |
| 28 | raid strategy | `GameContext` | `strategy` | yes | optional/budgeted |
| 29 | raid points | `GameContext` | `points` | no | optional/budgeted |
| 30 | royal title | `GameContext` | `royal_title` | yes | optional/budgeted |
| 31 | ideoligion role | `GameContext` | `ideological_role` | yes | optional/budgeted |
| 32 | important context | `PromptEnchantment` | — | yes | optional/budgeted |
| 33 | setting | `Setting` | — | yes | optional/budgeted |
| 34 | tone | `Tone` | — | yes | optional/budgeted |
| 35 | my last opening line (do not reuse) | `LastOpener` | — | yes | optional/budgeted |
| 36 | how my previous entry ended (continuity; do not retell it) | `PreviousEntryEnding` | — | yes | optional/budgeted |
| 37 | trait | `GameContext` | `trait` | no | optional/budgeted |
| 38 | trait description | `GameContext` | `trait_description` | yes | optional/budgeted |
| 39 | narrative context | `NarrativeContext` | — | yes | optional/budgeted |
| 40 | relevant past | `MemoryContext` | — | yes | required when present |
| 41 | belief context | `BeliefContext` | — | yes | optional/budgeted |
| 42 | you | `MoodSnapshot` | — | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_SoloBatched`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_SoloDayReflection","templateKey":"SoloDayReflection","includePersona":true,"includePromptEnchantment":true,"appendDirectSpeechInstruction":false,"maxTokens":0,"systemPromptSource":"fallback","finalInstructionSource":"xml","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"pov","source":"PovName","contextKey":""},{"enabled":true,"label":"what happened","source":"PovText","contextKey":""},{"enabled":true,"label":"instruction","source":"Instruction","contextKey":""},{"enabled":true,"label":"you","source":"PawnSummary","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"progression kind","source":"GameContext","contextKey":"progression_kind"},{"enabled":true,"label":"skill","source":"GameContext","contextKey":"skill"},{"enabled":false,"label":"skill level","source":"GameContext","contextKey":"skill_level"},{"enabled":false,"label":"previous skill milestone","source":"GameContext","contextKey":"previous_skill_milestone"},{"enabled":true,"label":"passion","source":"GameContext","contextKey":"passion"},{"enabled":false,"label":"psylink level","source":"GameContext","contextKey":"psylink_level"},{"enabled":false,"label":"previous psylink level","source":"GameContext","contextKey":"previous_psylink_level"},{"enabled":true,"label":"xenotype","source":"GameContext","contextKey":"xenotype"},{"enabled":true,"label":"previous xenotype","source":"GameContext","contextKey":"previous_xenotype"},{"enabled":true,"label":"major xenotype","source":"GameContext","contextKey":"major_xenotype"},{"enabled":true,"label":"title","source":"GameContext","contextKey":"title"},{"enabled":true,"label":"previous title","source":"GameContext","contextKey":"previous_title"},{"enabled":true,"label":"ritual role","source":"GameContext","contextKey":"ritual_role"},{"enabled":true,"label":"ritual title","source":"GameContext","contextKey":"ritual_title"},{"enabled":true,"label":"ability","source":"GameContext","contextKey":"ability_label"},{"enabled":true,"label":"ability category","source":"GameContext","contextKey":"ability_category"},{"enabled":true,"label":"ability target","source":"GameContext","contextKey":"ability_target"},{"enabled":false,"label":"ability recharge ticks (long recharge = rarer, weightier use)","source":"GameContext","contextKey":"ability_cooldown_ticks"},{"enabled":true,"label":"raid arrival mode","source":"GameContext","contextKey":"arrival_mode"},{"enabled":true,"label":"raid strategy","source":"GameContext","contextKey":"strategy"},{"enabled":false,"label":"raid points","source":"GameContext","contextKey":"points"},{"enabled":true,"label":"royal title","source":"GameContext","contextKey":"royal_title"},{"enabled":true,"label":"ideoligion role","source":"GameContext","contextKey":"ideological_role"},{"enabled":true,"label":"important context","source":"PromptEnchantment","contextKey":""},{"enabled":true,"label":"setting","source":"Setting","contextKey":""},{"enabled":true,"label":"tone","source":"Tone","contextKey":""},{"enabled":true,"label":"my last opening line (do not reuse)","source":"LastOpener","contextKey":""},{"enabled":true,"label":"how my previous entry ended (continuity; do not retell it)","source":"PreviousEntryEnding","contextKey":""},{"enabled":false,"label":"trait","source":"GameContext","contextKey":"trait"},{"enabled":true,"label":"trait description","source":"GameContext","contextKey":"trait_description"},{"enabled":true,"label":"narrative context","source":"NarrativeContext","contextKey":""},{"enabled":true,"label":"relevant past","source":"MemoryContext","contextKey":""},{"enabled":true,"label":"belief context","source":"BeliefContext","contextKey":""}]} -->
## Template: `SoloDayReflection`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_SoloDayReflection` |
| Selection | End-of-day reflection. |
| Persona/style block | yes |
| Live prompt enchantment allowed | yes |
| Direct-speech instruction appended | no |
| System prompt source | `fallback` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `xml`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | pov | `PovName` | — | yes | required when present |
| 3 | what happened | `PovText` | — | yes | required when present |
| 4 | instruction | `Instruction` | — | yes | required when present |
| 5 | you | `PawnSummary` | — | yes | optional/budgeted |
| 6 | event prompt | `EventPrompt` | — | yes | required when present |
| 7 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 8 | progression kind | `GameContext` | `progression_kind` | yes | optional/budgeted |
| 9 | skill | `GameContext` | `skill` | yes | optional/budgeted |
| 10 | skill level | `GameContext` | `skill_level` | no | optional/budgeted |
| 11 | previous skill milestone | `GameContext` | `previous_skill_milestone` | no | optional/budgeted |
| 12 | passion | `GameContext` | `passion` | yes | optional/budgeted |
| 13 | psylink level | `GameContext` | `psylink_level` | no | required when present |
| 14 | previous psylink level | `GameContext` | `previous_psylink_level` | no | required when present |
| 15 | xenotype | `GameContext` | `xenotype` | yes | optional/budgeted |
| 16 | previous xenotype | `GameContext` | `previous_xenotype` | yes | optional/budgeted |
| 17 | major xenotype | `GameContext` | `major_xenotype` | yes | optional/budgeted |
| 18 | title | `GameContext` | `title` | yes | required when present |
| 19 | previous title | `GameContext` | `previous_title` | yes | required when present |
| 20 | ritual role | `GameContext` | `ritual_role` | yes | optional/budgeted |
| 21 | ritual title | `GameContext` | `ritual_title` | yes | optional/budgeted |
| 22 | ability | `GameContext` | `ability_label` | yes | optional/budgeted |
| 23 | ability category | `GameContext` | `ability_category` | yes | optional/budgeted |
| 24 | ability target | `GameContext` | `ability_target` | yes | optional/budgeted |
| 25 | ability recharge ticks (long recharge = rarer, weightier use) | `GameContext` | `ability_cooldown_ticks` | no | optional/budgeted |
| 26 | raid arrival mode | `GameContext` | `arrival_mode` | yes | optional/budgeted |
| 27 | raid strategy | `GameContext` | `strategy` | yes | optional/budgeted |
| 28 | raid points | `GameContext` | `points` | no | optional/budgeted |
| 29 | royal title | `GameContext` | `royal_title` | yes | optional/budgeted |
| 30 | ideoligion role | `GameContext` | `ideological_role` | yes | optional/budgeted |
| 31 | important context | `PromptEnchantment` | — | yes | optional/budgeted |
| 32 | setting | `Setting` | — | yes | optional/budgeted |
| 33 | tone | `Tone` | — | yes | optional/budgeted |
| 34 | my last opening line (do not reuse) | `LastOpener` | — | yes | optional/budgeted |
| 35 | how my previous entry ended (continuity; do not retell it) | `PreviousEntryEnding` | — | yes | optional/budgeted |
| 36 | trait | `GameContext` | `trait` | no | optional/budgeted |
| 37 | trait description | `GameContext` | `trait_description` | yes | optional/budgeted |
| 38 | narrative context | `NarrativeContext` | — | yes | optional/budgeted |
| 39 | relevant past | `MemoryContext` | — | yes | required when present |
| 40 | belief context | `BeliefContext` | — | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_SoloDayReflection`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_SoloQuadrumReflection","templateKey":"SoloQuadrumReflection","includePersona":true,"includePromptEnchantment":true,"appendDirectSpeechInstruction":false,"maxTokens":350,"systemPromptSource":"xml","finalInstructionSource":"xml","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"pov","source":"PovName","contextKey":""},{"enabled":true,"label":"quadrum dates","source":"GameContext","contextKey":"quadrum_dates"},{"enabled":true,"label":"what happened","source":"PovText","contextKey":""},{"enabled":true,"label":"instruction","source":"Instruction","contextKey":""},{"enabled":true,"label":"you","source":"PawnSummary","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"important entry count","source":"GameContext","contextKey":"important_entries"},{"enabled":true,"label":"setting","source":"Setting","contextKey":""},{"enabled":true,"label":"my last opening line (do not reuse)","source":"LastOpener","contextKey":""},{"enabled":true,"label":"how my previous entry ended (continuity; do not retell it)","source":"PreviousEntryEnding","contextKey":""},{"enabled":true,"label":"narrative context","source":"NarrativeContext","contextKey":""},{"enabled":true,"label":"relevant past","source":"MemoryContext","contextKey":""},{"enabled":true,"label":"belief context","source":"BeliefContext","contextKey":""},{"enabled":true,"label":"tone","source":"Tone","contextKey":""}]} -->
## Template: `SoloQuadrumReflection`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_SoloQuadrumReflection` |
| Selection | Quadrum reflection boundary. |
| Persona/style block | yes |
| Live prompt enchantment allowed | yes |
| Direct-speech instruction appended | no |
| System prompt source | `xml` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `xml`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | pov | `PovName` | — | yes | required when present |
| 3 | quadrum dates | `GameContext` | `quadrum_dates` | yes | required when present |
| 4 | what happened | `PovText` | — | yes | required when present |
| 5 | instruction | `Instruction` | — | yes | required when present |
| 6 | you | `PawnSummary` | — | yes | optional/budgeted |
| 7 | event prompt | `EventPrompt` | — | yes | required when present |
| 8 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 9 | important entry count | `GameContext` | `important_entries` | yes | optional/budgeted |
| 10 | setting | `Setting` | — | yes | optional/budgeted |
| 11 | my last opening line (do not reuse) | `LastOpener` | — | yes | optional/budgeted |
| 12 | how my previous entry ended (continuity; do not retell it) | `PreviousEntryEnding` | — | yes | optional/budgeted |
| 13 | narrative context | `NarrativeContext` | — | yes | optional/budgeted |
| 14 | relevant past | `MemoryContext` | — | yes | required when present |
| 15 | belief context | `BeliefContext` | — | yes | optional/budgeted |
| 16 | tone | `Tone` | — | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_SoloQuadrumReflection`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_SoloArcReflection","templateKey":"SoloArcReflection","includePersona":true,"includePromptEnchantment":true,"appendDirectSpeechInstruction":false,"maxTokens":420,"systemPromptSource":"xml","finalInstructionSource":"xml","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"pov","source":"PovName","contextKey":""},{"enabled":true,"label":"arc year","source":"GameContext","contextKey":"arc_year"},{"enabled":true,"label":"selected memories","source":"PovText","contextKey":""},{"enabled":true,"label":"instruction","source":"Instruction","contextKey":""},{"enabled":true,"label":"you","source":"PawnSummary","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"selected memory count","source":"GameContext","contextKey":"selected_memories"},{"enabled":true,"label":"candidate memory count","source":"GameContext","contextKey":"candidate_memories"},{"enabled":true,"label":"entries this year","source":"GameContext","contextKey":"entries_this_year"},{"enabled":true,"label":"forced yearly arc","source":"GameContext","contextKey":"forced"},{"enabled":true,"label":"setting","source":"Setting","contextKey":""},{"enabled":true,"label":"my last opening line (do not reuse)","source":"LastOpener","contextKey":""},{"enabled":true,"label":"how my previous entry ended (continuity; do not retell it)","source":"PreviousEntryEnding","contextKey":""},{"enabled":true,"label":"narrative context","source":"NarrativeContext","contextKey":""},{"enabled":true,"label":"relevant past","source":"MemoryContext","contextKey":""},{"enabled":true,"label":"belief context","source":"BeliefContext","contextKey":""},{"enabled":true,"label":"tone","source":"Tone","contextKey":""}]} -->
## Template: `SoloArcReflection`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_SoloArcReflection` |
| Selection | Year/arc reflection boundary. |
| Persona/style block | yes |
| Live prompt enchantment allowed | yes |
| Direct-speech instruction appended | no |
| System prompt source | `xml` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `xml`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | pov | `PovName` | — | yes | required when present |
| 3 | arc year | `GameContext` | `arc_year` | yes | required when present |
| 4 | selected memories | `PovText` | — | yes | required when present |
| 5 | instruction | `Instruction` | — | yes | required when present |
| 6 | you | `PawnSummary` | — | yes | optional/budgeted |
| 7 | event prompt | `EventPrompt` | — | yes | required when present |
| 8 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 9 | selected memory count | `GameContext` | `selected_memories` | yes | optional/budgeted |
| 10 | candidate memory count | `GameContext` | `candidate_memories` | yes | optional/budgeted |
| 11 | entries this year | `GameContext` | `entries_this_year` | yes | optional/budgeted |
| 12 | forced yearly arc | `GameContext` | `forced` | yes | optional/budgeted |
| 13 | setting | `Setting` | — | yes | optional/budgeted |
| 14 | my last opening line (do not reuse) | `LastOpener` | — | yes | optional/budgeted |
| 15 | how my previous entry ended (continuity; do not retell it) | `PreviousEntryEnding` | — | yes | optional/budgeted |
| 16 | narrative context | `NarrativeContext` | — | yes | optional/budgeted |
| 17 | relevant past | `MemoryContext` | — | yes | required when present |
| 18 | belief context | `BeliefContext` | — | yes | optional/budgeted |
| 19 | tone | `Tone` | — | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_SoloArcReflection`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_SoloBeliefReflection","templateKey":"SoloBeliefReflection","includePersona":true,"includePromptEnchantment":true,"appendDirectSpeechInstruction":false,"maxTokens":360,"systemPromptSource":"xml","finalInstructionSource":"xml","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"pov","source":"PovName","contextKey":""},{"enabled":true,"label":"what happened","source":"PovText","contextKey":""},{"enabled":true,"label":"instruction","source":"Instruction","contextKey":""},{"enabled":true,"label":"you","source":"PawnSummary","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"narrative context","source":"NarrativeContext","contextKey":""},{"enabled":true,"label":"relevant past","source":"MemoryContext","contextKey":""},{"enabled":true,"label":"belief context","source":"BeliefContext","contextKey":""},{"enabled":true,"label":"belief trigger","source":"GameContext","contextKey":"belief_reflection_trigger"},{"enabled":true,"label":"setting","source":"Setting","contextKey":""},{"enabled":true,"label":"my last opening line (do not reuse)","source":"LastOpener","contextKey":""},{"enabled":true,"label":"how my previous entry ended (continuity; do not retell it)","source":"PreviousEntryEnding","contextKey":""},{"enabled":true,"label":"tone","source":"Tone","contextKey":""}]} -->
## Template: `SoloBeliefReflection`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_SoloBeliefReflection` |
| Selection | Ideology belief-reflection boundary. |
| Persona/style block | yes |
| Live prompt enchantment allowed | yes |
| Direct-speech instruction appended | no |
| System prompt source | `xml` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `xml`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | pov | `PovName` | — | yes | required when present |
| 3 | what happened | `PovText` | — | yes | required when present |
| 4 | instruction | `Instruction` | — | yes | required when present |
| 5 | you | `PawnSummary` | — | yes | optional/budgeted |
| 6 | event prompt | `EventPrompt` | — | yes | required when present |
| 7 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 8 | narrative context | `NarrativeContext` | — | yes | optional/budgeted |
| 9 | relevant past | `MemoryContext` | — | yes | required when present |
| 10 | belief context | `BeliefContext` | — | yes | optional/budgeted |
| 11 | belief trigger | `GameContext` | `belief_reflection_trigger` | yes | required when present |
| 12 | setting | `Setting` | — | yes | optional/budgeted |
| 13 | my last opening line (do not reuse) | `LastOpener` | — | yes | optional/budgeted |
| 14 | how my previous entry ended (continuity; do not retell it) | `PreviousEntryEnding` | — | yes | optional/budgeted |
| 15 | tone | `Tone` | — | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_SoloBeliefReflection`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_DeathDescription","templateKey":"DeathDescription","includePersona":false,"includePromptEnchantment":false,"appendDirectSpeechInstruction":false,"maxTokens":0,"systemPromptSource":"fallback","finalInstructionSource":"fallback","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"deceased","source":"DeathVictim","contextKey":""},{"enabled":true,"label":"what happened","source":"NeutralText","contextKey":""},{"enabled":true,"label":"death facts","source":"DeathFacts","contextKey":""},{"enabled":true,"label":"deceased pawn","source":"DeathPawnSummary","contextKey":""},{"enabled":true,"label":"setting","source":"DeathSetting","contextKey":""},{"enabled":true,"label":"persona weapon","source":"GameContext","contextKey":"persona_weapon_name"},{"enabled":true,"label":"persona milestone","source":"GameContext","contextKey":"persona_milestone"},{"enabled":true,"label":"previous bond state","source":"GameContext","contextKey":"bond_previous_state"},{"enabled":true,"label":"new bond state","source":"GameContext","contextKey":"bond_new_state"},{"enabled":true,"label":"bond ending cause","source":"GameContext","contextKey":"bond_end_cause"},{"enabled":true,"label":"persona trait 1","source":"GameContext","contextKey":"persona_trait_1"},{"enabled":true,"label":"persona trait 2","source":"GameContext","contextKey":"persona_trait_2"}]} -->
## Template: `DeathDescription`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_DeathDescription` |
| Selection | Neutral death-description request. |
| Persona/style block | no |
| Live prompt enchantment allowed | no |
| Direct-speech instruction appended | no |
| System prompt source | `fallback` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `fallback`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | event prompt | `EventPrompt` | — | yes | required when present |
| 3 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 4 | deceased | `DeathVictim` | — | yes | required when present |
| 5 | what happened | `NeutralText` | — | yes | required when present |
| 6 | death facts | `DeathFacts` | — | yes | required when present |
| 7 | deceased pawn | `DeathPawnSummary` | — | yes | required when present |
| 8 | setting | `DeathSetting` | — | yes | optional/budgeted |
| 9 | persona weapon | `GameContext` | `persona_weapon_name` | yes | required when present |
| 10 | persona milestone | `GameContext` | `persona_milestone` | yes | required when present |
| 11 | previous bond state | `GameContext` | `bond_previous_state` | yes | required when present |
| 12 | new bond state | `GameContext` | `bond_new_state` | yes | required when present |
| 13 | bond ending cause | `GameContext` | `bond_end_cause` | yes | required when present |
| 14 | persona trait 1 | `GameContext` | `persona_trait_1` | yes | optional/budgeted |
| 15 | persona trait 2 | `GameContext` | `persona_trait_2` | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_DeathDescription`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_ArrivalDescription","templateKey":"ArrivalDescription","includePersona":false,"includePromptEnchantment":false,"appendDirectSpeechInstruction":false,"maxTokens":0,"systemPromptSource":"fallback","finalInstructionSource":"fallback","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"event","source":"EventNoun","contextKey":""},{"enabled":true,"label":"event prompt","source":"EventPrompt","contextKey":""},{"enabled":true,"label":"event enhancement","source":"EventEnhancement","contextKey":""},{"enabled":true,"label":"colonist","source":"ArrivalPawn","contextKey":""},{"enabled":true,"label":"what happened","source":"NeutralText","contextKey":""},{"enabled":true,"label":"arrival facts","source":"ArrivalFacts","contextKey":""},{"enabled":true,"label":"colonist pawn","source":"PawnSummary","contextKey":""},{"enabled":true,"label":"setting","source":"Setting","contextKey":""}]} -->
## Template: `ArrivalDescription`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_ArrivalDescription` |
| Selection | Neutral arrival-description request. |
| Persona/style block | no |
| Live prompt enchantment allowed | no |
| Direct-speech instruction appended | no |
| System prompt source | `fallback` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `fallback`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | event | `EventNoun` | — | yes | required when present |
| 2 | event prompt | `EventPrompt` | — | yes | required when present |
| 3 | event enhancement | `EventEnhancement` | — | yes | optional/budgeted |
| 4 | colonist | `ArrivalPawn` | — | yes | required when present |
| 5 | what happened | `NeutralText` | — | yes | required when present |
| 6 | arrival facts | `ArrivalFacts` | — | yes | required when present |
| 7 | colonist pawn | `PawnSummary` | — | yes | required when present |
| 8 | setting | `Setting` | — | yes | optional/budgeted |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_ArrivalDescription`

<!-- repowiki:template {"defName":"DiaryPromptTemplate_Title","templateKey":"Title","includePersona":false,"includePromptEnchantment":false,"appendDirectSpeechInstruction":false,"maxTokens":0,"systemPromptSource":"fallback","finalInstructionSource":"fallback","recipientFinalInstructionSource":"fallback","fields":[{"enabled":true,"label":"diary entry to title","source":"EntryText","contextKey":""}]} -->
## Template: `Title`

| Contract | Value |
|---|---|
| Def | `DiaryPromptTemplate_Title` |
| Selection | Separate bounded title request after a main page succeeds. |
| Persona/style block | no |
| Live prompt enchantment allowed | no |
| Direct-speech instruction appended | no |
| System prompt source | `fallback` (XML text when present; otherwise the matching shared defensive fallback) |
| Final instruction source | `fallback`; paired-recipient source `fallback` |
| Lane/title behavior | One main request uses the selected active lane and this template's cap (0 inherits the normal cap). After a successful non-title page, a separate bounded `Title` request may follow when titles are enabled. |

| # | Label | Source | Context key | Enabled | Budget class |
|---:|---|---|---|---:|---|
| 1 | diary entry to title | `EntryText` | — | yes | required when present |

Source: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml) — `DiaryPromptTemplate_Title`

## Event-prompt policy index

| Def | Selector | Packages | Shipped forced model |
|---|---|---|---|
| `DiaryEventPrompt_Interaction` | `Interaction` | Base game | — |
| `DiaryEventPrompt_MentalState` | `MentalState` | Base game | — |
| `DiaryEventPrompt_IdeoChange` | `IdeoChange` | Ideology (`Ludeon.RimWorld.Ideology`) | — |
| `DiaryEventPrompt_CounselSuccess` | `Counsel_Success` | Ideology (`Ludeon.RimWorld.Ideology`) | — |
| `DiaryEventPrompt_CounselFailure` | `Counsel_Failure` | Ideology (`Ludeon.RimWorld.Ideology`) | — |
| `DiaryEventPrompt_Tale` | `Tale` | Base game | — |
| `DiaryEventPrompt_MoodEvent` | `MoodEvent` | Base game | — |
| `DiaryEventPrompt_Thought` | `Thought` | Base game | — |
| `DiaryEventPrompt_Inspiration` | `Inspiration` | Base game | — |
| `DiaryEventPrompt_Romance` | `Romance` | Base game | — |
| `DiaryEventPrompt_Work` | `Work` | Base game | — |
| `DiaryEventPrompt_Hediff` | `Hediff` | Base game | — |
| `DiaryEventPrompt_Raid` | `Raid` | Base game | — |
| `DiaryEventPrompt_Quest` | `Quest` | Base game | — |
| `DiaryEventPrompt_RitualConversion` | `ritualConversion` | Ideology (`Ludeon.RimWorld.Ideology`) | — |
| `DiaryEventPrompt_Ritual` | `Ritual` | Base game | — |
| `DiaryEventPrompt_Ability` | `Ability` | Base game | — |
| `DiaryEventPrompt_DayReflection` | `DayReflection` | Base game | — |
| `DiaryEventPrompt_QuadrumReflection` | `QuadrumReflection` | Base game | — |
| `DiaryEventPrompt_Progression` | `Progression` | Base game | — |
| `DiaryEventPrompt_PawnBirthday` | `PawnBirthday` | Base game | — |
| `DiaryEventPrompt_ArrivalAnniversary` | `ArrivalAnniversary` | Base game | — |
| `DiaryEventPrompt_BondedDeathAnniversary` | `BondedDeathAnniversary` | Base game | — |
| `DiaryEventPrompt_RecordMilestone` | `RecordMilestone` | Base game | — |
| `DiaryEventPrompt_ArcReflection` | `ArcReflection` | Base game | — |
| `DiaryEventPrompt_BeliefReflection` | `BeliefReflection` | Ideology (`Ludeon.RimWorld.Ideology`) | — |
| `DiaryEventPrompt_ArtImmortalized` | `artImmortalized` | Base game | — |
| `DiaryEventPrompt_Arrival` | `Arrival` | Base game | — |
| `DiaryEventPrompt_Death` | `Death` | Base game | — |
| `DiaryEventPrompt_PersonaWeapon` | `PersonaWeapon` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_RoyalAscent` | `questRoyalAscent` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_PersonaWeaponBondFormed` | `PersonaWeaponBondFormed` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_PersonaWeaponBondSeparated` | `PersonaWeaponBondSeparated` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_PersonaWeaponBondRecovered` | `PersonaWeaponBondRecovered` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_PersonaWeaponBondEnded` | `PersonaWeaponBondEnded` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_PersonaWeaponFirstConsequentialKill` | `PersonaWeaponFirstConsequentialKill` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_RoyalTitleGained` | `RoyalTitleGained` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_RoyalTitlePromoted` | `RoyalTitlePromoted` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_RoyalTitleDemoted` | `RoyalTitleDemoted` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_RoyalTitleLost` | `RoyalTitleLost` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_RoyalSuccession` | `RoyalSuccession` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_RoyalHeirAppointed` | `RoyalHeirAppointed` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_PsylinkLevel` | `PsylinkLevel` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_BestowingCeremony` | `BestowingCeremony` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_AnimaTreeLinking` | `AnimaTreeLinking` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_RoyalPermit` | `RoyalPermit` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_RoyalPermitMilitaryAid` | `RoyalPermitMilitaryAid` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_RoyalPermitTransportShuttle` | `RoyalPermitTransportShuttle` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_RoyalPermitOrbitalStrike` | `RoyalPermitOrbitalStrike` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_RoyalPermitOrbitalSalvo` | `RoyalPermitOrbitalSalvo` | Royalty (`Ludeon.RimWorld.Royalty`) | — |
| `DiaryEventPrompt_AnomalyStudyBreakthrough` | `anomalyStudyBreakthrough` | Anomaly (`Ludeon.RimWorld.Anomaly`) | — |
| `DiaryEventPrompt_AnomalyContainmentBreach` | `anomalyContainmentBreach` | Anomaly (`Ludeon.RimWorld.Anomaly`) | — |
| `DiaryEventPrompt_AnomalyCreepJoinerOutcome` | `anomalyCreepJoinerOutcome` | Anomaly (`Ludeon.RimWorld.Anomaly`) | — |
| `DiaryEventPrompt_AnomalyGhoulTransformation` | `anomalyGhoulTransformation` | Anomaly (`Ludeon.RimWorld.Anomaly`) | — |
| `DiaryEventPrompt_AnomalyVoidOutcome` | `anomalyVoidOutcome` | Anomaly (`Ludeon.RimWorld.Anomaly`) | — |
| `DiaryEventPrompt_RitualAnomalyInvitation` | `ritualAnomalyInvitation` | Anomaly (`Ludeon.RimWorld.Anomaly`) | — |
| `DiaryEventPrompt_RitualAnomalyFleshAndWeather` | `ritualAnomalyFleshAndWeather` | Anomaly (`Ludeon.RimWorld.Anomaly`) | — |
| `DiaryEventPrompt_RitualAnomalyPredation` | `ritualAnomalyPredation` | Anomaly (`Ludeon.RimWorld.Anomaly`) | — |
| `DiaryEventPrompt_RitualAnomalyMind` | `ritualAnomalyMind` | Anomaly (`Ludeon.RimWorld.Anomaly`) | — |
| `DiaryEventPrompt_RitualAnomalyAbduction` | `ritualAnomalyAbduction` | Anomaly (`Ludeon.RimWorld.Anomaly`) | — |
| `DiaryEventPrompt_RitualAnomalyDeathRefusal` | `ritualAnomalyDeathRefusal` | Anomaly (`Ludeon.RimWorld.Anomaly`) | — |
| `DiaryEventPrompt_RitualAnomalyPsychic` | `ritualAnomalyPsychic` | Anomaly (`Ludeon.RimWorld.Anomaly`) | — |
| `DiaryEventPrompt_BiotechFamilyBirth` | `biotechFamilyBirth` | Biotech (`Ludeon.RimWorld.Biotech`) | — |
| `DiaryEventPrompt_BiotechDeathrestInterrupted` | `biotechDeathrestInterrupted` | Biotech (`Ludeon.RimWorld.Biotech`) | — |
| `DiaryEventPrompt_OdysseyGravshipLanding` | `odysseyGravshipLanding` | Odyssey (`Ludeon.RimWorld.Odyssey`) | — |
| `DiaryEventPrompt_RecruitGroup` | `recruit` | Base game | — |
| `DiaryEventPrompt_TrialGroup` | `trial` | Ideology (`Ludeon.RimWorld.Ideology`) | — |
| `DiaryEventPrompt_ConversionGroup` | `conversion` | Ideology (`Ludeon.RimWorld.Ideology`) | — |
| `DiaryEventPrompt_SlaveryGroup` | `slavery` | Ideology (`Ludeon.RimWorld.Ideology`) | — |
| `DiaryEventPrompt_External` | `External` | Base game | — |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Interaction","eventType":"Interaction","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"1dc183a745eb859bae58674b3787467cf93cf9f39fd2fa34cc8c30af1c61a26e"} -->
## Event policy: `DiaryEventPrompt_Interaction` — social interaction

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Interaction`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | — |
| Enhancement | Stay inside the supplied social text; add no unstated history, gestures, or dialogue. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Interaction` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_MentalState","eventType":"MentalState","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"ea51a3bee6b18f7aba7d074e1739fc3d7e158347d24594397c3ecd1386c142bb"} -->
## Event policy: `DiaryEventPrompt_MentalState` — mental state

| Contract | Value |
|---|---|
| Selector/classifier mapping | `MentalState`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a mental-state event as immediate pressure on thought, impulse, and action. |
| Enhancement | Write with pressure and unstable momentum: thought, impulse, body, then action. Stay inside the named state and observed behavior; do not diagnose beyond supplied facts. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_MentalState` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_IdeoChange","eventType":"IdeoChange","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Ideology"],"forcedModel":"","guidanceHash":"865407f8d77391b006cdc0d32dce0480d88058e256eb6cc6b43633a1244865cf"} -->
## Event policy: `DiaryEventPrompt_IdeoChange` — crisis of belief

| Contract | Value |
|---|---|
| Selector/classifier mapping | `IdeoChange`; lookup accepts this selector or the Def name. |
| Availability | Ideology (`Ludeon.RimWorld.Ideology`) |
| Prompt guidance | Write the Crisis of Belief mental state from the breaking pawn's point of view. |
| Enhancement | Use only supplied belief facts. Successful conversion or different previous/current ideoligion proves real change; failure or unchanged identity proves only shaken convictions and falling certainty. Without previous facts, mention only current belief and certainty. Never invent a former ideoligion. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_IdeoChange` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_CounselSuccess","eventType":"Counsel_Success","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Ideology"],"forcedModel":"","guidanceHash":"d773e3262ca1452577c55414d2b2dadfbf5e11d04df41385964b68a76edc83ab"} -->
## Event policy: `DiaryEventPrompt_CounselSuccess` — successful counsel

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Counsel_Success`; lookup accepts this selector or the Def name. |
| Availability | Ideology (`Ludeon.RimWorld.Ideology`) |
| Prompt guidance | Write the successful Counsel interaction from the supplied counselor or listener point of view. |
| Enhancement | Use the supplied roles and the recorded conversation line. Success proves only that the listener's mood was relieved: a painful memory stopped weighing on them, or their mood briefly lifted. Do not invent the memory, dialogue, certainty, doctrine change, or a religious struggle. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_CounselSuccess` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_CounselFailure","eventType":"Counsel_Failure","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Ideology"],"forcedModel":"","guidanceHash":"70bd4f54cbd931c2a4fa5746313ceb4a24c981b6fd2af53755b576e1446d66ed"} -->
## Event policy: `DiaryEventPrompt_CounselFailure` — failed counsel

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Counsel_Failure`; lookup accepts this selector or the Def name. |
| Availability | Ideology (`Ludeon.RimWorld.Ideology`) |
| Prompt guidance | Write the failed Counsel interaction from the supplied counselor or listener point of view. |
| Enhancement | Use the supplied roles and the recorded conversation line. Failure proves only that the attempt went badly and left the listener with a brief negative mood. Do not invent advice, dialogue, certainty, doctrine change, or a religious struggle. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_CounselFailure` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Tale","eventType":"Tale","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"b703bf9eb7ab74f0fa22f1498e925a93d2fcb6e44694de3ad3dc4495cc3c6a92"} -->
## Event policy: `DiaryEventPrompt_Tale` — tale

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Tale`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a notable non-social RimWorld tale as something the pawn lived through or remembers. |
| Enhancement | Find the dramatic pressure in the tale facts: danger, pride, shame, relief, damage, or aftermath. Do not turn the entry into ordinary social chatter. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Tale` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_MoodEvent","eventType":"MoodEvent","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"05de34a3abaf5ca57ff58c2059820e528304d739426fcbd43397c2e642d03629"} -->
## Event policy: `DiaryEventPrompt_MoodEvent` — mood event

| Contract | Value |
|---|---|
| Selector/classifier mapping | `MoodEvent`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a colony-wide mood event through how it touches this pawn. |
| Enhancement | Show the condition pressing on the pawn's senses or nerves rather than explaining game mechanics; keep the condition's scope clear. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_MoodEvent` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Thought","eventType":"Thought","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"dbe4ba92966535fef2d3d9998882fd63e7acc605ec0c834f2c3cbd043d8a5d6a"} -->
## Event policy: `DiaryEventPrompt_Thought` — thought

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Thought`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a temporary thought or mood memory as inner pressure, relief, or irritation. |
| Enhancement | Make the thought bite, comfort, itch, or linger in the pawn's mind. Treat it as internal context, not as a new external event unless the facts say so. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Thought` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Inspiration","eventType":"Inspiration","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"556fc37164e1f3d149cf8671ee2c0bc8238fd9a335e9a31ac2f9340e2096a973"} -->
## Event policy: `DiaryEventPrompt_Inspiration` — inspiration

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Inspiration`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write inspiration as sudden useful clarity catching in the pawn's mind. |
| Enhancement | Make the clarity feel electric and actionable: a sudden plan, urge, or confidence. Focus on what the pawn wants to do next; do not invent success. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Inspiration` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Romance","eventType":"Romance","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"2b26be3bb766d9cf6ece7ac186a73667abcf50f01cce740088fb63dab138fdb5"} -->
## Event policy: `DiaryEventPrompt_Romance` — romance

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Romance`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a relationship milestone as an emotional change between the two people. |
| Enhancement | Make the changed bond feel risky and personal: hope, embarrassment, ache, relief, or dread. Do not backfill a conversation or private scene. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Romance` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Work","eventType":"Work","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"ee287c640d0966f6648fafb72cb645e92e328846f07cec898e76d76cd8dd5eef"} -->
## Event policy: `DiaryEventPrompt_Work` — work

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Work`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write sampled colony work as a concrete moment in the pawn's labor. |
| Enhancement | Use tactile work detail: weight, heat, dirt, rhythm, tools, mistakes, or satisfaction. Let passion, strain, or dread come only from supplied fields. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Work` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Hediff","eventType":"Hediff","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"0a5c219fe7f062b957218a813f1b29a7cbd3f0a0548418a343cd712006e795d0"} -->
## Event policy: `DiaryEventPrompt_Hediff` — health condition

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Hediff`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a health-condition event through the pawn's body and mood. |
| Enhancement | Make the body unavoidable: pain, weakness, breath, fever, hunger, balance, or fear. Do not invent treatment, diagnosis, or recovery. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Hediff` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Raid","eventType":"Raid","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"fe1ea5e6bfef9c24e7bf48ca620c2cc0f31d353be255afe100f12d5b1375f65d"} -->
## Event policy: `DiaryEventPrompt_Raid` — raid

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Raid`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a raid, drop-pod attack, or infestation from the pawn's immediate experience of danger. |
| Enhancement | Use the supplied raid fields, especially arrival mode and strategy. Ordinary raids: warning, preparation, first sight, anticipation. Drop pods or infestations: sudden contact already inside or under the colony. Do not invent battle outcomes. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Raid` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Quest","eventType":"Quest","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"a07e5d0989d6696e7468c9a31990eb12f150d512a253aaa9d64618ab2eb9e7e1"} -->
## Event policy: `DiaryEventPrompt_Quest` — quest

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Quest`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a quest lifecycle event as a colony promise, shared resolution, or shared loss. |
| Enhancement | Make the quest personal without assigning the work to this pawn alone. Use the lifecycle signal and supplied quest facts; frame completed or failed quests as the colony's shared effort, relief, or cost unless facts say otherwise. Do not copy the quest name verbatim or invent rewards or blame. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Quest` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RitualConversion","eventType":"ritualConversion","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Ideology"],"forcedModel":"","guidanceHash":"b0089929b25a7e25b169191f1fe97f5c087ff7119da36f2fc55a69b3d6221597"} -->
## Event policy: `DiaryEventPrompt_RitualConversion` — conversion ritual

| Contract | Value |
|---|---|
| Selector/classifier mapping | `ritualConversion`; lookup accepts this selector or the Def name. |
| Availability | Ideology (`Ludeon.RimWorld.Ideology`) |
| Prompt guidance | Write a completed conversion ritual from this pawn's assigned perspective. |
| Enhancement | Use only the supplied converter, convertee, participant, spectator, belief, certainty, and verified result fields. A quality label may shape ceremony and emotion but never proves conversion. Do not give the convertee's before-and-after belief facts to another pawn. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RitualConversion` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Ritual","eventType":"Ritual","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"01c15379a24a193192ddcce51c33524fc377e6f1f2fd7d588f80a94036b850fc"} -->
## Event policy: `DiaryEventPrompt_Ritual` — ritual

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Ritual`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a finished Ideology ritual from the pawn's assigned place in it. |
| Enhancement | Use the supplied ritual role, title, behavior, quality, and status fields. Quality shapes confidence and emotional weight, but never name or explain the quality label. Stay in what completion feels like from that role; do not invent ritual effects beyond the supplied outcome. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Ritual` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Ability","eventType":"Ability","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"e95f22c02e3dccc4dceb25ae49a2693e294463d478cbba2dfe0fd76fe0838280"} -->
## Event policy: `DiaryEventPrompt_Ability` — ability

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Ability`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a successful pawn ability use as a concrete moment of focus, risk, or exertion. |
| Enhancement | Use the supplied ability name, category, and target. Write the moment of use — the effort it took, where it was aimed, what it cost right then — as lived sensation, not a technique explained; do not invent extra effects or outcomes. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Ability` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_DayReflection","eventType":"DayReflection","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"a7808f68e1614485b9b35040d4f98e7c0f4d6c8e050983a9229609b65e55022b"} -->
## Event policy: `DiaryEventPrompt_DayReflection` — day reflection

| Contract | Value |
|---|---|
| Selector/classifier mapping | `DayReflection`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write an end-of-day reflection that weighs the day as a whole. |
| Enhancement | Choose the supplied moments that still sting, glow, or weigh on the pawn tonight instead of listing every event. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_DayReflection` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_QuadrumReflection","eventType":"QuadrumReflection","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"7334acbc91c2b87560e47f4d8a7e4f10fe5ddd9a96e3808d0a119de12fc143e3"} -->
## Event policy: `DiaryEventPrompt_QuadrumReflection` — quadrum reflection

| Contract | Value |
|---|---|
| Selector/classifier mapping | `QuadrumReflection`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a rare long reflection over a whole quadrum of the pawn's life. |
| Enhancement | Use the dated highlights as anchors across time. Mention dates naturally, connect several events into a broader life reflection, and do not list every highlight mechanically. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_QuadrumReflection` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Progression","eventType":"Progression","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"a00e12cbe3dfb22e9f1db0580e2019967a24b24e68b5e9ef0b363d32b0f87e40"} -->
## Event policy: `DiaryEventPrompt_Progression` — progression

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Progression`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a pawn progression moment as a concrete private diary entry about change, effort, status, or identity. |
| Enhancement | Use the supplied before-and-after fields exactly. Do not explain game mechanics, invent training scenes, or add powers, titles, body changes, or achievements that were not supplied. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Progression` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_PawnBirthday","eventType":"PawnBirthday","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"2241997eaa3565127fc8afe06bed36e09c4824a916fa50c9d3b5d4095aa5b0c9"} -->
## Event policy: `DiaryEventPrompt_PawnBirthday` — birthday

| Contract | Value |
|---|---|
| Selector/classifier mapping | `PawnBirthday`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write another year of age as a quiet private life-marker. |
| Enhancement | Use the supplied age exactly. Do not invent a party, a gift, a well-wisher, or anyone else noticing the date, and do not state the pawn's birth date or year. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_PawnBirthday` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_ArrivalAnniversary","eventType":"ArrivalAnniversary","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"94a5ac1046efba765556541f67d3a56424b297e048a2ae5e63d362d58f284899"} -->
## Event policy: `DiaryEventPrompt_ArrivalAnniversary` — colony anniversary

| Contract | Value |
|---|---|
| Selector/classifier mapping | `ArrivalAnniversary`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a whole number of years spent in this colony as private stock-taking. |
| Enhancement | Use the supplied number of years exactly. Do not invent the circumstances of the arrival, a ceremony, or anyone else marking the date, and do not summarize colony history that was not supplied. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_ArrivalAnniversary` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_BondedDeathAnniversary","eventType":"BondedDeathAnniversary","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"06344f2b2395d70ca075141ced8c088d961d8b06e31078b000c3af39d87ec1f9"} -->
## Event policy: `DiaryEventPrompt_BondedDeathAnniversary` — remembered loss

| Contract | Value |
|---|---|
| Selector/classifier mapping | `BondedDeathAnniversary`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write remembrance on the anniversary of losing someone close. |
| Enhancement | Keep every supplied name, relation, and number of years exactly. Do not retell how they died, invent last words, a grave, a gathering, or anyone else remembering with this pawn, and do not claim contact with the dead. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_BondedDeathAnniversary` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RecordMilestone","eventType":"RecordMilestone","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"1164c2b2d987fff0593b0581edc97e69af43b8a4ecd5ca69994690703eb99022"} -->
## Event policy: `DiaryEventPrompt_RecordMilestone` — personal record

| Contract | Value |
|---|---|
| Selector/classifier mapping | `RecordMilestone`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write what doing this much of one thing has made of the pawn. |
| Enhancement | Use the supplied tally name and total exactly. Do not present it as a score, achievement, rank, or comparison with anyone else, and do not invent specific past instances that make up the total. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RecordMilestone` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_ArcReflection","eventType":"ArcReflection","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"65227780f74c65f14be6e62f186e10ce991512cbb53e1364fbb4c31a36faf461"} -->
## Event policy: `DiaryEventPrompt_ArcReflection` — arc reflection

| Contract | Value |
|---|---|
| Selector/classifier mapping | `ArcReflection`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a rare life-arc diary entry about who the pawn is becoming across the selected memories. |
| Enhancement | Use the selected memories as anchors, not as a checklist. Connect only a few memories into a private sense of change, regret, pride, fear, or resolve. Do not mention every memory. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_ArcReflection` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_BeliefReflection","eventType":"BeliefReflection","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Ideology"],"forcedModel":"","guidanceHash":"5d21052ff413ff9bfb82b8e046ee6d2644139a92ef6d059934971d5df0a26f5b"} -->
## Event policy: `DiaryEventPrompt_BeliefReflection` — belief reflection

| Contract | Value |
|---|---|
| Selector/classifier mapping | `BeliefReflection`; lookup accepts this selector or the Def name. |
| Availability | Ideology (`Ludeon.RimWorld.Ideology`) |
| Prompt guidance | Write a private reflection on the supplied belief experience, accumulated certainty change, or change of faith. |
| Enhancement | Stay inside the frozen belief context and exact trigger. Do not invent doctrine, commandments, rituals, memories, a former ideoligion, or a conversion that the supplied facts do not prove. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_BeliefReflection` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_ArtImmortalized","eventType":"artImmortalized","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"2359d890a3fa103b95105859e822c870840e0097da405842b5a9346d505c914c"} -->
## Event policy: `DiaryEventPrompt_ArtImmortalized` — immortalized in art

| Contract | Value |
|---|---|
| Selector/classifier mapping | `artImmortalized`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write about meeting a piece of your own past as an object someone else made. |
| Enhancement | Stay with the artwork in front of you and how it sits with you now. Do not retell the deed it depicts, and do not claim what the artist meant or what anyone else thinks of it. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_ArtImmortalized` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Arrival","eventType":"Arrival","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"4017a6882af6b8350c0cb373a19a1d12eebf9611cd059f7080321042f1211aa2"} -->
## Event policy: `DiaryEventPrompt_Arrival` — arrival

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Arrival`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a factual colony-arrival note about how this pawn joined or began here. |
| Enhancement | Make the arrival feel like a threshold: dust, shock, silence, relief, suspicion, or first sight of shelter. Use only scenario and arrival facts. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Arrival` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_Death","eventType":"Death","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"b1f442887b6dd96d72a8b0486b954b9b84293ebc22a66365bad1ade64a7492ab"} -->
## Event policy: `DiaryEventPrompt_Death` — death

| Contract | Value |
|---|---|
| Selector/classifier mapping | `Death`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a factual death note about how the colonist died. |
| Enhancement | Make the death note stark and specific, with one concrete cause or setting detail. Use only supplied cause, body-part, weapon, condition, and setting facts; keep it brief. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_Death` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_PersonaWeapon","eventType":"PersonaWeapon","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"4369450e817c6f906b04b68059498de82c961c22d365710da5fd72025ac45a95"} -->
## Event policy: `DiaryEventPrompt_PersonaWeapon` — persona weapon bond

| Contract | Value |
|---|---|
| Selector/classifier mapping | `PersonaWeapon`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write an exact persona-weapon bond lifecycle change from the bonded pawn's point of view. |
| Enhancement | Use only the supplied weapon, lifecycle states, duration, prior bond, ending cause, and selected trait facts. Treat the weapon as present and uncanny without inventing dialogue, conscious motives, combat, or a death. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_PersonaWeapon` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RoyalAscent","eventType":"questRoyalAscent","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"134f13a5ff6648d079528d9288a59fc673f61984e3ff2348431425fa7b05d5a2"} -->
## Event policy: `DiaryEventPrompt_RoyalAscent` — Royal Ascent chapter

| Contract | Value |
|---|---|
| Selector/classifier mapping | `questRoyalAscent`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the exact Royal Ascent lifecycle edge as one colony chapter witnessed by this pawn. |
| Enhancement | Use quest_signal as the ownership boundary: accepted proves commitment and preparation only; completed or failed proves only the hosting quest's terminal outcome. Never claim the Stellarch arrived, name a failure cause, say who boarded or escaped, or invent ceremony, rewards, or blame. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RoyalAscent` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_PersonaWeaponBondFormed","eventType":"PersonaWeaponBondFormed","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"b0c658938ca47e65abffb63ce894a92feb4971dbb116b4ff1631b4c03cc8aeed"} -->
## Event policy: `DiaryEventPrompt_PersonaWeaponBondFormed` — persona weapon bond formed

| Contract | Value |
|---|---|
| Selector/classifier mapping | `PersonaWeaponBondFormed`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the moment an exact persona-weapon bond formed. |
| Enhancement | Center first contact, recognition, obligation, or unease grounded in the supplied weapon and traits. If an exact previous bonded pawn is supplied, acknowledge the transfer without inventing how it happened or what the weapon said. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_PersonaWeaponBondFormed` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_PersonaWeaponBondSeparated","eventType":"PersonaWeaponBondSeparated","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"4e7d42f6186d4740225eddb43523520872ca397b1ed9e547713a63dfe24f24a5"} -->
## Event policy: `DiaryEventPrompt_PersonaWeaponBondSeparated` — persona weapon bond separated

| Contract | Value |
|---|---|
| Selector/classifier mapping | `PersonaWeaponBondSeparated`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write a persona-weapon bond's first meaningful period of separation. |
| Enhancement | Use the supplied separation duration and state change as absence, friction, jealousy, relief, or unease. This is not proof of destruction, abandonment, transfer, combat, or death. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_PersonaWeaponBondSeparated` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_PersonaWeaponBondRecovered","eventType":"PersonaWeaponBondRecovered","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"6d7328b00634694abda911a5d47d0a9f1ff45862f7f07a0a84b8cce2d561383c"} -->
## Event policy: `DiaryEventPrompt_PersonaWeaponBondRecovered` — persona weapon bond recovered

| Contract | Value |
|---|---|
| Selector/classifier mapping | `PersonaWeaponBondRecovered`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the exact bonded pawn wielding a persona weapon again after a recorded separation. |
| Enhancement | Make return and recognition concrete through the supplied duration, weapon, and trait facts. Do not invent apology, dialogue, a fight, or what happened while they were apart. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_PersonaWeaponBondRecovered` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_PersonaWeaponBondEnded","eventType":"PersonaWeaponBondEnded","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"5a694cb33ce97854a841cf6b1b10d008b637a40a123d28ac53c2c5db3420d8f0"} -->
## Event policy: `DiaryEventPrompt_PersonaWeaponBondEnded` — persona weapon bond ended

| Contract | Value |
|---|---|
| Selector/classifier mapping | `PersonaWeaponBondEnded`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the standalone ending of an exact persona-weapon bond. |
| Enhancement | Use the supplied ending cause and bond duration to make finality specific. Do not invent a death, attacker, battle, transfer, or last words beyond the exact facts. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_PersonaWeaponBondEnded` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_PersonaWeaponFirstConsequentialKill","eventType":"PersonaWeaponFirstConsequentialKill","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"26e2a828c8ac68372a844a4176ef9765c6fc1dd49aa046c79aa8b4045884e858"} -->
## Event policy: `DiaryEventPrompt_PersonaWeaponFirstConsequentialKill` — first consequential persona-weapon kill

| Contract | Value |
|---|---|
| Selector/classifier mapping | `PersonaWeaponFirstConsequentialKill`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the exact bonded wielder's first consequential kill with this persona weapon. |
| Enhancement | Use only the supplied victim, source Tale, weapon, bond state, and selected trait facts. Keep the killer as the sole point of view; do not invent dialogue, motives for the weapon, another attack, or the victim's inner experience. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_PersonaWeaponFirstConsequentialKill` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RoyalTitleGained","eventType":"RoyalTitleGained","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"7f3899df8461ae47cb9d9ec7660beafbf5cd090ca64eb0d38dabcbddee2546f1"} -->
## Event policy: `DiaryEventPrompt_RoyalTitleGained` — first royal title

| Contract | Value |
|---|---|
| Selector/classifier mapping | `RoyalTitleGained`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the pawn receiving their first exact title in the supplied royal faction. |
| Enhancement | Preserve the faction, new title, and cause exactly. Show first rank as recognition, obligation, pride, or unease; do not invent a ceremony, duties, or faction mechanics. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RoyalTitleGained` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RoyalTitlePromoted","eventType":"RoyalTitlePromoted","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"7fd72a1049dd0e164b112726889d944b3776cce0390a3594bfb4ef4e14424a28"} -->
## Event policy: `DiaryEventPrompt_RoyalTitlePromoted` — royal promotion

| Contract | Value |
|---|---|
| Selector/classifier mapping | `RoyalTitlePromoted`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write an exact promotion from the supplied previous title to the new title in one faction. |
| Enhancement | Keep both titles and the faction correct. Optional supplied duty changes may color the reaction, but do not list mechanics or invent a bestowing ceremony. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RoyalTitlePromoted` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RoyalTitleDemoted","eventType":"RoyalTitleDemoted","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"48c1411e7fcf42f1b82e75bc347d67123f38416d1337cd282ec77a480f22c8bf"} -->
## Event policy: `DiaryEventPrompt_RoyalTitleDemoted` — royal demotion

| Contract | Value |
|---|---|
| Selector/classifier mapping | `RoyalTitleDemoted`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write an exact demotion from the supplied previous title to the lower title in one faction. |
| Enhancement | Keep both titles and the faction correct. Center loss of standing, relief, shame, anger, or changed responsibility without inventing blame or political events. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RoyalTitleDemoted` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RoyalTitleLost","eventType":"RoyalTitleLost","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"f5645aab1419b39d5828a925bd8fb923cf2c8e09ba7d53fd02b67f9882ad55e8"} -->
## Event policy: `DiaryEventPrompt_RoyalTitleLost` — royal title lost

| Contract | Value |
|---|---|
| Selector/classifier mapping | `RoyalTitleLost`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the complete loss of the supplied previous royal title in its exact faction. |
| Enhancement | Treat the new title value none as complete loss, not promotion or transfer. Use only the supplied cause; do not invent death, succession, punishment, or a replacement title. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RoyalTitleLost` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RoyalSuccession","eventType":"RoyalSuccession","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"91cf6bf4ac29603e1d00568a4c3e2736a16a990374fcea02797e741399d8211c"} -->
## Event policy: `DiaryEventPrompt_RoyalSuccession` — royal succession

| Contract | Value |
|---|---|
| Selector/classifier mapping | `RoyalSuccession`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the heir inheriting the supplied exact royal title after the named title holder's death. |
| Enhancement | Keep the heir as the sole point of view and preserve the deceased holder, title, and faction exactly. Treat this as succession, not an ordinary promotion or bestowing ceremony; do not invent last words, politics, rival claimants, or ceremony details. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RoyalSuccession` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RoyalHeirAppointed","eventType":"RoyalHeirAppointed","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"04b580554470061898ee657eca20cc74671423c55d912d0c3e29656614f9a405"} -->
## Event policy: `DiaryEventPrompt_RoyalHeirAppointed` — royal heir appointed

| Contract | Value |
|---|---|
| Selector/classifier mapping | `RoyalHeirAppointed`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the named heir's explicit appointment to inherit the supplied title. |
| Enhancement | Keep the appointed heir as the sole point of view and preserve the title holder, title, and faction exactly. This is an appointment, not a death or completed inheritance; do not invent a ceremony, succession, or political dispute. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RoyalHeirAppointed` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_PsylinkLevel","eventType":"PsylinkLevel","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"a0324fa4e213ebbef831510cfd76b15b0e75598b97e9d3b73cd06e1e877651a6"} -->
## Event policy: `DiaryEventPrompt_PsylinkLevel` — psylink gain

| Contract | Value |
|---|---|
| Selector/classifier mapping | `PsylinkLevel`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the exact supplied psylink level reached and its known or unknown cause. |
| Enhancement | Keep the exact level stated in the event line and the supplied cause; never invent a previous level or the size of the jump. Write lived psychic sensation, responsibility, or unease without naming unprovided powers or explaining implant and ritual mechanics. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_PsylinkLevel` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_BestowingCeremony","eventType":"BestowingCeremony","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"6a56b48959243b9b03875f80dd8c5a1d895a786f3135df2ba19dc876ee4ad180"} -->
## Event policy: `DiaryEventPrompt_BestowingCeremony` — royal bestowing ceremony

| Contract | Value |
|---|---|
| Selector/classifier mapping | `BestowingCeremony`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the completed imperial bestowing ceremony from the supplied ritual role. |
| Enhancement | Use the exact mutation pawn, faction, title before/after, and imperial_bestowing cause when present. Any psychic change is felt, never numbered. Keep all changes in this ritual page; do not invent honors, speeches, powers, or another progression scene. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_BestowingCeremony` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_AnimaTreeLinking","eventType":"AnimaTreeLinking","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"93203565cc5d0f615f7c683fdb2ae64392d31425dbbdb3a52bcb7148d7d5fcfb"} -->
## Event policy: `DiaryEventPrompt_AnimaTreeLinking` — anima linking ceremony

| Contract | Value |
|---|---|
| Selector/classifier mapping | `AnimaTreeLinking`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the completed anima linking ceremony from the supplied ritual role. |
| Enhancement | Use the supplied anima_linking cause when present, and write the deepening link as felt, never as a level. Keep the psychic change in this ritual page and do not invent powers, imperial rank, or extra ritual effects. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_AnimaTreeLinking` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RoyalPermit","eventType":"RoyalPermit","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"034663a8fabb571ba2915fd23fa72d0f9262d42ab254ac838947d8f5a1abcd6b"} -->
## Event policy: `DiaryEventPrompt_RoyalPermit` — dramatic royal permit

| Contract | Value |
|---|---|
| Selector/classifier mapping | `RoyalPermit`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write an exact successful dramatic royal-permit use from the permit owner's point of view. |
| Enhancement | Preserve the supplied permit, faction, title, setting, and cooldown-use fact. Describe invoking authority, not an unobserved result; invent no target, arrival, impact, completion, dialogue, or favor amount. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RoyalPermit` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RoyalPermitMilitaryAid","eventType":"RoyalPermitMilitaryAid","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"b0657a51bd868de9fc1a19b9ca2977a2aa887d1e6f9ae2d6e123dfc08508960b"} -->
## Event policy: `DiaryEventPrompt_RoyalPermitMilitaryAid` — royal military aid called

| Contract | Value |
|---|---|
| Selector/classifier mapping | `RoyalPermitMilitaryAid`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the permit owner calling in military aid from the supplied royal faction. |
| Enhancement | Center the decision to invoke aid and the weight of rank behind it. The successful permit use proves the call, not that troops arrived or won; invent no force size, route, target, battle, or favor amount. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RoyalPermitMilitaryAid` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RoyalPermitTransportShuttle","eventType":"RoyalPermitTransportShuttle","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"144655a85c07fa6b895ca8208915dc529765f6f14a41b653b04716d227d1300d"} -->
## Event policy: `DiaryEventPrompt_RoyalPermitTransportShuttle` — royal transport shuttle called

| Contract | Value |
|---|---|
| Selector/classifier mapping | `RoyalPermitTransportShuttle`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the permit owner calling for a royal transport shuttle. |
| Enhancement | Use only the supplied permit, faction, title, and setting. Do not invent passengers, cargo, destination, flight events, arrival, completion, dialogue, or a favor amount. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RoyalPermitTransportShuttle` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RoyalPermitOrbitalStrike","eventType":"RoyalPermitOrbitalStrike","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"c0d830494f40f73d9d20f021e1e7908dc427578ae4c7123190e4e0b378420cae"} -->
## Event policy: `DiaryEventPrompt_RoyalPermitOrbitalStrike` — royal orbital strike invoked

| Contract | Value |
|---|---|
| Selector/classifier mapping | `RoyalPermitOrbitalStrike`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the permit owner exercising an orbital-strike permit. |
| Enhancement | Center the decision, authority, and anticipation at the supplied setting. Do not invent a target, impact, casualties, damage, accuracy, completion, dialogue, or a favor amount. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RoyalPermitOrbitalStrike` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RoyalPermitOrbitalSalvo","eventType":"RoyalPermitOrbitalSalvo","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"forcedModel":"","guidanceHash":"ff7eb439a0c452d20b4a2661ea9372de4c3a1b9d0dbdbfda5b15a29edfd9a859"} -->
## Event policy: `DiaryEventPrompt_RoyalPermitOrbitalSalvo` — royal orbital salvo invoked

| Contract | Value |
|---|---|
| Selector/classifier mapping | `RoyalPermitOrbitalSalvo`; lookup accepts this selector or the Def name. |
| Availability | Royalty (`Ludeon.RimWorld.Royalty`) |
| Prompt guidance | Write the permit owner exercising an orbital-salvo permit. |
| Enhancement | Center the decision, authority, and anticipation at the supplied setting. Do not invent a target, impacts, casualties, damage, accuracy, completion, dialogue, or a favor amount. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RoyalPermitOrbitalSalvo` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_AnomalyStudyBreakthrough","eventType":"anomalyStudyBreakthrough","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"forcedModel":"","guidanceHash":"e521293999a107731ad2ecdd6073e212b2054265bbb7254a2d4ca6aa35d32293"} -->
## Event policy: `DiaryEventPrompt_AnomalyStudyBreakthrough` — Anomaly study breakthrough

| Contract | Value |
|---|---|
| Selector/classifier mapping | `anomalyStudyBreakthrough`; lookup accepts this selector or the Def name. |
| Availability | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Prompt guidance | Write the exact visible Anomaly study breakthrough reached by this researcher. |
| Enhancement | Preserve the supplied subject and milestone. Do not invent hidden research, abilities, codex text, or later consequences. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_AnomalyStudyBreakthrough` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_AnomalyContainmentBreach","eventType":"anomalyContainmentBreach","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"forcedModel":"","guidanceHash":"2cf67de8f4a491c0b44c54641858c8a3a896d55294f5bcfbb3d2725b892c7820"} -->
## Event policy: `DiaryEventPrompt_AnomalyContainmentBreach` — Anomaly containment breach

| Contract | Value |
|---|---|
| Selector/classifier mapping | `anomalyContainmentBreach`; lookup accepts this selector or the Def name. |
| Availability | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Prompt guidance | Write the exact containment breach from the supplied witness role. |
| Enhancement | Name only escaped entities and visible facts supplied. Do not assign blame, cause, hidden powers, or an outcome after the breach. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_AnomalyContainmentBreach` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_AnomalyCreepJoinerOutcome","eventType":"anomalyCreepJoinerOutcome","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"forcedModel":"","guidanceHash":"1f221c3a4764b2b08d4d938a37f818dad09ed45f7c1a77022caa527435d80a0b"} -->
## Event policy: `DiaryEventPrompt_AnomalyCreepJoinerOutcome` — strange-arrival outcome

| Contract | Value |
|---|---|
| Selector/classifier mapping | `anomalyCreepJoinerOutcome`; lookup accepts this selector or the Def name. |
| Availability | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Prompt guidance | Write the strange arrival's exact visible outcome. |
| Enhancement | Keep the supplied role and visible result. Do not reveal hidden motives, drawbacks, infections, timers, or discoveries not stated. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_AnomalyCreepJoinerOutcome` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_AnomalyGhoulTransformation","eventType":"anomalyGhoulTransformation","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"forcedModel":"","guidanceHash":"518c30033d42a0901154c5e98bd18e9aac3b01391f391934f011465ded48eef5"} -->
## Event policy: `DiaryEventPrompt_AnomalyGhoulTransformation` — ghoul transformation

| Contract | Value |
|---|---|
| Selector/classifier mapping | `anomalyGhoulTransformation`; lookup accepts this selector or the Def name. |
| Availability | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Prompt guidance | Write the completed ghoul transformation from the supplied subject or surgeon point of view. |
| Enhancement | Keep participant roles and visible results exact. Do not invent procedure mechanics, a cure, later abilities, or moral judgment. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_AnomalyGhoulTransformation` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_AnomalyVoidOutcome","eventType":"anomalyVoidOutcome","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"forcedModel":"","guidanceHash":"1d05ba1ecbc936e6573427b81e283ae2a763a39c4cd0542bc8130c5db91eddba"} -->
## Event policy: `DiaryEventPrompt_AnomalyVoidOutcome` — answer to the void

| Contract | Value |
|---|---|
| Selector/classifier mapping | `anomalyVoidOutcome`; lookup accepts this selector or the Def name. |
| Availability | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Prompt guidance | Write the exact terminal answer this pawn committed before the void. |
| Enhancement | Preserve the supplied branch and visible result. Do not merge alternatives or invent dialogue, mechanics, consequences, or another pawn's agency. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_AnomalyVoidOutcome` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RitualAnomalyInvitation","eventType":"ritualAnomalyInvitation","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"forcedModel":"","guidanceHash":"e366d24ab4193ae4d56a1903ffd8a933ac7cc62ebca40c1eabccd64be8ffbfbd"} -->
## Event policy: `DiaryEventPrompt_RitualAnomalyInvitation` — Anomaly invitation ritual

| Contract | Value |
|---|---|
| Selector/classifier mapping | `ritualAnomalyInvitation`; lookup accepts this selector or the Def name. |
| Availability | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Prompt guidance | Write the supplied invitation ritual or its recorded outcome as one witnessed occult event. |
| Enhancement | Use only the ritual, role, quality, participants, and outcome supplied. Do not invent what was invited, what answered, dialogue, or later effects. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RitualAnomalyInvitation` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RitualAnomalyFleshAndWeather","eventType":"ritualAnomalyFleshAndWeather","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"forcedModel":"","guidanceHash":"8a7dd3d948b01f257b2e14f9b789ddc2496554d1525b581e0fa3833aab22b208"} -->
## Event policy: `DiaryEventPrompt_RitualAnomalyFleshAndWeather` — Anomaly flesh or weather ritual

| Contract | Value |
|---|---|
| Selector/classifier mapping | `ritualAnomalyFleshAndWeather`; lookup accepts this selector or the Def name. |
| Availability | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Prompt guidance | Write the supplied flesh-shaping or unnatural-weather ritual and its visible result. |
| Enhancement | Keep exact roles, quality, and outcome. Do not invent bodily changes, weather damage, hidden causes, or later consequences. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RitualAnomalyFleshAndWeather` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RitualAnomalyPredation","eventType":"ritualAnomalyPredation","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"forcedModel":"","guidanceHash":"f52b95436afcf98d19f49fb21770f68c7bfcfafbf825d61632639e994035f104"} -->
## Event policy: `DiaryEventPrompt_RitualAnomalyPredation` — Anomaly predation ritual

| Contract | Value |
|---|---|
| Selector/classifier mapping | `ritualAnomalyPredation`; lookup accepts this selector or the Def name. |
| Availability | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Prompt guidance | Write the supplied predatory ritual from this pawn's exact participant or witness role. |
| Enhancement | Use only named targets, quality, and visible outcome. Do not invent pursuit, wounds, deaths, powers, or what happened afterward. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RitualAnomalyPredation` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RitualAnomalyMind","eventType":"ritualAnomalyMind","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"forcedModel":"","guidanceHash":"73550db6b795b37abf1601e118fe49912264e01ec2e663c7f34c6b5750599ce1"} -->
## Event policy: `DiaryEventPrompt_RitualAnomalyMind` — Anomaly mind ritual

| Contract | Value |
|---|---|
| Selector/classifier mapping | `ritualAnomalyMind`; lookup accepts this selector or the Def name. |
| Availability | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Prompt guidance | Write the supplied mind-affecting ritual as immediate psychic pressure and its recorded result. |
| Enhancement | Keep roles, quality, and outcome exact. Do not invent private thoughts, lasting damage, secret knowledge, or additional victims. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RitualAnomalyMind` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RitualAnomalyAbduction","eventType":"ritualAnomalyAbduction","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"forcedModel":"","guidanceHash":"6c718baad554b52ab4daace29cb4e87d499dc046890974ad57c9a7a22b301e58"} -->
## Event policy: `DiaryEventPrompt_RitualAnomalyAbduction` — Anomaly abduction ritual

| Contract | Value |
|---|---|
| Selector/classifier mapping | `ritualAnomalyAbduction`; lookup accepts this selector or the Def name. |
| Availability | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Prompt guidance | Write the supplied abduction ritual and only the arrival, disappearance, or outcome it visibly proved. |
| Enhancement | Preserve exact actors, targets, quality, and result. Do not invent a destination, captivity, rescue, dialogue, or events beyond the record. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RitualAnomalyAbduction` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RitualAnomalyDeathRefusal","eventType":"ritualAnomalyDeathRefusal","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"forcedModel":"","guidanceHash":"ca2fa639a60e2c66a8c39adcd3dc9ee66a96a8bf72d4f103973bfadba29d6996"} -->
## Event policy: `DiaryEventPrompt_RitualAnomalyDeathRefusal` — Anomaly death-refusal ritual

| Contract | Value |
|---|---|
| Selector/classifier mapping | `ritualAnomalyDeathRefusal`; lookup accepts this selector or the Def name. |
| Availability | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Prompt guidance | Write the supplied ritual that contests death and its exact recorded outcome. |
| Enhancement | Keep the named subject, roles, quality, and visible result. Do not invent resurrection details, hidden costs, dialogue, or later survival. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RitualAnomalyDeathRefusal` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RitualAnomalyPsychic","eventType":"ritualAnomalyPsychic","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"forcedModel":"","guidanceHash":"5c79a0296af424e8cdd9dd252bdf53362bd0acfa514b60c4c4989307620d41ec"} -->
## Event policy: `DiaryEventPrompt_RitualAnomalyPsychic` — Anomaly psychic ritual

| Contract | Value |
|---|---|
| Selector/classifier mapping | `ritualAnomalyPsychic`; lookup accepts this selector or the Def name. |
| Availability | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Prompt guidance | Write the supplied psychic ritual as a witnessed act with its exact recorded result. |
| Enhancement | Use only participants, quality, target, and outcome supplied. Do not invent thoughts, powers, voices, hidden knowledge, or later effects. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RitualAnomalyPsychic` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_BiotechFamilyBirth","eventType":"biotechFamilyBirth","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Biotech"],"forcedModel":"","guidanceHash":"b092163f69b83447a33f5ac0ca16898f6a129a6a440b2a7be734cfa92960d185"} -->
## Event policy: `DiaryEventPrompt_BiotechFamilyBirth` — family birth

| Contract | Value |
|---|---|
| Selector/classifier mapping | `biotechFamilyBirth`; lookup accepts this selector or the Def name. |
| Availability | Biotech (`Ludeon.RimWorld.Biotech`) |
| Prompt guidance | Write the supplied birth as a family threshold from this pawn's exact role. |
| Enhancement | Use only parent, baby, setting, and outcome facts supplied. Do not invent labor details, health, genes, names, dialogue, or future bonds. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_BiotechFamilyBirth` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_BiotechDeathrestInterrupted","eventType":"biotechDeathrestInterrupted","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Biotech"],"forcedModel":"","guidanceHash":"b67a39a0867f3933a6bbdd45f3b044281cca263f9ce9dc9eba5f0c1a6c922217"} -->
## Event policy: `DiaryEventPrompt_BiotechDeathrestInterrupted` — interrupted deathrest

| Contract | Value |
|---|---|
| Selector/classifier mapping | `biotechDeathrestInterrupted`; lookup accepts this selector or the Def name. |
| Availability | Biotech (`Ludeon.RimWorld.Biotech`) |
| Prompt guidance | Write the confirmed interruption of active deathrest as incomplete waking and bodily consequence. |
| Enhancement | Use only the supplied state and setting. Do not invent a cause, culprit, routine completion, dialogue, or additional injury. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_BiotechDeathrestInterrupted` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_OdysseyGravshipLanding","eventType":"odysseyGravshipLanding","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Odyssey"],"forcedModel":"","guidanceHash":"46a9853ee26d56b80963728e9514217b4acf7c3d7b147ee67e9c4773404b0769"} -->
## Event policy: `DiaryEventPrompt_OdysseyGravshipLanding` — gravship landing

| Contract | Value |
|---|---|
| Selector/classifier mapping | `odysseyGravshipLanding`; lookup accepts this selector or the Def name. |
| Availability | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| Prompt guidance | Write the supplied gravship landing as the end of a journey and the first sight of a place. |
| Enhancement | Preserve exact ship, place, role, and visible result. Do not invent the route, hazards, passengers, damage, discoveries, or what follows. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_OdysseyGravshipLanding` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_RecruitGroup","eventType":"recruit","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"b08a5057334076df3b65fd8db3a092613c7b6ddbf0eeb768c5ed72bffd19e7f3"} -->
## Event policy: `DiaryEventPrompt_RecruitGroup` — recruitment or captivity

| Contract | Value |
|---|---|
| Selector/classifier mapping | `recruit`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write the exact recruitment, resistance, prison, or release interaction supplied. |
| Enhancement | Keep recruiter, captive, outcome, and social text exact. Do not invent coercion, consent, promises, escape plans, or a later allegiance change. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_RecruitGroup` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_TrialGroup","eventType":"trial","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Ideology"],"forcedModel":"","guidanceHash":"4b48161d95d88469e374cae271f5bdabb6340b6fd9a45ec4fa4aa77cc2098880"} -->
## Event policy: `DiaryEventPrompt_TrialGroup` — trial or accusation

| Contract | Value |
|---|---|
| Selector/classifier mapping | `trial`; lookup accepts this selector or the Def name. |
| Availability | Ideology (`Ludeon.RimWorld.Ideology`) |
| Prompt guidance | Write the supplied accusation, defense, or judgment from this pawn's exact role. |
| Enhancement | Use only the charge, participants, social text, and recorded outcome. Do not invent evidence, testimony, guilt, sentence, or crowd reaction. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_TrialGroup` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_ConversionGroup","eventType":"conversion","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Ideology"],"forcedModel":"","guidanceHash":"4c26ae5f2c3219405465faf99b105c3a92eddfa5c3a3405f92ec8f6d4abab8b6"} -->
## Event policy: `DiaryEventPrompt_ConversionGroup` — conversion or preaching

| Contract | Value |
|---|---|
| Selector/classifier mapping | `conversion`; lookup accepts this selector or the Def name. |
| Availability | Ideology (`Ludeon.RimWorld.Ideology`) |
| Prompt guidance | Write the supplied conversion or preaching interaction as pressure between stated beliefs. |
| Enhancement | Preserve speaker, listener, social text, certainty, and outcome supplied. Do not invent doctrine, arguments, a former faith, or conversion without proof. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_ConversionGroup` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_SlaveryGroup","eventType":"slavery","enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Ideology"],"forcedModel":"","guidanceHash":"21673ddaa1b9b13d18d1887523e702217fc31c89e2dc660559aad8eb129fd7ae"} -->
## Event policy: `DiaryEventPrompt_SlaveryGroup` — slavery or suppression

| Contract | Value |
|---|---|
| Selector/classifier mapping | `slavery`; lookup accepts this selector or the Def name. |
| Availability | Ideology (`Ludeon.RimWorld.Ideology`) |
| Prompt guidance | Write the supplied enslavement, suppression, or defiance interaction from this pawn's exact role. |
| Enhancement | Use only status, participants, social text, and result supplied. Do not invent abuse, consent, rebellion plans, escape, emancipation, or later punishment. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_SlaveryGroup` |

<!-- repowiki:event-policy {"defName":"DiaryEventPrompt_External","eventType":"External","enableWhenPackageIdsLoaded":[],"forcedModel":"","guidanceHash":"9b32a382e9d5df5aabc55d516082b60c060fc730a4e4ffb6e0d991ff7ad0951e"} -->
## Event policy: `DiaryEventPrompt_External` — external mod event

| Contract | Value |
|---|---|
| Selector/classifier mapping | `External`; lookup accepts this selector or the Def name. |
| Availability | Base game |
| Prompt guidance | Write a moment reported by another mod as something the pawn just lived through. |
| Enhancement | Stay strictly inside the supplied event line and context facts; do not invent mechanics, systems, or unstated participants. |
| Model preference | No shipped forced model. A player Prompt Studio override may still request a configured model; unknown model text falls back to normal lane routing. |
| Source | [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml) — `DiaryEventPrompt_External` |

## Live-context prompt-enchantment index

| Def | Meaning | Activation source | Selector | Prerequisite |
|---|---|---|---|---|
| `DiaryEnchant_RoyalTitle` | royal title | `RoyalTitle` | `RoyalTitle` | Royalty and an important event |
| `DiaryEnchant_IdeologyRole` | ideoligion role | `IdeologyRole` | `IdeologyRole` | Ideology and an important event |
| `DiaryEnchant_ConsciousnessClouded` | clouded consciousness | `Capacity` | `Consciousness` in [0.35, 0.55] | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_ConsciousnessFading` | fading consciousness | `Capacity` | `Consciousness` in [0.2, 0.35] | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_ConsciousnessBarelyAwake` | barely conscious | `Capacity` | `Consciousness` in [-1, 0.2] | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_FeverishBody` | feverish body | `Hediff` | `Flu`, `Malaria`, `Plague`, `GutWorms`, `MuscleParasites`, `FoodPoisoning`, `ToxicBuildup`, `WoundInfection`, `SleepingSickness` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_BloodLossUrgency` | blood loss urgency | `Hediff` | `BloodLoss` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_AlcoholHigh` | alcohol intoxication | `Hediff` | `AlcoholHigh` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_Hangover` | hangover | `Hediff` | `Hangover` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_AmbrosiaHigh` | ambrosia warmth | `Hediff` | `AmbrosiaHigh` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_GoJuiceHigh` | go-juice high | `Hediff` | `GoJuiceHigh` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_LuciferiumHigh` | luciferium high | `Hediff` | `LuciferiumHigh` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_LuciferiumDependency` | luciferium dependency | `Hediff` | `LuciferiumAddiction` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_ChemicalCraving` | chemical craving | `Hediff` | `AlcoholAddiction`, `AlcoholWithdrawal`, `AmbrosiaAddiction`, `AmbrosiaWithdrawal`, `SmokeleafAddiction`, `SmokeleafWithdrawal`, `PsychiteAddiction`, `PsychiteWithdrawal`, `WakeUpAddiction`, `WakeUpWithdrawal`, `GoJuiceAddiction`, `GoJuiceWithdrawal` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_FlakeHigh` | flake high | `Hediff` | `FlakeHigh` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_PsychiteTeaHigh` | psychite tea high | `Hediff` | `PsychiteTeaHigh` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_YayoHigh` | yayo high | `Hediff` | `YayoHigh` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_SmokeleafHigh` | smokeleaf high | `Hediff` | `SmokeleafHigh` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_PsychicHangover` | psychic hangover | `Hediff` | `PsychicHangover` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_Blindness` | blindness | `Hediff` | `Blindness` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_MemoryDecay` | memory decay | `Hediff` | `Dementia`, `Alzheimers`, `CrumblingMind` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_TraumaSavant` | trauma savant | `Hediff` | `TraumaSavant` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_ResurrectionPsychosis` | resurrection psychosis | `Hediff` | `ResurrectionPsychosis` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_Joywire` | joywire | `Hediff` | `Joywire` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_ParalyticAbasia` | paralytic abasia | `Hediff` | `Abasia` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_Mindscrew` | mindscrew | `Hediff` | `Mindscrew` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_Pregnancy` | pregnancy | `Hediff` | `Pregnant`, `PregnantHuman` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_HemogenCraving` | hemogen craving | `Hediff` | `HemogenCraving` | Biotech |
| `DiaryEnchant_PsychicBondTorn` | psychic bond torn | `Hediff` | `PsychicBondTorn` | Biotech |
| `DiaryEnchant_BlissLobotomy` | bliss lobotomy | `Hediff` | `BlissLobotomy` | Anomaly |
| `DiaryEnchant_RevenantHypnosis` | revenant hypnosis | `Hediff` | `RevenantHypnosis` | Anomaly |
| `DiaryEnchant_CubeInterest` | cube interest | `Hediff` | `CubeInterest` | Anomaly |
| `DiaryEnchant_CubeWithdrawal` | cube withdrawal | `Hediff` | `CubeWithdrawal` | Anomaly |
| `DiaryEnchant_CubeRage` | cube rage | `Hediff` | `CubeRage` | Anomaly |
| `DiaryEnchant_VoidShockOrTouched` | void shock or touched | `Hediff` | `VoidShock`, `VoidTouched` | Anomaly |
| `DiaryEnchant_CorpseTorment` | corpse torment | `Hediff` | `CorpseTorment` | Anomaly |
| `DiaryEnchant_Inhumanized` | inhumanized | `Hediff` | `Inhumanized` | Anomaly |
| `DiaryEnchant_FleshMutation` | flesh tentacle or whip | `Hediff` | `Tentacle`, `FleshTentacle`, `FleshWhip` | Anomaly |
| `DiaryEnchant_Malnutrition` | malnutrition | `Hediff` | `Malnutrition` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_TemperatureInjury` | heatstroke or hypothermia | `Hediff` | `Heatstroke`, `Hypothermia` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_AnestheticHaze` | anesthetic haze | `Hediff` | `Anesthetic` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_PsychicShock` | psychic shock | `Hediff` | `PsychicShock` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_Carcinoma` | carcinoma | `Hediff` | `Carcinoma` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_Mechanites` | mechanites | `Hediff` | `FibrousMechanites`, `SensoryMechanites` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_WakeUpHigh` | wake-up high | `Hediff` | `WakeUpHigh` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_CryptosleepSickness` | cryptosleep sickness | `Hediff` | `CryptosleepSickness` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_AgingBody` | aging body | `Hediff` | `Frail`, `BadBack`, `Cataract`, `HearingLoss` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_Deathrest` | deathrest | `Hediff` | `Deathrest`, `DeathrestExhaustion` | Biotech |
| `DiaryEnchant_LungRot` | lung rot | `Hediff` | `LungRot`, `LungRotExposure` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_BloodRage` | blood rage | `Hediff` | `BloodRage` | Base game or any loaded content providing a matching live hediff/capacity |
| `DiaryEnchant_VacuumExposure` | vacuum exposure | `Hediff` | `VacuumExposure`, `VacuumBurn` | Odyssey |
| `DiaryEnchant_GravNausea` | grav nausea | `Hediff` | `GravNausea` | Odyssey |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_RoyalTitle","label":"royal title","source":"RoyalTitle","chance":0.22,"frequency":-1,"weight":0.55,"severity":1,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":[],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"411fcccdd87cfcab4612165d4b854c34f9a3a96d3657fafad9d871b88380bd0b"} -->
## Enchantment: `DiaryEnchant_RoyalTitle` — royal title

| Contract | Value |
|---|---|
| Activation selector | source `RoyalTitle`; hediffs —; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.22; frequency override=-1; weight=0.55; severity=1 |
| Expected prompt cue/effect | royal title important context respect this status if it changes how the pawn would frame the event |
| Prerequisite | Royalty and an important event |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_RoyalTitle` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_IdeologyRole","label":"ideoligion role","source":"IdeologyRole","chance":0.22,"frequency":-1,"weight":0.55,"severity":1,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":[],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"f30ebd1b820e9bab15d582154f20788df015fdcf9517beaba33856b1f276d0b0"} -->
## Enchantment: `DiaryEnchant_IdeologyRole` — ideoligion role

| Contract | Value |
|---|---|
| Activation selector | source `IdeologyRole`; hediffs —; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.22; frequency override=-1; weight=0.55; severity=1 |
| Expected prompt cue/effect | ideoligion role important context respect this status if it changes how the pawn would frame the event |
| Prerequisite | Ideology and an important event |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_IdeologyRole` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_ConsciousnessClouded","label":"clouded consciousness","source":"Capacity","chance":1,"frequency":-1,"weight":2.2,"severity":1.2,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":[],"hediffSeverityTiers":[],"capacityDefName":"Consciousness","minCapacity":0.35,"maxCapacity":0.55,"effectHash":"151402c46ecb7a7e81c9ad01ac4aaa47a5c84af5a66e5285b089877907a3ac9e"} -->
## Enchantment: `DiaryEnchant_ConsciousnessClouded` — clouded consciousness

| Contract | Value |
|---|---|
| Activation selector | source `Capacity`; hediffs —; capacity `Consciousness`; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=1; frequency override=-1; weight=2.2; severity=1.2 |
| Expected prompt cue/effect | consciousness moderate dulled awareness |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_ConsciousnessClouded` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_ConsciousnessFading","label":"fading consciousness","source":"Capacity","chance":1,"frequency":-1,"weight":3.2,"severity":1.5,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":[],"hediffSeverityTiers":[],"capacityDefName":"Consciousness","minCapacity":0.2,"maxCapacity":0.35,"effectHash":"7eb4d92fdf4f9cc7f734cb67851f532ebcfcc8c1df442d65ce0a4e27f109fbd4"} -->
## Enchantment: `DiaryEnchant_ConsciousnessFading` — fading consciousness

| Contract | Value |
|---|---|
| Activation selector | source `Capacity`; hediffs —; capacity `Consciousness`; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=1; frequency override=-1; weight=3.2; severity=1.5 |
| Expected prompt cue/effect | consciousness major fogged awareness sluggish thoughts |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_ConsciousnessFading` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_ConsciousnessBarelyAwake","label":"barely conscious","source":"Capacity","chance":1,"frequency":-1,"weight":5,"severity":2,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":[],"hediffSeverityTiers":[],"capacityDefName":"Consciousness","minCapacity":-1,"maxCapacity":0.2,"effectHash":"17a1e6290b047ec1f81e06e1050e009b5831bb06a4b5031e1541743d84dfef49"} -->
## Enchantment: `DiaryEnchant_ConsciousnessBarelyAwake` — barely conscious

| Contract | Value |
|---|---|
| Activation selector | source `Capacity`; hediffs —; capacity `Consciousness`; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=1; frequency override=-1; weight=5; severity=2 |
| Expected prompt cue/effect | consciousness critical near collapse thoughts fragmented barely awake |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_ConsciousnessBarelyAwake` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_FeverishBody","label":"feverish body","source":"Hediff","chance":0.65,"frequency":-1,"weight":1.2,"severity":1.2,"visibleOnly":true,"minHediffSeverity":0.05,"hediffDefNames":["Flu","Malaria","Plague","GutWorms","MuscleParasites","FoodPoisoning","ToxicBuildup","WoundInfection","SleepingSickness"],"hediffSeverityTiers":[{"level":"minor","chance":0.35,"frequency":-1,"weight":0.9,"severity":-1},{"level":"moderate","chance":0.65,"frequency":-1,"weight":1.2,"severity":-1},{"level":"major","chance":0.85,"frequency":-1,"weight":1.5,"severity":1.3},{"level":"critical","chance":1,"frequency":-1,"weight":1.8,"severity":1.6}],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_FeverishBody` — feverish body

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Flu`, `Malaria`, `Plague`, `GutWorms`, `MuscleParasites`, `FoodPoisoning`, `ToxicBuildup`, `WoundInfection`, `SleepingSickness`; capacity —; visible-only=yes; minimum hediff severity 0.05 |
| Selection policy | chance=0.65; frequency override=-1; weight=1.2; severity=1.2 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_FeverishBody` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_BloodLossUrgency","label":"blood loss urgency","source":"Hediff","chance":0.75,"frequency":-1,"weight":1.4,"severity":1.6,"visibleOnly":true,"minHediffSeverity":0.05,"hediffDefNames":["BloodLoss"],"hediffSeverityTiers":[{"level":"minor","chance":0.25,"frequency":-1,"weight":0.8,"severity":1},{"level":"moderate","chance":0.65,"frequency":-1,"weight":1.2,"severity":1.3},{"level":"major","chance":0.9,"frequency":-1,"weight":1.6,"severity":1.7},{"level":"critical","chance":1,"frequency":-1,"weight":2,"severity":2}],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_BloodLossUrgency` — blood loss urgency

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `BloodLoss`; capacity —; visible-only=yes; minimum hediff severity 0.05 |
| Selection policy | chance=0.75; frequency override=-1; weight=1.4; severity=1.6 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_BloodLossUrgency` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_AlcoholHigh","label":"alcohol intoxication","source":"Hediff","chance":0.55,"frequency":-1,"weight":0.9,"severity":1,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["AlcoholHigh"],"hediffSeverityTiers":[{"level":"minor","chance":0.35,"frequency":-1,"weight":-1,"severity":-1},{"level":"moderate","chance":0.65,"frequency":-1,"weight":1.1,"severity":-1},{"level":"major","chance":0.9,"frequency":-1,"weight":1.3,"severity":1.2},{"level":"critical","chance":1,"frequency":-1,"weight":1.5,"severity":1.4}],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"0c8fab8c1ff5c674acf40f847c97f97b07feaf0d523228233b5885adf4ea47ae"} -->
## Enchantment: `DiaryEnchant_AlcoholHigh` — alcohol intoxication

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `AlcoholHigh`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.55; frequency override=-1; weight=0.9; severity=1 |
| Expected prompt cue/effect | alcohol intoxication alcohol lifts mood while loosening judgment, slowing movement, and clouding awareness as it deepens loosened judgment reduced capacities |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_AlcoholHigh` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_Hangover","label":"hangover","source":"Hediff","chance":0.6,"frequency":-1,"weight":0.9,"severity":1.1,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["Hangover"],"hediffSeverityTiers":[{"level":"minor","chance":0.35,"frequency":-1,"weight":-1,"severity":-1},{"level":"moderate","chance":0.65,"frequency":-1,"weight":-1,"severity":-1},{"level":"major","chance":0.9,"frequency":-1,"weight":1.3,"severity":-1},{"level":"critical","chance":1,"frequency":-1,"weight":1.5,"severity":1.4}],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"131b831cfc1448fe58bf90749ea3efbc93f5343f94681e307ff58745fdcd920c"} -->
## Enchantment: `DiaryEnchant_Hangover` — hangover

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Hangover`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.6; frequency override=-1; weight=0.9; severity=1.1 |
| Expected prompt cue/effect | hangover the delayed aftereffect of alcohol leaves the head pounding and consciousness dulled dulled awareness pounding aftereffects |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_Hangover` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_AmbrosiaHigh","label":"ambrosia warmth","source":"Hediff","chance":0.45,"frequency":-1,"weight":0.8,"severity":0.9,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["AmbrosiaHigh"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"05d173321bfcc3a7127ac4904f0e2e8816a098ea65e2528bffeb84940d08d0c1"} -->
## Enchantment: `DiaryEnchant_AmbrosiaHigh` — ambrosia warmth

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `AmbrosiaHigh`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.45; frequency override=-1; weight=0.8; severity=0.9 |
| Expected prompt cue/effect | ambrosia warmth ambrosia brings a gentle warmth, relaxed mood, and a little easy energy relaxed energy soft mood lift |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_AmbrosiaHigh` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_GoJuiceHigh","label":"go-juice high","source":"Hediff","chance":0.65,"frequency":-1,"weight":1.1,"severity":1.25,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["GoJuiceHigh"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"9ae265cf9857be7a79d2b3c9648ad5f7c880120e1b43e85393b8ccd260f0276a"} -->
## Enchantment: `DiaryEnchant_GoJuiceHigh` — go-juice high

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `GoJuiceHigh`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.65; frequency override=-1; weight=1.1; severity=1.25 |
| Expected prompt cue/effect | go-juice high go-juice drives a pumped but steady combat rush, dulling pain and pushing the body faster combat rush sharpened body |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_GoJuiceHigh` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_LuciferiumHigh","label":"luciferium high","source":"Hediff","chance":0.45,"frequency":-1,"weight":1,"severity":1.2,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["LuciferiumHigh"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"8a29e8a88a0c8d3d728e37319d3532929f38a0708e6e194152c58a9489858131"} -->
## Enchantment: `DiaryEnchant_LuciferiumHigh` — luciferium high

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `LuciferiumHigh`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.45; frequency override=-1; weight=1; severity=1.2 |
| Expected prompt cue/effect | luciferium high luciferium mechanites sharpen the whole body while leaving an unspoken debt for the next dose artificial clarity mechanite debt |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_LuciferiumHigh` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_LuciferiumDependency","label":"luciferium dependency","source":"Hediff","chance":0.75,"frequency":-1,"weight":1.2,"severity":1.4,"visibleOnly":true,"minHediffSeverity":0.05,"hediffDefNames":["LuciferiumAddiction"],"hediffSeverityTiers":[{"level":"minor","chance":0.45,"frequency":-1,"weight":-1,"severity":-1},{"level":"moderate","chance":0.75,"frequency":-1,"weight":1.3,"severity":-1},{"level":"major","chance":0.95,"frequency":-1,"weight":1.6,"severity":1.6},{"level":"critical","chance":1,"frequency":-1,"weight":2,"severity":2}],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_LuciferiumDependency` — luciferium dependency

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `LuciferiumAddiction`; capacity —; visible-only=yes; minimum hediff severity 0.05 |
| Selection policy | chance=0.75; frequency override=-1; weight=1.2; severity=1.4 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_LuciferiumDependency` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_ChemicalCraving","label":"chemical craving","source":"Hediff","chance":0.55,"frequency":-1,"weight":1,"severity":1.2,"visibleOnly":true,"minHediffSeverity":0.05,"hediffDefNames":["AlcoholAddiction","AlcoholWithdrawal","AmbrosiaAddiction","AmbrosiaWithdrawal","SmokeleafAddiction","SmokeleafWithdrawal","PsychiteAddiction","PsychiteWithdrawal","WakeUpAddiction","WakeUpWithdrawal","GoJuiceAddiction","GoJuiceWithdrawal"],"hediffSeverityTiers":[{"level":"minor","chance":0.3,"frequency":-1,"weight":-1,"severity":-1},{"level":"moderate","chance":0.6,"frequency":-1,"weight":1.1,"severity":-1},{"level":"major","chance":0.85,"frequency":-1,"weight":1.4,"severity":1.3},{"level":"critical","chance":1,"frequency":-1,"weight":1.7,"severity":1.6}],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_ChemicalCraving` — chemical craving

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `AlcoholAddiction`, `AlcoholWithdrawal`, `AmbrosiaAddiction`, `AmbrosiaWithdrawal`, `SmokeleafAddiction`, `SmokeleafWithdrawal`, `PsychiteAddiction`, `PsychiteWithdrawal`, `WakeUpAddiction`, `WakeUpWithdrawal`, `GoJuiceAddiction`, `GoJuiceWithdrawal`; capacity —; visible-only=yes; minimum hediff severity 0.05 |
| Selection policy | chance=0.55; frequency override=-1; weight=1; severity=1.2 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_ChemicalCraving` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_FlakeHigh","label":"flake high","source":"Hediff","chance":0.65,"frequency":-1,"weight":1,"severity":1.15,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["FlakeHigh"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"c1260a4d4da56c78f13a3a8ab648ee7e0183cd613483590f81b8bb2cf211cda3"} -->
## Enchantment: `DiaryEnchant_FlakeHigh` — flake high

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `FlakeHigh`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.65; frequency override=-1; weight=1; severity=1.15 |
| Expected prompt cue/effect | flake high flake gives a powerful euphoric psychite rush that feels bright but physically debilitating hard psychite euphoria debilitating high |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_FlakeHigh` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_PsychiteTeaHigh","label":"psychite tea high","source":"Hediff","chance":0.45,"frequency":-1,"weight":0.8,"severity":0.95,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["PsychiteTeaHigh"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"260cccf0ce7ee38f38083596e1cee46d89176664403e135546c6bebe927bf2c5"} -->
## Enchantment: `DiaryEnchant_PsychiteTeaHigh` — psychite tea high

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `PsychiteTeaHigh`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.45; frequency override=-1; weight=0.8; severity=0.95 |
| Expected prompt cue/effect | psychite tea high psychite tea gives a milder euphoric lift, easing pain and adding comfortable energy mild psychite lift easy energy |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_PsychiteTeaHigh` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_YayoHigh","label":"yayo high","source":"Hediff","chance":0.65,"frequency":-1,"weight":1,"severity":1.15,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["YayoHigh"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"d07e6b8365da57ecd73993603f147b305cfd7a693f17dc837b82c245d3b57c08"} -->
## Enchantment: `DiaryEnchant_YayoHigh` — yayo high

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `YayoHigh`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.65; frequency override=-1; weight=1; severity=1.15 |
| Expected prompt cue/effect | yayo high yayo hits as intense psychite euphoria with restless confidence and a faster body hard psychite euphoria restless drive |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_YayoHigh` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_SmokeleafHigh","label":"smokeleaf high","source":"Hediff","chance":0.55,"frequency":-1,"weight":0.8,"severity":1,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["SmokeleafHigh"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"eac29583038af17da905dcc712fd8e3df746ade5abd764f14bf9e75caebf8fed"} -->
## Enchantment: `DiaryEnchant_SmokeleafHigh` — smokeleaf high

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `SmokeleafHigh`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.55; frequency override=-1; weight=0.8; severity=1 |
| Expected prompt cue/effect | smokeleaf haze smokeleaf wraps thought in fuzzy well-being while slowing awareness and movement fuzzy well-being slowed body |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_SmokeleafHigh` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_PsychicHangover","label":"psychic hangover","source":"Hediff","chance":0.7,"frequency":-1,"weight":1.1,"severity":1.25,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["PsychicHangover"],"hediffSeverityTiers":[{"level":"minor","chance":0.4,"frequency":-1,"weight":-1,"severity":-1},{"level":"moderate","chance":0.7,"frequency":-1,"weight":-1,"severity":-1},{"level":"major","chance":0.9,"frequency":-1,"weight":1.4,"severity":1.4},{"level":"critical","chance":1,"frequency":-1,"weight":1.7,"severity":1.6}],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_PsychicHangover` — psychic hangover

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `PsychicHangover`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.7; frequency override=-1; weight=1.1; severity=1.25 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_PsychicHangover` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_Blindness","label":"blindness","source":"Hediff","chance":0.75,"frequency":-1,"weight":1,"severity":1.2,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["Blindness"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"7a0f45cb4e1200af8bc0ba6e7c89a8920077a25678d48244c5b5501d20562ca9"} -->
## Enchantment: `DiaryEnchant_Blindness` — blindness

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Blindness`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.75; frequency override=-1; weight=1; severity=1.2 |
| Expected prompt cue/effect | sight is absent; ground the entry in sound, touch, distance, remembered layout, and reliance without pretending the pawn can see a world read without sight |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_Blindness` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_MemoryDecay","label":"memory decay","source":"Hediff","chance":0.8,"frequency":-1,"weight":1.2,"severity":1.3,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["Dementia","Alzheimers","CrumblingMind"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_MemoryDecay` — memory decay

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Dementia`, `Alzheimers`, `CrumblingMind`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.8; frequency override=-1; weight=1.2; severity=1.3 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_MemoryDecay` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_TraumaSavant","label":"trauma savant","source":"Hediff","chance":0.75,"frequency":-1,"weight":1.1,"severity":1.15,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["TraumaSavant"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"54a380a36fbf905f0d0313341635f8fe87a79c8cc92856df40a2f8b309df3d2d"} -->
## Enchantment: `DiaryEnchant_TraumaSavant` — trauma savant

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `TraumaSavant`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.75; frequency override=-1; weight=1.1; severity=1.15 |
| Expected prompt cue/effect | ordinary social feeling has gone quiet while thought and technical focus remain unnaturally sharp; keep the contrast restrained sharp thought without ordinary feeling |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_TraumaSavant` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_ResurrectionPsychosis","label":"resurrection psychosis","source":"Hediff","chance":0.85,"frequency":-1,"weight":1.4,"severity":1.5,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["ResurrectionPsychosis"],"hediffSeverityTiers":[{"level":"minor","chance":0.55,"frequency":-1,"weight":-1,"severity":-1},{"level":"moderate","chance":0.85,"frequency":-1,"weight":-1,"severity":-1},{"level":"major","chance":1,"frequency":-1,"weight":1.7,"severity":1.7},{"level":"critical","chance":1,"frequency":-1,"weight":2,"severity":2}],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_ResurrectionPsychosis` — resurrection psychosis

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `ResurrectionPsychosis`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.85; frequency override=-1; weight=1.4; severity=1.5 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_ResurrectionPsychosis` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_Joywire","label":"joywire","source":"Hediff","chance":0.55,"frequency":-1,"weight":0.9,"severity":1.1,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["Joywire"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"36f5e17aa540069c11b63493f8d923db4a9c8a4494355bff7294354cec121c29"} -->
## Enchantment: `DiaryEnchant_Joywire` — joywire

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Joywire`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.55; frequency override=-1; weight=0.9; severity=1.1 |
| Expected prompt cue/effect | an implanted current keeps mood artificially bright even when the moment itself may not deserve cheer artificial cheer against the facts |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_Joywire` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_ParalyticAbasia","label":"paralytic abasia","source":"Hediff","chance":0.75,"frequency":-1,"weight":1.1,"severity":1.2,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["Abasia"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_ParalyticAbasia` — paralytic abasia

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Abasia`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.75; frequency override=-1; weight=1.1; severity=1.2 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_ParalyticAbasia` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_Mindscrew","label":"mindscrew","source":"Hediff","chance":0.65,"frequency":-1,"weight":1.2,"severity":1.3,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["Mindscrew"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_Mindscrew` — mindscrew

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Mindscrew`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.65; frequency override=-1; weight=1.2; severity=1.3 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_Mindscrew` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_Pregnancy","label":"pregnancy","source":"Hediff","chance":0.45,"frequency":-1,"weight":0.9,"severity":1,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["Pregnant","PregnantHuman"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"5c7ca58f655297af661d03a94ce1a7e52c2f48ad19457ba99798d0a129745e4f"} -->
## Enchantment: `DiaryEnchant_Pregnancy` — pregnancy

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Pregnant`, `PregnantHuman`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.45; frequency override=-1; weight=0.9; severity=1 |
| Expected prompt cue/effect | the pawn is carrying a developing child; let bodily awareness, anticipation, and uncertainty enter only where the event makes them relevant a body carrying new life |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_Pregnancy` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_HemogenCraving","label":"hemogen craving","source":"Hediff","chance":0.75,"frequency":-1,"weight":1.2,"severity":1.35,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["HemogenCraving"],"hediffSeverityTiers":[{"level":"minor","chance":0.4,"frequency":-1,"weight":-1,"severity":-1},{"level":"moderate","chance":0.75,"frequency":-1,"weight":-1,"severity":-1},{"level":"major","chance":0.95,"frequency":-1,"weight":1.5,"severity":1.5},{"level":"critical","chance":1,"frequency":-1,"weight":1.8,"severity":1.8}],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_HemogenCraving` — hemogen craving

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `HemogenCraving`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.75; frequency override=-1; weight=1.2; severity=1.35 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Biotech |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_HemogenCraving` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_PsychicBondTorn","label":"psychic bond torn","source":"Hediff","chance":0.8,"frequency":-1,"weight":1.3,"severity":1.4,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["PsychicBondTorn"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_PsychicBondTorn` — psychic bond torn

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `PsychicBondTorn`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.8; frequency override=-1; weight=1.3; severity=1.4 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Biotech |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_PsychicBondTorn` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_BlissLobotomy","label":"bliss lobotomy","source":"Hediff","chance":0.65,"frequency":-1,"weight":1,"severity":1.2,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["BlissLobotomy"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_BlissLobotomy` — bliss lobotomy

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `BlissLobotomy`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.65; frequency override=-1; weight=1; severity=1.2 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Anomaly |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_BlissLobotomy` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_RevenantHypnosis","label":"revenant hypnosis","source":"Hediff","chance":0.85,"frequency":-1,"weight":1.5,"severity":1.6,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["RevenantHypnosis"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"77eabb1ea2446360e2a56f87b509d9904deaba49ab19044daf5a45a02f90e457"} -->
## Enchantment: `DiaryEnchant_RevenantHypnosis` — revenant hypnosis

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `RevenantHypnosis`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.85; frequency override=-1; weight=1.5; severity=1.6 |
| Expected prompt cue/effect | revenant hypnosis a revenant's psychic hold keeps dragging attention into trance and blank obedience trance pressure thoughts pulled away |
| Prerequisite | Anomaly |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_RevenantHypnosis` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_CubeInterest","label":"cube interest","source":"Hediff","chance":0.55,"frequency":-1,"weight":1,"severity":1.1,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["CubeInterest"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"f8953100ce7d8e2cfd25d32fd19d1b174840b119a1d39522f5b86401c5b90215"} -->
## Enchantment: `DiaryEnchant_CubeInterest` — cube interest

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `CubeInterest`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.55; frequency override=-1; weight=1; severity=1.1 |
| Expected prompt cue/effect | cube fixation the cube keeps returning to mind until ordinary thoughts feel secondary compulsive focus object fixation |
| Prerequisite | Anomaly |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_CubeInterest` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_CubeWithdrawal","label":"cube withdrawal","source":"Hediff","chance":0.85,"frequency":-1,"weight":1.4,"severity":1.5,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["CubeWithdrawal"],"hediffSeverityTiers":[{"level":"minor","chance":0.5,"frequency":-1,"weight":-1,"severity":-1},{"level":"moderate","chance":0.85,"frequency":-1,"weight":-1,"severity":-1},{"level":"major","chance":1,"frequency":-1,"weight":1.7,"severity":1.7},{"level":"critical","chance":1,"frequency":-1,"weight":2,"severity":2}],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"c2572297a8a118ae011381474c2b114d5430dda88d25a96251d3c97371d2c8cf"} -->
## Enchantment: `DiaryEnchant_CubeWithdrawal` — cube withdrawal

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `CubeWithdrawal`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.85; frequency override=-1; weight=1.4; severity=1.5 |
| Expected prompt cue/effect | cube withdrawal separation from the cube becomes an aching need that crowds out other concerns compulsive absence restless need |
| Prerequisite | Anomaly |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_CubeWithdrawal` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_CubeRage","label":"cube rage","source":"Hediff","chance":1,"frequency":-1,"weight":1.8,"severity":1.8,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["CubeRage"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"679ca85ca15d35ad18c49bd347998924a0548a98808c89b226a63e04c25ce466"} -->
## Enchantment: `DiaryEnchant_CubeRage` — cube rage

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `CubeRage`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=1; frequency override=-1; weight=1.8; severity=1.8 |
| Expected prompt cue/effect | cube rage the cube fixation has curdled into violent, narrowing anger compulsion turns violent thoughts narrow to anger |
| Prerequisite | Anomaly |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_CubeRage` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_VoidShockOrTouched","label":"void shock or touched","source":"Hediff","chance":0.85,"frequency":-1,"weight":1.4,"severity":1.5,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["VoidShock","VoidTouched"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"12174d729f3cf3be4a13658537bf2d9545e726913f36922fd2fcf2000638a727"} -->
## Enchantment: `DiaryEnchant_VoidShockOrTouched` — void shock or touched

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `VoidShock`, `VoidTouched`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.85; frequency override=-1; weight=1.4; severity=1.5 |
| Expected prompt cue/effect | contact with the void has left thought and sensation altered; keep the disturbance close and unexplained rather than adding hidden lore the void's afterimage |
| Prerequisite | Anomaly |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_VoidShockOrTouched` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_CorpseTorment","label":"corpse torment","source":"Hediff","chance":0.85,"frequency":-1,"weight":1.4,"severity":1.5,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["CorpseTorment"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"25f581dca03ca8b6762c43df496709bea2ee3f7c9f0013bee289876099b0bc2e"} -->
## Enchantment: `DiaryEnchant_CorpseTorment` — corpse torment

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `CorpseTorment`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.85; frequency override=-1; weight=1.4; severity=1.5 |
| Expected prompt cue/effect | corpse torment death-haunted torment presses into thought and will not settle like ordinary grief death-haunted thoughts unnatural guilt |
| Prerequisite | Anomaly |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_CorpseTorment` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_Inhumanized","label":"inhumanized","source":"Hediff","chance":1,"frequency":-1,"weight":2.2,"severity":2,"visibleOnly":false,"minHediffSeverity":0,"hediffDefNames":["Inhumanized"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"8fbd3e840471f2c8569578ab2af3ce5368a58fb7409441f59bfc58f80376a172"} -->
## Enchantment: `DiaryEnchant_Inhumanized` — inhumanized

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Inhumanized`; capacity —; visible-only=no; minimum hediff severity 0 |
| Selection policy | chance=1; frequency override=-1; weight=2.2; severity=2 |
| Expected prompt cue/effect | inhumanized human warmth and ordinary attachment have gone distant; the pawn's thoughts should feel cold, void-touched, and detached from people without inventing new lore void detachment human warmth absent use the forced dark void writing style |
| Prerequisite | Anomaly |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_Inhumanized` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_FleshMutation","label":"flesh tentacle or whip","source":"Hediff","chance":0.7,"frequency":-1,"weight":1.1,"severity":1.2,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["Tentacle","FleshTentacle","FleshWhip"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_FleshMutation` — flesh tentacle or whip

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Tentacle`, `FleshTentacle`, `FleshWhip`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.7; frequency override=-1; weight=1.1; severity=1.2 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Anomaly |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_FleshMutation` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_Malnutrition","label":"malnutrition","source":"Hediff","chance":0.75,"frequency":-1,"weight":1.3,"severity":1.4,"visibleOnly":true,"minHediffSeverity":0.05,"hediffDefNames":["Malnutrition"],"hediffSeverityTiers":[{"level":"minor","chance":0.35,"frequency":-1,"weight":0.9,"severity":1},{"level":"moderate","chance":0.7,"frequency":-1,"weight":1.3,"severity":-1},{"level":"major","chance":0.9,"frequency":-1,"weight":1.6,"severity":1.6},{"level":"critical","chance":1,"frequency":-1,"weight":2,"severity":1.9}],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_Malnutrition` — malnutrition

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Malnutrition`; capacity —; visible-only=yes; minimum hediff severity 0.05 |
| Selection policy | chance=0.75; frequency override=-1; weight=1.3; severity=1.4 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_Malnutrition` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_TemperatureInjury","label":"heatstroke or hypothermia","source":"Hediff","chance":0.75,"frequency":-1,"weight":1.3,"severity":1.4,"visibleOnly":true,"minHediffSeverity":0.05,"hediffDefNames":["Heatstroke","Hypothermia"],"hediffSeverityTiers":[{"level":"minor","chance":0.35,"frequency":-1,"weight":0.9,"severity":1},{"level":"moderate","chance":0.7,"frequency":-1,"weight":1.3,"severity":-1},{"level":"major","chance":0.9,"frequency":-1,"weight":1.6,"severity":1.6},{"level":"critical","chance":1,"frequency":-1,"weight":2,"severity":1.9}],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_TemperatureInjury` — heatstroke or hypothermia

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Heatstroke`, `Hypothermia`; capacity —; visible-only=yes; minimum hediff severity 0.05 |
| Selection policy | chance=0.75; frequency override=-1; weight=1.3; severity=1.4 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_TemperatureInjury` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_AnestheticHaze","label":"anesthetic haze","source":"Hediff","chance":0.6,"frequency":-1,"weight":1,"severity":1.1,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["Anesthetic"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_AnestheticHaze` — anesthetic haze

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Anesthetic`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.6; frequency override=-1; weight=1; severity=1.1 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_AnestheticHaze` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_PsychicShock","label":"psychic shock","source":"Hediff","chance":0.7,"frequency":-1,"weight":1.1,"severity":1.2,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["PsychicShock"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_PsychicShock` — psychic shock

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `PsychicShock`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.7; frequency override=-1; weight=1.1; severity=1.2 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_PsychicShock` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_Carcinoma","label":"carcinoma","source":"Hediff","chance":0.7,"frequency":-1,"weight":1.2,"severity":1.3,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["Carcinoma"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"5e023dc9d912ed8bc58366ca7c54c8fc9eb0dffa7fb02c66508b5869ee9e0b6a"} -->
## Enchantment: `DiaryEnchant_Carcinoma` — carcinoma

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Carcinoma`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.7; frequency override=-1; weight=1.2; severity=1.3 |
| Expected prompt cue/effect | a slow internal illness is present; write bodily uncertainty and treatment pressure without inventing prognosis, pain, or diagnosis details a slow threat inside the body |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_Carcinoma` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_Mechanites","label":"mechanites","source":"Hediff","chance":0.65,"frequency":-1,"weight":1.1,"severity":1.2,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["FibrousMechanites","SensoryMechanites"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_Mechanites` — mechanites

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `FibrousMechanites`, `SensoryMechanites`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.65; frequency override=-1; weight=1.1; severity=1.2 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_Mechanites` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_WakeUpHigh","label":"wake-up high","source":"Hediff","chance":0.55,"frequency":-1,"weight":0.9,"severity":1,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["WakeUpHigh"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_WakeUpHigh` — wake-up high

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `WakeUpHigh`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.55; frequency override=-1; weight=0.9; severity=1 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_WakeUpHigh` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_CryptosleepSickness","label":"cryptosleep sickness","source":"Hediff","chance":0.6,"frequency":-1,"weight":1,"severity":1.1,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["CryptosleepSickness"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_CryptosleepSickness` — cryptosleep sickness

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `CryptosleepSickness`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.6; frequency override=-1; weight=1; severity=1.1 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_CryptosleepSickness` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_AgingBody","label":"aging body","source":"Hediff","chance":0.15,"frequency":-1,"weight":0.5,"severity":0.8,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["Frail","BadBack","Cataract","HearingLoss"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_AgingBody` — aging body

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Frail`, `BadBack`, `Cataract`, `HearingLoss`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.15; frequency override=-1; weight=0.5; severity=0.8 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_AgingBody` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_Deathrest","label":"deathrest","source":"Hediff","chance":0.6,"frequency":-1,"weight":1,"severity":1.1,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["Deathrest","DeathrestExhaustion"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"44601af2727409d79c5ed98911aa7dbaae88c6ec11cf9260857bbe8c564f6e90"} -->
## Enchantment: `DiaryEnchant_Deathrest` — deathrest

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `Deathrest`, `DeathrestExhaustion`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.6; frequency override=-1; weight=1; severity=1.1 |
| Expected prompt cue/effect | the body is drawn toward deathlike restorative sleep; keep its stillness, hunger for rest, or incomplete recovery relevant without inventing interruption deathlike restorative sleep |
| Prerequisite | Biotech |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_Deathrest` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_LungRot","label":"lung rot","source":"Hediff","chance":0.7,"frequency":-1,"weight":1.1,"severity":1.2,"visibleOnly":true,"minHediffSeverity":0.05,"hediffDefNames":["LungRot","LungRotExposure"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"ed1de02dc7e33b33a6542845bf89b451e820f01a1a452eacd418dd872710b709"} -->
## Enchantment: `DiaryEnchant_LungRot` — lung rot

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `LungRot`, `LungRotExposure`; capacity —; visible-only=yes; minimum hediff severity 0.05 |
| Selection policy | chance=0.7; frequency override=-1; weight=1.1; severity=1.2 |
| Expected prompt cue/effect | breathing is threatened by a rotting lung condition; use strain, caution, or air hunger only to the degree supplied breath under strain |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_LungRot` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_BloodRage","label":"blood rage","source":"Hediff","chance":0.85,"frequency":-1,"weight":1.4,"severity":1.5,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["BloodRage"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"545c38b0922de19734fbffde62792c37c2aef6a3216cfa472449173165220f7d"} -->
## Enchantment: `DiaryEnchant_BloodRage` — blood rage

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `BloodRage`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.85; frequency override=-1; weight=1.4; severity=1.5 |
| Expected prompt cue/effect | Uses the live hediff/capacity label, severity, body part, and generic health-context wording. |
| Prerequisite | Base game or any loaded content providing a matching live hediff/capacity |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_BloodRage` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_VacuumExposure","label":"vacuum exposure","source":"Hediff","chance":0.8,"frequency":-1,"weight":1.3,"severity":1.4,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["VacuumExposure","VacuumBurn"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"7feb3d8ce8ca2af2d472db14fb57d07ca41d4e41ba49119f01f19618addbf6ad"} -->
## Enchantment: `DiaryEnchant_VacuumExposure` — vacuum exposure

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `VacuumExposure`, `VacuumBurn`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.8; frequency override=-1; weight=1.3; severity=1.4 |
| Expected prompt cue/effect | exposure to vacuum has injured the body; write pressure, raw tissue, and fragile recovery without inventing the accident or exact damage vacuum-burned tissue |
| Prerequisite | Odyssey |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_VacuumExposure` |

<!-- repowiki:enchantment {"defName":"DiaryEnchant_GravNausea","label":"grav nausea","source":"Hediff","chance":0.7,"frequency":-1,"weight":1.1,"severity":1.2,"visibleOnly":true,"minHediffSeverity":0,"hediffDefNames":["GravNausea"],"hediffSeverityTiers":[],"capacityDefName":"","minCapacity":-1,"maxCapacity":-1,"effectHash":"750f5270a851c0f6fe37ed4958a6471ec182997d61c4639cbb469cc3761a0fd8"} -->
## Enchantment: `DiaryEnchant_GravNausea` — grav nausea

| Contract | Value |
|---|---|
| Activation selector | source `Hediff`; hediffs `GravNausea`; capacity —; visible-only=yes; minimum hediff severity 0 |
| Selection policy | chance=0.7; frequency override=-1; weight=1.1; severity=1.2 |
| Expected prompt cue/effect | changed gravity has unsettled balance and stomach; let motion, orientation, and queasiness tint the moment without adding a new incident gravity turning the stomach |
| Prerequisite | Odyssey |
| Expected evidence | With a matching live state and an allowed context preset, repeated deterministic prompt-test rerolls can select one `important context` line. No separate diary page is created by the enchantment. |
| Source | [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml) — `DiaryEnchant_GravNausea` |

## Participating layers not enumerated here

Writing style and psychotype/persona text join the **system prompt** only when the selected template allows persona/style. Humor is an optional voice block. Culture annotations, recalled memory, belief context, narrative continuity, event windows, and observations supply bounded user-prompt fields when selected. Text decoration is post-response presentation; it is not an extra LLM prompt language. These catalogs remain XML-owned, but this reference deliberately does not enumerate every preset instance.

## Source of truth

- Prompt templates: [1.6/Defs/DiaryPromptTemplateDefs.xml](../../../1.6/Defs/DiaryPromptTemplateDefs.xml).
- Event guidance: [1.6/Defs/DiaryEventPromptDefs.xml](../../../1.6/Defs/DiaryEventPromptDefs.xml).
- Live-context cues: [1.6/Defs/DiaryPromptEnchantmentDefs.xml](../../../1.6/Defs/DiaryPromptEnchantmentDefs.xml).
- Required/optional context selection: [Source/Pipeline/PromptContextDetail.cs](../../../Source/Pipeline/PromptContextDetail.cs) — `PromptContextSelector.IsRequired`.
- Rendering: [Source/Generation/PromptAssembler.cs](../../../Source/Generation/PromptAssembler.cs) — `PromptAssembler`.
