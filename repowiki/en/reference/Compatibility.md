# Compatibility

This reference is exhaustive for compatibility XML and first-party adapter projects shipped in this repository. It is **not** a closed list of third-party users of the public API.

The generic path is: an adapter verifies its own source event, submits a bounded request through `PawnDiaryApi`, and the core `External` runtime route applies the player master switch, source budget, group policy, pawn eligibility, and normal generation/persistence rules. See [EXTERNAL_API.md](../../../EXTERNAL_API.md) for the public contract.

## First-party adapter index

| Adapter | Package ID | Required packages | Primary behavior |
|---|---|---|---|
| Pawn Diary — Example Adapter (template) | `aimmlegate.pawndiary.adapter.example` | `aimmlegate.pawndiary` | Public API explorer/test harness; submits factual, prompt-guided, and direct entries and demonstrates context/listener calls. |
| Pawn Diary: 1-2-3 Personalities | `aimmlegate.pawndiary.adapter.personalities123` | `aimmlegate.pawndiary`, `hahkethomemah.simplepersonalities` | Reads 1-2-3 Personalities and maps/seeds Pawn Diary psychotype voice; optional LLM transform uses Pawn Diary's one-shot completion API. |
| Pawn Diary: Powerful AI Integration | `aimmlegate.pawndiary.adapter.powerfulai` | `aimmlegate.pawndiary`, `codex.dynamicrolesstoryteller` | Mirrors Powerful AI persona text into Pawn Diary's reversible psychotype override, directly or through one LLM transform. |
| Pawn Diary: Rimpsyche | `aimmlegate.pawndiary.adapter.rimpsyche` | `aimmlegate.pawndiary`, `Maux36.Rimpsyche` | Adds a Rimpsyche context line, optional psychotype ownership/transform, XML social coverage, and charged-conversation External pages. |
| PawnDiary: RimTalk bridge | `aimmlegate.pawndiary.rimtalkbridge` | `aimmlegate.pawndiary`, `cj.rimtalk` | Injects diary context into RimTalk, optionally synchronizes persona direction, and can submit selected displayed conversations as External pages. |
| Pawn Diary: SpeakUp | `aimmlegate.pawndiary.adapter.speakup` | `aimmlegate.pawndiary`, `JPT.speakup` | Adds richer SpeakUp XML groups and optionally submits a completed multi-reply Talk chain as one External page. |
| Pawn Diary: Vanilla Social Interactions Expanded | `aimmlegate.pawndiary.adapter.vsie` | `aimmlegate.pawndiary`, `VanillaExpanded.VanillaSocialInteractionsExpanded` | Adds VSIE interaction/thought/relation groups and submits birthday/funeral gatherings that lack a native InteractionDef/TaleDef. |

<!-- repowiki:adapter {"directory":"PawnDiary.ExampleAdapter","packageId":"aimmlegate.pawndiary.adapter.example","dependencies":["aimmlegate.pawndiary"]} -->
## Adapter: Pawn Diary — Example Adapter (template)

| Test field | Expected behavior |
|---|---|
| Required package(s) | `aimmlegate.pawndiary` |
| Capture/submission path | Public API explorer/test harness; submits factual, prompt-guided, and direct entries and demonstrates context/listener calls. |
| Setup/trigger | Use its Dev Mode API Explorer actions with an eligible pawn. |
| Expected evidence | External test pages use `exampleAdapterQuietMoment`, `exampleAdapterPromptIdea`, or `exampleAdapterDirectNote`. |
| Dependency absent | It depends only on Pawn Diary; without a live game or the master integration switch it returns safe disabled/not-ready results. |
| External transmission | No independent network transport. Its optional completion calls use Pawn Diary's configured LLM lane. |
| Source | [integrations/PawnDiary.ExampleAdapter](../../../integrations/PawnDiary.ExampleAdapter/) |

<!-- repowiki:adapter {"directory":"PawnDiary.PersonalitiesBridge","packageId":"aimmlegate.pawndiary.adapter.personalities123","dependencies":["aimmlegate.pawndiary","hahkethomemah.simplepersonalities"]} -->
## Adapter: Pawn Diary: 1-2-3 Personalities

| Test field | Expected behavior |
|---|---|
| Required package(s) | `aimmlegate.pawndiary`, `hahkethomemah.simplepersonalities` |
| Capture/submission path | Reads 1-2-3 Personalities and maps/seeds Pawn Diary psychotype voice; optional LLM transform uses Pawn Diary's one-shot completion API. |
| Setup/trigger | Select a bridge mode, then load or regenerate a pawn with 1-2-3 personality data. |
| Expected evidence | Voice changes are visible in the pawn Writing Style dialog; XML also classifies shipped personality thoughts/interactions. |
| Dependency absent | The bridge warns once and remains idle when 1-2-3 Personalities is absent. |
| External transmission | LLM Transform only: the configured transform instruction plus the pawn's 1-2-3 personality summary goes to the selected Pawn Diary lane. |
| Source | [integrations/PawnDiary.PersonalitiesBridge](../../../integrations/PawnDiary.PersonalitiesBridge/) |

<!-- repowiki:adapter {"directory":"PawnDiary.PowerfulAiBridge","packageId":"aimmlegate.pawndiary.adapter.powerfulai","dependencies":["aimmlegate.pawndiary","codex.dynamicrolesstoryteller"]} -->
## Adapter: Pawn Diary: Powerful AI Integration

| Test field | Expected behavior |
|---|---|
| Required package(s) | `aimmlegate.pawndiary`, `codex.dynamicrolesstoryteller` |
| Capture/submission path | Mirrors Powerful AI persona text into Pawn Diary's reversible psychotype override, directly or through one LLM transform. |
| Setup/trigger | Choose Direct or LLM-assisted mode and load/regenerate a pawn with Powerful AI persona text. |
| Expected evidence | The pawn's effective psychotype/voice reports the source-owned override; no compatibility event page is created. |
| Dependency absent | The bridge warns once and remains idle when Powerful AI Integration is absent. |
| External transmission | LLM-assisted only: the bridge prompt plus Powerful AI persona text goes to the selected Pawn Diary lane. |
| Source | [integrations/PawnDiary.PowerfulAiBridge](../../../integrations/PawnDiary.PowerfulAiBridge/) |

<!-- repowiki:adapter {"directory":"PawnDiary.RimpsycheBridge","packageId":"aimmlegate.pawndiary.adapter.rimpsyche","dependencies":["aimmlegate.pawndiary","Maux36.Rimpsyche"]} -->
## Adapter: Pawn Diary: Rimpsyche

| Test field | Expected behavior |
|---|---|
| Required package(s) | `aimmlegate.pawndiary`, `Maux36.Rimpsyche` |
| Capture/submission path | Adds a Rimpsyche context line, optional psychotype ownership/transform, XML social coverage, and charged-conversation External pages. |
| Setup/trigger | Enable the desired tiers; create a sufficiently charged Rimpsyche conversation or regenerate a pawn outlook. |
| Expected evidence | Charged conversations use `rimpsyche_charged_conversation`; voice/context tiers are visible in prompt preview and the pawn voice editor. |
| Dependency absent | Package guards prevent Rimpsyche types from running and leave the bridge inert. |
| External transmission | LLM Transform only: transform guidance plus the bounded Rimpsyche psyche summary goes to the selected Pawn Diary lane. |
| Source | [integrations/PawnDiary.RimpsycheBridge](../../../integrations/PawnDiary.RimpsycheBridge/) |

<!-- repowiki:adapter {"directory":"PawnDiary.RimTalkBridge","packageId":"aimmlegate.pawndiary.rimtalkbridge","dependencies":["aimmlegate.pawndiary","cj.rimtalk"]} -->
## Adapter: PawnDiary: RimTalk bridge

| Test field | Expected behavior |
|---|---|
| Required package(s) | `aimmlegate.pawndiary`, `cj.rimtalk` |
| Capture/submission path | Injects diary context into RimTalk, optionally synchronizes persona direction, and can submit selected displayed conversations as External pages. |
| Setup/trigger | Set integration level 1 for shared context or level 2 for conversation capture, then complete a RimTalk chat. |
| Expected evidence | Selected conversations use `rimtalkbridgeConversation`; fallback ambient XML remains available if the rich hook is not ready. |
| Dependency absent | All RimTalk-typed behavior is guarded; the bridge warns once and stays idle. |
| External transmission | Context/persona is passed in-process to RimTalk. Optional semantic assessment/persona transform uses a selected Pawn Diary LLM lane. |
| Source | [integrations/PawnDiary.RimTalkBridge](../../../integrations/PawnDiary.RimTalkBridge/) |

<!-- repowiki:adapter {"directory":"PawnDiary.SpeakUp","packageId":"aimmlegate.pawndiary.adapter.speakup","dependencies":["aimmlegate.pawndiary","JPT.speakup"]} -->
## Adapter: Pawn Diary: SpeakUp

| Test field | Expected behavior |
|---|---|
| Required package(s) | `aimmlegate.pawndiary`, `JPT.speakup` |
| Capture/submission path | Adds richer SpeakUp XML groups and optionally submits a completed multi-reply Talk chain as one External page. |
| Setup/trigger | Enable whole-conversation capture and complete a SpeakUp Talk meeting the reply threshold. |
| Expected evidence | Individual lines classify through SpeakUp groups; whole conversations use `speakupbridgeConversation`. |
| Dependency absent | The bridge installs no reflection hook and stays inert when SpeakUp is absent. |
| External transmission | No independent network transport; submitted conversation summaries use Pawn Diary's normal configured generation lane. |
| Source | [integrations/PawnDiary.SpeakUp](../../../integrations/PawnDiary.SpeakUp/) |

<!-- repowiki:adapter {"directory":"PawnDiary.Vsie","packageId":"aimmlegate.pawndiary.adapter.vsie","dependencies":["aimmlegate.pawndiary","VanillaExpanded.VanillaSocialInteractionsExpanded"]} -->
## Adapter: Pawn Diary: Vanilla Social Interactions Expanded

| Test field | Expected behavior |
|---|---|
| Required package(s) | `aimmlegate.pawndiary`, `VanillaExpanded.VanillaSocialInteractionsExpanded` |
| Capture/submission path | Adds VSIE interaction/thought/relation groups and submits birthday/funeral gatherings that lack a native InteractionDef/TaleDef. |
| Setup/trigger | Cause a VSIE social interaction, birthday gathering, or funeral gathering with the matching adapter toggle enabled. |
| Expected evidence | XML groups classify ordinary VSIE signals; gatherings use `vsieBirthdayGathering` or `vsieFuneralGathering`. |
| Dependency absent | Package-gated XML and a startup guard make the adapter inert when VSIE is absent. |
| External transmission | No independent network transport; External gathering pages use Pawn Diary's configured generation lane. |
| Source | [integrations/PawnDiary.Vsie](../../../integrations/PawnDiary.Vsie/) |

## Shipped compatibility policy index

| Type | Exact policy ID | Owning XML | Package gate / dependency |
|---|---|---|---|
| group | `alphamemes_funeral` | [1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml) | `Sarg.AlphaMemes` |
| group | `alphamemes_rituals` | [1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml) | `Sarg.AlphaMemes` |
| group | `alphamemes_thoughts` | [1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml) | `Sarg.AlphaMemes` |
| group | `alphamemes_baptism` | [1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml) | `Sarg.AlphaMemes` |
| group | `alphamemes_hediffs` | [1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml) | `Sarg.AlphaMemes` |
| group | `hospitality_guestwork` | [1.6/Defs/Compat/DiaryCompat_Hospitality.xml](../../../1.6/Defs/Compat/DiaryCompat_Hospitality.xml) | `Orion.Hospitality` |
| group | `hospitality_scrounge` | [1.6/Defs/Compat/DiaryCompat_Hospitality.xml](../../../1.6/Defs/Compat/DiaryCompat_Hospitality.xml) | `Orion.Hospitality` |
| group | `rimpsyche_chatter` | [1.6/Defs/Compat/DiaryCompat_Rimpsyche.xml](../../../1.6/Defs/Compat/DiaryCompat_Rimpsyche.xml) | `Maux36.Rimpsyche` |
| group | `rimpsyche_afterfeel` | [1.6/Defs/Compat/DiaryCompat_Rimpsyche.xml](../../../1.6/Defs/Compat/DiaryCompat_Rimpsyche.xml) | `Maux36.Rimpsyche` |
| group | `rimtalk_chatter` | [1.6/Defs/Compat/DiaryCompat_RimTalk.xml](../../../1.6/Defs/Compat/DiaryCompat_RimTalk.xml) | `cj.rimtalk` |
| group | `speakup_chitchat` | [1.6/Defs/Compat/DiaryCompat_SpeakUp.xml](../../../1.6/Defs/Compat/DiaryCompat_SpeakUp.xml) | `JPT.speakup` |
| group | `vee_raids` | [1.6/Defs/Compat/DiaryCompat_VEE.xml](../../../1.6/Defs/Compat/DiaryCompat_VEE.xml) | `VanillaExpanded.VEE` |
| group | `vee_hediffs` | [1.6/Defs/Compat/DiaryCompat_VEE.xml](../../../1.6/Defs/Compat/DiaryCompat_VEE.xml) | `VanillaExpanded.VEE` |
| group | `viememes_darkrites` | [1.6/Defs/Compat/DiaryCompat_VIEMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_VIEMemes.xml) | `VanillaExpanded.VMemesE` |
| group | `viememes_rituals` | [1.6/Defs/Compat/DiaryCompat_VIEMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_VIEMemes.xml) | `VanillaExpanded.VMemesE` |
| group | `viememes_thoughts` | [1.6/Defs/Compat/DiaryCompat_VIEMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_VIEMemes.xml) | `VanillaExpanded.VMemesE` |
| group | `viememes_interrogation` | [1.6/Defs/Compat/DiaryCompat_VIEMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_VIEMemes.xml) | `VanillaExpanded.VMemesE` |
| group | `vte_thoughts` | [1.6/Defs/Compat/DiaryCompat_VTE.xml](../../../1.6/Defs/Compat/DiaryCompat_VTE.xml) | `VanillaExpanded.VanillaTraitsExpanded` |
| group | `vte_mentalbreaks` | [1.6/Defs/Compat/DiaryCompat_VTE.xml](../../../1.6/Defs/Compat/DiaryCompat_VTE.xml) | `VanillaExpanded.VanillaTraitsExpanded` |
| group | `wbr_hookup` | [1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml](../../../1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml) | `divineDerivative.Romance` |
| group | `wbr_askedout` | [1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml](../../../1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml) | `divineDerivative.Romance` |
| group | `wbr_thoughts` | [1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml](../../../1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml) | `divineDerivative.Romance` |
| window | `HospitalityGuestsArrived` | [1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml](../../../1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml) | `Orion.Hospitality` |
| window | `HospitalityGuestJoined` | [1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml](../../../1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml) | `Orion.Hospitality` |
| group | `eventWindowHospitalityGuestsArrived` | [1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml](../../../1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml) | `Orion.Hospitality` |
| group | `eventWindowHospitalityGuestJoined` | [1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml](../../../1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml) | `Orion.Hospitality` |
| window | `VeeEarthquake` | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) | `VanillaExpanded.VEE` |
| window | `VeeMeteoriteShower` | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) | `VanillaExpanded.VEE` |
| window | `VeeSpaceBattle` | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) | `VanillaExpanded.VEE` |
| window | `VeeStampede` | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) | `VanillaExpanded.VEE` |
| group | `eventWindowVeeEarthquake` | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) | `VanillaExpanded.VEE` |
| group | `eventWindowVeeMeteoriteShower` | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) | `VanillaExpanded.VEE` |
| group | `eventWindowVeeSpaceBattle` | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) | `VanillaExpanded.VEE` |
| group | `eventWindowVeeStampede` | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) | `VanillaExpanded.VEE` |
| condition | `VeeDroughtActive` | [1.6/Defs/Compat/DiaryObservedConditions_VEE.xml](../../../1.6/Defs/Compat/DiaryObservedConditions_VEE.xml) | `VanillaExpanded.VEE` |
| condition | `VeeLongNightActive` | [1.6/Defs/Compat/DiaryObservedConditions_VEE.xml](../../../1.6/Defs/Compat/DiaryObservedConditions_VEE.xml) | `VanillaExpanded.VEE` |
| condition | `VeeScorchActive` | [1.6/Defs/Compat/DiaryObservedConditions_VEE.xml](../../../1.6/Defs/Compat/DiaryObservedConditions_VEE.xml) | `VanillaExpanded.VEE` |
| condition | `VeeWhiteoutActive` | [1.6/Defs/Compat/DiaryObservedConditions_VEE.xml](../../../1.6/Defs/Compat/DiaryObservedConditions_VEE.xml) | `VanillaExpanded.VEE` |
| condition | `VeePsychicBloomActive` | [1.6/Defs/Compat/DiaryObservedConditions_VEE.xml](../../../1.6/Defs/Compat/DiaryObservedConditions_VEE.xml) | `VanillaExpanded.VEE` |
| condition | `VeePsychicHumActive` | [1.6/Defs/Compat/DiaryObservedConditions_VEE.xml](../../../1.6/Defs/Compat/DiaryObservedConditions_VEE.xml) | `VanillaExpanded.VEE` |
| group | `exampleAdapterQuietMoment` | [integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml](../../../integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml) | Adapter package / owning file |
| group | `exampleAdapterPromptIdea` | [integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml](../../../integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml) | Adapter package / owning file |
| group | `exampleAdapterDirectNote` | [integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml](../../../integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml) | Adapter package / owning file |
| group | `personalities123_thoughts` | [integrations/PawnDiary.PersonalitiesBridge/1.6/Defs/DiaryCompat_123Personalities.xml](../../../integrations/PawnDiary.PersonalitiesBridge/1.6/Defs/DiaryCompat_123Personalities.xml) | `hahkethomemah.simplepersonalities`; `hahkethomemah.simplepersonalities.module2` |
| group | `personalities123_interactions` | [integrations/PawnDiary.PersonalitiesBridge/1.6/Defs/DiaryCompat_123Personalities.xml](../../../integrations/PawnDiary.PersonalitiesBridge/1.6/Defs/DiaryCompat_123Personalities.xml) | `hahkethomemah.simplepersonalities.module2` |
| group | `rimpsyche_conversations` | [integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryCompat_Rimpsyche.xml](../../../integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryCompat_Rimpsyche.xml) | `Maux36.Rimpsyche` |
| group | `rimpsyche_thoughts` | [integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryCompat_Rimpsyche.xml](../../../integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryCompat_Rimpsyche.xml) | `Maux36.Rimpsyche` |
| group | `rimpsyche_charged_conversation` | [integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryExternalGroups_Rimpsyche.xml](../../../integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryExternalGroups_Rimpsyche.xml) | `Maux36.Rimpsyche` |
| group | `rimtalkbridgeConversation` | [integrations/PawnDiary.RimTalkBridge/1.6/Defs/DiaryExternalGroups_RimTalkBridge.xml](../../../integrations/PawnDiary.RimTalkBridge/1.6/Defs/DiaryExternalGroups_RimTalkBridge.xml) | Adapter package / owning file |
| group | `speakupbridge_deeptalk` | [integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml](../../../integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml) | `JPT.speakup` |
| group | `speakupbridge_jokes` | [integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml](../../../integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml) | `JPT.speakup` |
| group | `speakupbridge_prisoner` | [integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml](../../../integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml) | `JPT.speakup` |
| group | `speakupbridge_reactions` | [integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml](../../../integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml) | `JPT.speakup` |
| group | `speakupbridge_chatter` | [integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml](../../../integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml) | `JPT.speakup` |
| group | `speakupbridgeConversation` | [integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryExternalGroups_SpeakUp.xml](../../../integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryExternalGroups_SpeakUp.xml) | `JPT.speakup` |
| group | `vsie_vent` | [integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml](../../../integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml) | `VanillaExpanded.VanillaSocialInteractionsExpanded` |
| group | `vsie_teaching` | [integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml](../../../integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml) | `VanillaExpanded.VanillaSocialInteractionsExpanded` |
| group | `friendship_relation` | [integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml](../../../integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml) | `VanillaExpanded.VanillaSocialInteractionsExpanded` |
| group | `vsie_thoughts` | [integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml](../../../integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml) | `VanillaExpanded.VanillaSocialInteractionsExpanded` |
| group | `vsieBirthdayGathering` | [integrations/PawnDiary.Vsie/1.6/Defs/DiaryExternalGroups_Vsie.xml](../../../integrations/PawnDiary.Vsie/1.6/Defs/DiaryExternalGroups_Vsie.xml) | `VanillaExpanded.VanillaSocialInteractionsExpanded` |
| group | `vsieFuneralGathering` | [integrations/PawnDiary.Vsie/1.6/Defs/DiaryExternalGroups_Vsie.xml](../../../integrations/PawnDiary.Vsie/1.6/Defs/DiaryExternalGroups_Vsie.xml) | `VanillaExpanded.VanillaSocialInteractionsExpanded` |

<!-- repowiki:compat-group {"defName":"alphamemes_funeral","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":["AM_AnimaBurial","AM_BlastOffFuneral","AM_CremateFuneral","AM_CremateFuneralNoCorpse","AM_DreadnoughtCryptoFuneral","AM_DreadnoughtFuneral","AM_FleshCraftingFuneral","AM_FuneralNoCorpse","AM_InsectoidBurial","AM_Mummification","AM_OcularFuneral","AM_PastedFuneral","AM_PyramidBurial","AM_RumBurial","AM_SkyBurial","GR_ExtractorFuneral"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Sarg.AlphaMemes"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml"} -->
## Compatibility group: `alphamemes_funeral`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Sarg.AlphaMemes` |
| Capture path | Core `Ritual` classification or the owning adapter's External submission. |
| Outcome | ritual participant/role fan-out page; label **Alpha Memes funerals**. |
| Setup/trigger | Complete a matching ritual with at least one diary-eligible participant. |
| Matcher inventory | exact —; ordinal —; prefixes `AM_AnimaBurial`, `AM_BlastOffFuneral`, `AM_CremateFuneral`, `AM_CremateFuneralNoCorpse`, `AM_DreadnoughtCryptoFuneral`, `AM_DreadnoughtFuneral`, `AM_FleshCraftingFuneral`, `AM_FuneralNoCorpse`, `AM_InsectoidBurial`, `AM_Mummification`, `AM_OcularFuneral`, `AM_PastedFuneral`, `AM_PyramidBurial`, `AM_RumBurial`, `AM_SkyBurial`, `GR_ExtractorFuneral`; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml) — `alphamemes_funeral` |

<!-- repowiki:compat-group {"defName":"alphamemes_rituals","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":["AM_"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Sarg.AlphaMemes"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml"} -->
## Compatibility group: `alphamemes_rituals`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Sarg.AlphaMemes` |
| Capture path | Core `Ritual` classification or the owning adapter's External submission. |
| Outcome | ritual participant/role fan-out page; label **Alpha Memes rituals**. |
| Setup/trigger | Complete a matching ritual with at least one diary-eligible participant. |
| Matcher inventory | exact —; ordinal —; prefixes `AM_`; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml) — `alphamemes_rituals` |

<!-- repowiki:compat-group {"defName":"alphamemes_thoughts","domain":"Thought","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":["AM_"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Sarg.AlphaMemes"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml"} -->
## Compatibility group: `alphamemes_thoughts`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Sarg.AlphaMemes` |
| Capture path | Core `Thought` classification or the owning adapter's External submission. |
| Outcome | immediate solo or route-owned page when the source is admitted; label **Alpha Memes aftereffects**. |
| Setup/trigger | Give an eligible colonist a matching thought/memory and allow the thought hook or scanner to observe it. |
| Matcher inventory | exact —; ordinal —; prefixes `AM_`; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml) — `alphamemes_thoughts` |

<!-- repowiki:compat-group {"defName":"alphamemes_baptism","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["AM_Speech_Baptism"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Sarg.AlphaMemes"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml"} -->
## Compatibility group: `alphamemes_baptism`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Sarg.AlphaMemes` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page; label **Baptism speeches**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `AM_Speech_Baptism`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml) — `alphamemes_baptism` |

<!-- repowiki:compat-group {"defName":"alphamemes_hediffs","domain":"Hediff","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["AM_CatharsisHediff","AM_IconoclastHediff"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Sarg.AlphaMemes"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml"} -->
## Compatibility group: `alphamemes_hediffs`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Sarg.AlphaMemes` |
| Capture path | Core `Hediff` classification or the owning adapter's External submission. |
| Outcome | reflection evidence; appears in a later day reflection; label **Ritual fervor**. |
| Setup/trigger | Apply or progress a matching health condition; the nested health policy decides immediate page versus reflection evidence. |
| Matcher inventory | exact `AM_CatharsisHediff`, `AM_IconoclastHediff`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml) — `alphamemes_hediffs` |

<!-- repowiki:compat-group {"defName":"hospitality_guestwork","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"HospitalityGuestworkAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":["GuestDiplomacy","CharmGuestAttempt"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Orion.Hospitality"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_Hospitality.xml"} -->
## Compatibility group: `hospitality_guestwork`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Orion.Hospitality` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **Hosting guests**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `GuestDiplomacy`, `CharmGuestAttempt`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_Hospitality.xml](../../../1.6/Defs/Compat/DiaryCompat_Hospitality.xml) — `hospitality_guestwork` |

<!-- repowiki:compat-group {"defName":"hospitality_scrounge","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["ScroungeFoodAttempt"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Orion.Hospitality"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_Hospitality.xml"} -->
## Compatibility group: `hospitality_scrounge`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Orion.Hospitality` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page; label **Guests scrounging food**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `ScroungeFoodAttempt`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_Hospitality.xml](../../../1.6/Defs/Compat/DiaryCompat_Hospitality.xml) — `hospitality_scrounge` |

<!-- repowiki:compat-group {"defName":"rimpsyche_chatter","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"RimpsycheAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":["Rimpsyche_"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Maux36.Rimpsyche"],"disableWhenPackageIdsLoaded":["aimmlegate.pawndiary.adapter.rimpsyche"],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_Rimpsyche.xml"} -->
## Compatibility group: `rimpsyche_chatter`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Maux36.Rimpsyche` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **Rimpsyche conversations**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact —; ordinal —; prefixes `Rimpsyche_`; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_Rimpsyche.xml](../../../1.6/Defs/Compat/DiaryCompat_Rimpsyche.xml) — `rimpsyche_chatter` |

<!-- repowiki:compat-group {"defName":"rimpsyche_afterfeel","domain":"Thought","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":["Maux36.Rimpsyche"],"enableWhenPackageIdsLoaded":["Maux36.Rimpsyche"],"disableWhenPackageIdsLoaded":["aimmlegate.pawndiary.adapter.rimpsyche"],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_Rimpsyche.xml"} -->
## Compatibility group: `rimpsyche_afterfeel`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Maux36.Rimpsyche` |
| Capture path | Core `Thought` classification or the owning adapter's External submission. |
| Outcome | immediate solo or route-owned page when the source is admitted; label **conversation afterfeel**. |
| Setup/trigger | Give an eligible colonist a matching thought/memory and allow the thought hook or scanner to observe it. |
| Matcher inventory | exact —; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages `Maux36.Rimpsyche`; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_Rimpsyche.xml](../../../1.6/Defs/Compat/DiaryCompat_Rimpsyche.xml) — `rimpsyche_afterfeel` |

<!-- repowiki:compat-group {"defName":"rimtalk_chatter","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"RimTalkAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":["RimTalkInteraction"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":["cj.rimtalk"],"enableWhenPackageIdsLoaded":["cj.rimtalk"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":["aimmlegate.pawndiary.rimtalkbridge.displayed-conversation","aimmlegate.pawndiary.rimtalkbridge.conversation-capture-not-requested"],"sourceFile":"1.6/Defs/Compat/DiaryCompat_RimTalk.xml"} -->
## Compatibility group: `rimtalk_chatter`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `cj.rimtalk` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **RimTalk conversations**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `RimTalkInteraction`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages `cj.rimtalk`; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_RimTalk.xml](../../../1.6/Defs/Compat/DiaryCompat_RimTalk.xml) — `rimtalk_chatter` |

<!-- repowiki:compat-group {"defName":"speakup_chitchat","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"SpeakUpAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":["JPT.speakup"],"enableWhenPackageIdsLoaded":["JPT.speakup"],"disableWhenPackageIdsLoaded":["aimmlegate.pawndiary.adapter.speakup"],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_SpeakUp.xml"} -->
## Compatibility group: `speakup_chitchat`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `JPT.speakup` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **SpeakUp chatter**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact —; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages `JPT.speakup`; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_SpeakUp.xml](../../../1.6/Defs/Compat/DiaryCompat_SpeakUp.xml) — `speakup_chitchat` |

<!-- repowiki:compat-group {"defName":"vee_raids","domain":"Raid","defaultEnabled":true,"important":true,"combat":true,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["RaidEnemyPurple","InfestationPurple"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VEE"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_VEE.xml"} -->
## Compatibility group: `vee_raids`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Capture path | Core `Raid` classification or the owning adapter's External submission. |
| Outcome | colony/map fan-out: one solo page per admitted colonist; label **VEE purple raids**. |
| Setup/trigger | Trigger the matching raid/infestation incident against a player map. |
| Matcher inventory | exact `RaidEnemyPurple`, `InfestationPurple`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_VEE.xml](../../../1.6/Defs/Compat/DiaryCompat_VEE.xml) — `vee_raids` |

<!-- repowiki:compat-group {"defName":"vee_hediffs","domain":"Hediff","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["VEE_SunSickness","VEE_LongNight","VEE_PsychicHumHediff","VEE_PsychicOverdriveHediff","VEE_PsychicRelaxationHediff","VEE_BloomPsychicSensitivity"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VEE"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_VEE.xml"} -->
## Compatibility group: `vee_hediffs`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Capture path | Core `Hediff` classification or the owning adapter's External submission. |
| Outcome | reflection evidence; appears in a later day reflection; label **VEE ambient conditions**. |
| Setup/trigger | Apply or progress a matching health condition; the nested health policy decides immediate page versus reflection evidence. |
| Matcher inventory | exact `VEE_SunSickness`, `VEE_LongNight`, `VEE_PsychicHumHediff`, `VEE_PsychicOverdriveHediff`, `VEE_PsychicRelaxationHediff`, `VEE_BloomPsychicSensitivity`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_VEE.xml](../../../1.6/Defs/Compat/DiaryCompat_VEE.xml) — `vee_hediffs` |

<!-- repowiki:compat-group {"defName":"viememes_darkrites","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":["VME_CeremonialSuicidePrecept","VME_PlagueFestivalPrecept","VME_ViolentConversionPrecept","VME_WickerManBurningPrecept"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VMemesE"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_VIEMemes.xml"} -->
## Compatibility group: `viememes_darkrites`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VMemesE` |
| Capture path | Core `Ritual` classification or the owning adapter's External submission. |
| Outcome | ritual participant/role fan-out page; label **VIE dark rites**. |
| Setup/trigger | Complete a matching ritual with at least one diary-eligible participant. |
| Matcher inventory | exact —; ordinal —; prefixes `VME_CeremonialSuicidePrecept`, `VME_PlagueFestivalPrecept`, `VME_ViolentConversionPrecept`, `VME_WickerManBurningPrecept`; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_VIEMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_VIEMemes.xml) — `viememes_darkrites` |

<!-- repowiki:compat-group {"defName":"viememes_rituals","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":["VME_"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VMemesE"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_VIEMemes.xml"} -->
## Compatibility group: `viememes_rituals`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VMemesE` |
| Capture path | Core `Ritual` classification or the owning adapter's External submission. |
| Outcome | ritual participant/role fan-out page; label **VIE festivals and rites**. |
| Setup/trigger | Complete a matching ritual with at least one diary-eligible participant. |
| Matcher inventory | exact —; ordinal —; prefixes `VME_`; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_VIEMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_VIEMemes.xml) — `viememes_rituals` |

<!-- repowiki:compat-group {"defName":"viememes_thoughts","domain":"Thought","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":["VME_"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VMemesE"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_VIEMemes.xml"} -->
## Compatibility group: `viememes_thoughts`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VMemesE` |
| Capture path | Core `Thought` classification or the owning adapter's External submission. |
| Outcome | immediate solo or route-owned page when the source is admitted; label **VIE ritual aftereffects**. |
| Setup/trigger | Give an eligible colonist a matching thought/memory and allow the thought hook or scanner to observe it. |
| Matcher inventory | exact —; ordinal —; prefixes `VME_`; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_VIEMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_VIEMemes.xml) — `viememes_thoughts` |

<!-- repowiki:compat-group {"defName":"viememes_interrogation","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["VFEA_InterrogatePrisoner","VFEA_InterrogationSuccess","VFEA_InterrogationRefused","VFEA_Intimidate"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VMemesE"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_VIEMemes.xml"} -->
## Compatibility group: `viememes_interrogation`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VMemesE` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page; label **Interrogations**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `VFEA_InterrogatePrisoner`, `VFEA_InterrogationSuccess`, `VFEA_InterrogationRefused`, `VFEA_Intimidate`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_VIEMemes.xml](../../../1.6/Defs/Compat/DiaryCompat_VIEMemes.xml) — `viememes_interrogation` |

<!-- repowiki:compat-group {"defName":"vte_thoughts","domain":"Thought","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["VTE_BondedAnimalBanishedHater","VTE_BondedAnimalDiedHater","VTE_BondedAnimalLostHater","VTE_CouldNotFinishItem","VTE_CreatedLowQualityItem","VTE_HarvestedOrgans","VTE_MechanoidIsKilled","VTE_NuzzledHater","VTE_ObservedManyBlood","VTE_SoakingWetChildOfTheSea","VTE_SoldMyBondedAnimalMoodHater","VTE_WatchedTelevisor"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VanillaTraitsExpanded"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_VTE.xml"} -->
## Compatibility group: `vte_thoughts`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VanillaTraitsExpanded` |
| Capture path | Core `Thought` classification or the owning adapter's External submission. |
| Outcome | immediate solo or route-owned page when the source is admitted; label **Trait-shaped memories**. |
| Setup/trigger | Give an eligible colonist a matching thought/memory and allow the thought hook or scanner to observe it. |
| Matcher inventory | exact `VTE_BondedAnimalBanishedHater`, `VTE_BondedAnimalDiedHater`, `VTE_BondedAnimalLostHater`, `VTE_CouldNotFinishItem`, `VTE_CreatedLowQualityItem`, `VTE_HarvestedOrgans`, `VTE_MechanoidIsKilled`, `VTE_NuzzledHater`, `VTE_ObservedManyBlood`, `VTE_SoakingWetChildOfTheSea`, `VTE_SoldMyBondedAnimalMoodHater`, `VTE_WatchedTelevisor`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_VTE.xml](../../../1.6/Defs/Compat/DiaryCompat_VTE.xml) — `vte_thoughts` |

<!-- repowiki:compat-group {"defName":"vte_mentalbreaks","domain":"MentalState","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["VTE_Kleptomaniac","VTE_TechnophobeTantrum","VTE_PanicFreezing"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VanillaTraitsExpanded"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_VTE.xml"} -->
## Compatibility group: `vte_mentalbreaks`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VanillaTraitsExpanded` |
| Capture path | Core `MentalState` classification or the owning adapter's External submission. |
| Outcome | immediate solo or route-owned page when the source is admitted; label **Trait-driven mental breaks**. |
| Setup/trigger | Start a matching mental state (Dev Mode mental-state tools are the most reproducible route). |
| Matcher inventory | exact `VTE_Kleptomaniac`, `VTE_TechnophobeTantrum`, `VTE_PanicFreezing`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_VTE.xml](../../../1.6/Defs/Compat/DiaryCompat_VTE.xml) — `vte_mentalbreaks` |

<!-- repowiki:compat-group {"defName":"wbr_hookup","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["TriedHookupWith"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["divineDerivative.Romance"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml"} -->
## Compatibility group: `wbr_hookup`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `divineDerivative.Romance` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page; label **Hookup attempts**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `TriedHookupWith`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml](../../../1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml) — `wbr_hookup` |

<!-- repowiki:compat-group {"defName":"wbr_askedout","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"WbrAskedOutAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":["AskedForDate","AskedForHangout"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["divineDerivative.Romance"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml"} -->
## Compatibility group: `wbr_askedout`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `divineDerivative.Romance` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **Date and hangout invitations**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `AskedForDate`, `AskedForHangout`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml](../../../1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml) — `wbr_askedout` |

<!-- repowiki:compat-group {"defName":"wbr_thoughts","domain":"Thought","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["RebuffedMyHookupAttempt","RebuffedMyHookupAttemptMood","FailedHookupAttemptOnMe","RebuffedMyDateAttempt","RebuffedMyDateAttemptMood","FailedDateAttemptOnMe","RebuffedMyHangoutAttempt","RebuffedMyHangoutAttemptMood","FailedHangoutAttemptOnMe","GotSomeLovinAsexual","LovinAsexualPositive","LovinAsexualNegative","PassionateLovinAsexualPositive","PassionateLovinAsexualNegative"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":["divineDerivative.Romance"],"enableWhenPackageIdsLoaded":["divineDerivative.Romance"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml"} -->
## Compatibility group: `wbr_thoughts`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `divineDerivative.Romance` |
| Capture path | Core `Thought` classification or the owning adapter's External submission. |
| Outcome | immediate solo or route-owned page when the source is admitted; label **Romantic aftermath**. |
| Setup/trigger | Give an eligible colonist a matching thought/memory and allow the thought hook or scanner to observe it. |
| Matcher inventory | exact `RebuffedMyHookupAttempt`, `RebuffedMyHookupAttemptMood`, `FailedHookupAttemptOnMe`, `RebuffedMyDateAttempt`, `RebuffedMyDateAttemptMood`, `FailedDateAttemptOnMe`, `RebuffedMyHangoutAttempt`, `RebuffedMyHangoutAttemptMood`, `FailedHangoutAttemptOnMe`, `GotSomeLovinAsexual`, `LovinAsexualPositive`, `LovinAsexualNegative`, `PassionateLovinAsexualPositive`, `PassionateLovinAsexualNegative`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages `divineDerivative.Romance`; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml](../../../1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml) — `wbr_thoughts` |

<!-- repowiki:compat-window {"defName":"HospitalityGuestsArrived","label":"guests arrived","windowKey":"HospitalityGuestsArrived","enabled":true,"enableWhenPackageIdsLoaded":["Orion.Hospitality"],"timeoutTicks":-1,"dedupTicks":30000,"restartOnStart":false,"keepActive":false,"recordScope":"MapWitness","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"Incident","signal":"executed","matchDefNames":["VisitorGroup","VisitorGroupMax","VisitorGroupSelectFaction"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[],"sourceFile":"1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml"} -->
## Compatibility window: `HospitalityGuestsArrived`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Orion.Hospitality` |
| Capture path | Start `Incident/executed`; exact `VisitorGroup`, `VisitorGroupMax`, `VisitorGroupSelectFaction`; tokens —; end —. |
| Outcome | one-shot or bounded page phase; no prompt context; page phases start=yes, end=no, timeout=no. |
| Setup/trigger | Emit the exact package-owned boundary signal and observe the configured page/context until its end or timeout. |
| Dependency absent | The package gate rejects starts and stale saved state cleanly. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml](../../../1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml) — `HospitalityGuestsArrived` |

<!-- repowiki:compat-window {"defName":"HospitalityGuestJoined","label":"a guest asked to join","windowKey":"HospitalityGuestJoined","enabled":true,"enableWhenPackageIdsLoaded":["Orion.Hospitality"],"timeoutTicks":-1,"dedupTicks":30000,"restartOnStart":false,"keepActive":false,"recordScope":"MapWitness","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"Incident","signal":"executed","matchDefNames":["HappyGuestJoins"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[],"sourceFile":"1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml"} -->
## Compatibility window: `HospitalityGuestJoined`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Orion.Hospitality` |
| Capture path | Start `Incident/executed`; exact `HappyGuestJoins`; tokens —; end —. |
| Outcome | one-shot or bounded page phase; no prompt context; page phases start=yes, end=no, timeout=no. |
| Setup/trigger | Emit the exact package-owned boundary signal and observe the configured page/context until its end or timeout. |
| Dependency absent | The package gate rejects starts and stale saved state cleanly. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml](../../../1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml) — `HospitalityGuestJoined` |

<!-- repowiki:compat-group {"defName":"eventWindowHospitalityGuestsArrived","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["HospitalityGuestsArrived"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Orion.Hospitality"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml"} -->
## Compatibility group: `eventWindowHospitalityGuestsArrived`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Orion.Hospitality` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page; label **Guests arrived**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `HospitalityGuestsArrived`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml](../../../1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml) — `eventWindowHospitalityGuestsArrived` |

<!-- repowiki:compat-group {"defName":"eventWindowHospitalityGuestJoined","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["HospitalityGuestJoined"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Orion.Hospitality"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml"} -->
## Compatibility group: `eventWindowHospitalityGuestJoined`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Orion.Hospitality` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page; label **A guest asked to join**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `HospitalityGuestJoined`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml](../../../1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml) — `eventWindowHospitalityGuestJoined` |

<!-- repowiki:compat-window {"defName":"VeeEarthquake","label":"VEE earthquake","windowKey":"VeeEarthquake","enabled":true,"enableWhenPackageIdsLoaded":["VanillaExpanded.VEE"],"timeoutTicks":-1,"dedupTicks":2500,"restartOnStart":false,"keepActive":false,"recordScope":"MapWitness","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"Incident","signal":"executed","matchDefNames":["VEE_Earthquake"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[],"sourceFile":"1.6/Defs/Compat/DiaryEventWindows_VEE.xml"} -->
## Compatibility window: `VeeEarthquake`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Capture path | Start `Incident/executed`; exact `VEE_Earthquake`; tokens —; end —. |
| Outcome | one-shot or bounded page phase; no prompt context; page phases start=yes, end=no, timeout=no. |
| Setup/trigger | Emit the exact package-owned boundary signal and observe the configured page/context until its end or timeout. |
| Dependency absent | The package gate rejects starts and stale saved state cleanly. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) — `VeeEarthquake` |

<!-- repowiki:compat-window {"defName":"VeeMeteoriteShower","label":"VEE meteorite shower","windowKey":"VeeMeteoriteShower","enabled":true,"enableWhenPackageIdsLoaded":["VanillaExpanded.VEE"],"timeoutTicks":-1,"dedupTicks":2500,"restartOnStart":false,"keepActive":false,"recordScope":"MapWitness","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"Incident","signal":"executed","matchDefNames":["VEE_MeteoriteShower"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[],"sourceFile":"1.6/Defs/Compat/DiaryEventWindows_VEE.xml"} -->
## Compatibility window: `VeeMeteoriteShower`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Capture path | Start `Incident/executed`; exact `VEE_MeteoriteShower`; tokens —; end —. |
| Outcome | one-shot or bounded page phase; no prompt context; page phases start=yes, end=no, timeout=no. |
| Setup/trigger | Emit the exact package-owned boundary signal and observe the configured page/context until its end or timeout. |
| Dependency absent | The package gate rejects starts and stale saved state cleanly. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) — `VeeMeteoriteShower` |

<!-- repowiki:compat-window {"defName":"VeeSpaceBattle","label":"VEE orbital disaster","windowKey":"VeeSpaceBattle","enabled":true,"enableWhenPackageIdsLoaded":["VanillaExpanded.VEE"],"timeoutTicks":-1,"dedupTicks":2500,"restartOnStart":false,"keepActive":false,"recordScope":"MapWitness","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"Incident","signal":"executed","matchDefNames":["VEE_SpaceBattle","VEE_ShuttleCrash"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[],"sourceFile":"1.6/Defs/Compat/DiaryEventWindows_VEE.xml"} -->
## Compatibility window: `VeeSpaceBattle`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Capture path | Start `Incident/executed`; exact `VEE_SpaceBattle`, `VEE_ShuttleCrash`; tokens —; end —. |
| Outcome | one-shot or bounded page phase; no prompt context; page phases start=yes, end=no, timeout=no. |
| Setup/trigger | Emit the exact package-owned boundary signal and observe the configured page/context until its end or timeout. |
| Dependency absent | The package gate rejects starts and stale saved state cleanly. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) — `VeeSpaceBattle` |

<!-- repowiki:compat-window {"defName":"VeeStampede","label":"VEE mass animal attack","windowKey":"VeeStampede","enabled":true,"enableWhenPackageIdsLoaded":["VanillaExpanded.VEE"],"timeoutTicks":-1,"dedupTicks":2500,"restartOnStart":false,"keepActive":false,"recordScope":"MapWitness","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"Incident","signal":"executed","matchDefNames":["ManhunterPackPurple","AnimalInsanityMassPurple"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[],"sourceFile":"1.6/Defs/Compat/DiaryEventWindows_VEE.xml"} -->
## Compatibility window: `VeeStampede`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Capture path | Start `Incident/executed`; exact `ManhunterPackPurple`, `AnimalInsanityMassPurple`; tokens —; end —. |
| Outcome | one-shot or bounded page phase; no prompt context; page phases start=yes, end=no, timeout=no. |
| Setup/trigger | Emit the exact package-owned boundary signal and observe the configured page/context until its end or timeout. |
| Dependency absent | The package gate rejects starts and stale saved state cleanly. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) — `VeeStampede` |

<!-- repowiki:compat-group {"defName":"eventWindowVeeEarthquake","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["VEE_Earthquake"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VEE"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryEventWindows_VEE.xml"} -->
## Compatibility group: `eventWindowVeeEarthquake`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page; label **Earthquake**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `VEE_Earthquake`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) — `eventWindowVeeEarthquake` |

<!-- repowiki:compat-group {"defName":"eventWindowVeeMeteoriteShower","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["VEE_MeteoriteShower"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VEE"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryEventWindows_VEE.xml"} -->
## Compatibility group: `eventWindowVeeMeteoriteShower`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page; label **Meteorite shower**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `VEE_MeteoriteShower`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) — `eventWindowVeeMeteoriteShower` |

<!-- repowiki:compat-group {"defName":"eventWindowVeeSpaceBattle","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["VEE_SpaceBattle","VEE_ShuttleCrash"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VEE"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryEventWindows_VEE.xml"} -->
## Compatibility group: `eventWindowVeeSpaceBattle`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page; label **Orbital disaster**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `VEE_SpaceBattle`, `VEE_ShuttleCrash`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) — `eventWindowVeeSpaceBattle` |

<!-- repowiki:compat-group {"defName":"eventWindowVeeStampede","domain":"Interaction","defaultEnabled":true,"important":true,"combat":true,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["ManhunterPackPurple","AnimalInsanityMassPurple"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VEE"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"1.6/Defs/Compat/DiaryEventWindows_VEE.xml"} -->
## Compatibility group: `eventWindowVeeStampede`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page; label **Mass animal attack**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `ManhunterPackPurple`, `AnimalInsanityMassPurple`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryEventWindows_VEE.xml](../../../1.6/Defs/Compat/DiaryEventWindows_VEE.xml) — `eventWindowVeeStampede` |

<!-- repowiki:compat-condition {"defName":"VeeDroughtActive","label":"VEE drought","conditionKey":"VeeDroughtActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["VEE_Drought"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"VanillaExpanded.VEE","sourceFile":"1.6/Defs/Compat/DiaryObservedConditions_VEE.xml"} -->
## Compatibility condition: `VeeDroughtActive`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Detection | Scheduled active GameCondition comparison; exact names: `VEE_Drought`. |
| Outcome | prompt-only observation. |
| Setup/trigger | Start the exact package-owned lasting condition and keep it active through the debounce. |
| Dependency absent | The MayRequire gate or absent exact string match leaves the observer inert. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryObservedConditions_VEE.xml](../../../1.6/Defs/Compat/DiaryObservedConditions_VEE.xml) — `VeeDroughtActive` |

<!-- repowiki:compat-condition {"defName":"VeeLongNightActive","label":"VEE long night","conditionKey":"VeeLongNightActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["VEE_LongNight"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"VanillaExpanded.VEE","sourceFile":"1.6/Defs/Compat/DiaryObservedConditions_VEE.xml"} -->
## Compatibility condition: `VeeLongNightActive`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Detection | Scheduled active GameCondition comparison; exact names: `VEE_LongNight`. |
| Outcome | prompt-only observation. |
| Setup/trigger | Start the exact package-owned lasting condition and keep it active through the debounce. |
| Dependency absent | The MayRequire gate or absent exact string match leaves the observer inert. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryObservedConditions_VEE.xml](../../../1.6/Defs/Compat/DiaryObservedConditions_VEE.xml) — `VeeLongNightActive` |

<!-- repowiki:compat-condition {"defName":"VeeScorchActive","label":"VEE scorch","conditionKey":"VeeScorchActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["VEE_Scorch"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"VanillaExpanded.VEE","sourceFile":"1.6/Defs/Compat/DiaryObservedConditions_VEE.xml"} -->
## Compatibility condition: `VeeScorchActive`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Detection | Scheduled active GameCondition comparison; exact names: `VEE_Scorch`. |
| Outcome | prompt-only observation. |
| Setup/trigger | Start the exact package-owned lasting condition and keep it active through the debounce. |
| Dependency absent | The MayRequire gate or absent exact string match leaves the observer inert. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryObservedConditions_VEE.xml](../../../1.6/Defs/Compat/DiaryObservedConditions_VEE.xml) — `VeeScorchActive` |

<!-- repowiki:compat-condition {"defName":"VeeWhiteoutActive","label":"VEE whiteout","conditionKey":"VeeWhiteoutActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["VEE_Whiteout"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"VanillaExpanded.VEE","sourceFile":"1.6/Defs/Compat/DiaryObservedConditions_VEE.xml"} -->
## Compatibility condition: `VeeWhiteoutActive`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Detection | Scheduled active GameCondition comparison; exact names: `VEE_Whiteout`. |
| Outcome | prompt-only observation. |
| Setup/trigger | Start the exact package-owned lasting condition and keep it active through the debounce. |
| Dependency absent | The MayRequire gate or absent exact string match leaves the observer inert. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryObservedConditions_VEE.xml](../../../1.6/Defs/Compat/DiaryObservedConditions_VEE.xml) — `VeeWhiteoutActive` |

<!-- repowiki:compat-condition {"defName":"VeePsychicBloomActive","label":"VEE psychic bloom","conditionKey":"VeePsychicBloomActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["VEE_PsychicBloom"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"VanillaExpanded.VEE","sourceFile":"1.6/Defs/Compat/DiaryObservedConditions_VEE.xml"} -->
## Compatibility condition: `VeePsychicBloomActive`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Detection | Scheduled active GameCondition comparison; exact names: `VEE_PsychicBloom`. |
| Outcome | prompt-only observation. |
| Setup/trigger | Start the exact package-owned lasting condition and keep it active through the debounce. |
| Dependency absent | The MayRequire gate or absent exact string match leaves the observer inert. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryObservedConditions_VEE.xml](../../../1.6/Defs/Compat/DiaryObservedConditions_VEE.xml) — `VeePsychicBloomActive` |

<!-- repowiki:compat-condition {"defName":"VeePsychicHumActive","label":"VEE psychic pressure","conditionKey":"VeePsychicHumActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["VEE_PsychicHum","VEE_PsychicOverdrive","VEE_PsychicStimulation","PsychicRain"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"VanillaExpanded.VEE","sourceFile":"1.6/Defs/Compat/DiaryObservedConditions_VEE.xml"} -->
## Compatibility condition: `VeePsychicHumActive`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VEE` |
| Detection | Scheduled active GameCondition comparison; exact names: `VEE_PsychicHum`, `VEE_PsychicOverdrive`, `VEE_PsychicStimulation`, `PsychicRain`. |
| Outcome | prompt-only observation. |
| Setup/trigger | Start the exact package-owned lasting condition and keep it active through the debounce. |
| Dependency absent | The MayRequire gate or absent exact string match leaves the observer inert. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [1.6/Defs/Compat/DiaryObservedConditions_VEE.xml](../../../1.6/Defs/Compat/DiaryObservedConditions_VEE.xml) — `VeePsychicHumActive` |

<!-- repowiki:compat-group {"defName":"exampleAdapterQuietMoment","domain":"External","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["exampleadapter_quiet_moment"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml"} -->
## Compatibility group: `exampleAdapterQuietMoment`

| Test field | Expected behavior |
|---|---|
| Required package(s) | Owning adapter mod |
| Capture path | Core `External` classification or the owning adapter's External submission. |
| Outcome | adapter-submitted page using the request shape chosen by the adapter; label **quiet moment**. |
| Setup/trigger | Submit the exact event key through a shipped adapter or `PawnDiaryApi`. |
| Matcher inventory | exact `exampleadapter_quiet_moment`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml](../../../integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml) — `exampleAdapterQuietMoment` |

<!-- repowiki:compat-group {"defName":"exampleAdapterPromptIdea","domain":"External","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["exampleadapter_prompt_idea"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml"} -->
## Compatibility group: `exampleAdapterPromptIdea`

| Test field | Expected behavior |
|---|---|
| Required package(s) | Owning adapter mod |
| Capture path | Core `External` classification or the owning adapter's External submission. |
| Outcome | adapter-submitted page using the request shape chosen by the adapter; label **prompt idea**. |
| Setup/trigger | Submit the exact event key through a shipped adapter or `PawnDiaryApi`. |
| Matcher inventory | exact `exampleadapter_prompt_idea`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml](../../../integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml) — `exampleAdapterPromptIdea` |

<!-- repowiki:compat-group {"defName":"exampleAdapterDirectNote","domain":"External","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["exampleadapter_direct_note"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml"} -->
## Compatibility group: `exampleAdapterDirectNote`

| Test field | Expected behavior |
|---|---|
| Required package(s) | Owning adapter mod |
| Capture path | Core `External` classification or the owning adapter's External submission. |
| Outcome | adapter-submitted page using the request shape chosen by the adapter; label **direct note**. |
| Setup/trigger | Submit the exact event key through a shipped adapter or `PawnDiaryApi`. |
| Matcher inventory | exact `exampleadapter_direct_note`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml](../../../integrations/PawnDiary.ExampleAdapter/1.6/Defs/DiaryExternalGroups_Example.xml) — `exampleAdapterDirectNote` |

<!-- repowiki:compat-group {"defName":"personalities123_thoughts","domain":"Thought","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":["hahkethomemah.simplepersonalities","hahkethomemah.simplepersonalities.module2"],"enableWhenPackageIdsLoaded":["hahkethomemah.simplepersonalities","hahkethomemah.simplepersonalities.module2"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.PersonalitiesBridge/1.6/Defs/DiaryCompat_123Personalities.xml"} -->
## Compatibility group: `personalities123_thoughts`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `hahkethomemah.simplepersonalities`; `hahkethomemah.simplepersonalities.module2` |
| Capture path | Core `Thought` classification or the owning adapter's External submission. |
| Outcome | immediate solo or route-owned page when the source is admitted; label **personality moods**. |
| Setup/trigger | Give an eligible colonist a matching thought/memory and allow the thought hook or scanner to observe it. |
| Matcher inventory | exact —; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages `hahkethomemah.simplepersonalities`, `hahkethomemah.simplepersonalities.module2`; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.PersonalitiesBridge/1.6/Defs/DiaryCompat_123Personalities.xml](../../../integrations/PawnDiary.PersonalitiesBridge/1.6/Defs/DiaryCompat_123Personalities.xml) — `personalities123_thoughts` |

<!-- repowiki:compat-group {"defName":"personalities123_interactions","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":["hahkethomemah.simplepersonalities.module2"],"enableWhenPackageIdsLoaded":["hahkethomemah.simplepersonalities.module2"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.PersonalitiesBridge/1.6/Defs/DiaryCompat_123Personalities.xml"} -->
## Compatibility group: `personalities123_interactions`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `hahkethomemah.simplepersonalities.module2` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page; label **personality chemistry**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact —; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages `hahkethomemah.simplepersonalities.module2`; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.PersonalitiesBridge/1.6/Defs/DiaryCompat_123Personalities.xml](../../../integrations/PawnDiary.PersonalitiesBridge/1.6/Defs/DiaryCompat_123Personalities.xml) — `personalities123_interactions` |

<!-- repowiki:compat-group {"defName":"rimpsyche_conversations","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"RimpsycheAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":["Rimpsyche_"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Maux36.Rimpsyche"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryCompat_Rimpsyche.xml"} -->
## Compatibility group: `rimpsyche_conversations`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Maux36.Rimpsyche` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **Rimpsyche conversations**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact —; ordinal —; prefixes `Rimpsyche_`; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryCompat_Rimpsyche.xml](../../../integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryCompat_Rimpsyche.xml) — `rimpsyche_conversations` |

<!-- repowiki:compat-group {"defName":"rimpsyche_thoughts","domain":"Thought","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":["Maux36.Rimpsyche"],"enableWhenPackageIdsLoaded":["Maux36.Rimpsyche"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryCompat_Rimpsyche.xml"} -->
## Compatibility group: `rimpsyche_thoughts`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Maux36.Rimpsyche` |
| Capture path | Core `Thought` classification or the owning adapter's External submission. |
| Outcome | immediate solo or route-owned page when the source is admitted; label **conversation afterfeel**. |
| Setup/trigger | Give an eligible colonist a matching thought/memory and allow the thought hook or scanner to observe it. |
| Matcher inventory | exact —; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages `Maux36.Rimpsyche`; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryCompat_Rimpsyche.xml](../../../integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryCompat_Rimpsyche.xml) — `rimpsyche_thoughts` |

<!-- repowiki:compat-group {"defName":"rimpsyche_charged_conversation","domain":"External","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["rimpsyche_conversation"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Maux36.Rimpsyche"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryExternalGroups_Rimpsyche.xml"} -->
## Compatibility group: `rimpsyche_charged_conversation`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `Maux36.Rimpsyche` |
| Capture path | Core `External` classification or the owning adapter's External submission. |
| Outcome | adapter-submitted page using the request shape chosen by the adapter; label **charged conversation**. |
| Setup/trigger | Submit the exact event key through a shipped adapter or `PawnDiaryApi`. |
| Matcher inventory | exact `rimpsyche_conversation`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryExternalGroups_Rimpsyche.xml](../../../integrations/PawnDiary.RimpsycheBridge/1.6/Defs/DiaryExternalGroups_Rimpsyche.xml) — `rimpsyche_charged_conversation` |

<!-- repowiki:compat-group {"defName":"rimtalkbridgeConversation","domain":"External","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["rimtalkbridge_conversation"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.RimTalkBridge/1.6/Defs/DiaryExternalGroups_RimTalkBridge.xml"} -->
## Compatibility group: `rimtalkbridgeConversation`

| Test field | Expected behavior |
|---|---|
| Required package(s) | Owning adapter mod |
| Capture path | Core `External` classification or the owning adapter's External submission. |
| Outcome | adapter-submitted page using the request shape chosen by the adapter; label **notable conversations**. |
| Setup/trigger | Submit the exact event key through a shipped adapter or `PawnDiaryApi`. |
| Matcher inventory | exact `rimtalkbridge_conversation`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.RimTalkBridge/1.6/Defs/DiaryExternalGroups_RimTalkBridge.xml](../../../integrations/PawnDiary.RimTalkBridge/1.6/Defs/DiaryExternalGroups_RimTalkBridge.xml) — `rimtalkbridgeConversation` |

<!-- repowiki:compat-group {"defName":"speakupbridge_deeptalk","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"SpeakUpBridgeDeepTalkAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":["DeepTalkConvo","DeepTalkConvoResponse","MeaningOfLife","ChildhoodDiscussions","Dream_nice","PsyVision","PsyVisionGood","PsyVisionBad"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["JPT.speakup"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml"} -->
## Compatibility group: `speakupbridge_deeptalk`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `JPT.speakup` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **SpeakUp deep talks**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `DeepTalkConvo`, `DeepTalkConvoResponse`, `MeaningOfLife`, `ChildhoodDiscussions`, `Dream_nice`, `PsyVision`, `PsyVisionGood`, `PsyVisionBad`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml](../../../integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml) — `speakupbridge_deeptalk` |

<!-- repowiki:compat-group {"defName":"speakupbridge_jokes","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"SpeakUpBridgeJokesAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":["JokeReaction"],"matchOrdinalDefNames":[],"matchPrefixes":["Joke_"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["JPT.speakup"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml"} -->
## Compatibility group: `speakupbridge_jokes`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `JPT.speakup` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **SpeakUp jokes**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `JokeReaction`; ordinal —; prefixes `Joke_`; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml](../../../integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml) — `speakupbridge_jokes` |

<!-- repowiki:compat-group {"defName":"speakupbridge_prisoner","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"SpeakUpBridgePrisonerAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":["PrisonerAccepts","PrisonerAnimalsSkills","PrisonerArtisticSkills","PrisonerBestSkill","PrisonerCV","PrisonerClothes","PrisonerConstructionSkills","PrisonerCookingSkills","PrisonerCraftingSkills","PrisonerEndchat","PrisonerFightingSkills","PrisonerIntellectualSkills","PrisonerMedicalSkills","PrisonerMiningSkills","PrisonerNeed","PrisonerPassion","PrisonerPlantsSkills","PrisonerPsychic","PrisonerRapport","PrisonerRefuses","PrisonerSocialSkills"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["JPT.speakup"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml"} -->
## Compatibility group: `speakupbridge_prisoner`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `JPT.speakup` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **SpeakUp prisoner talks**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `PrisonerAccepts`, `PrisonerAnimalsSkills`, `PrisonerArtisticSkills`, `PrisonerBestSkill`, `PrisonerCV`, `PrisonerClothes`, `PrisonerConstructionSkills`, `PrisonerCookingSkills`, `PrisonerCraftingSkills`, `PrisonerEndchat`, `PrisonerFightingSkills`, `PrisonerIntellectualSkills`, `PrisonerMedicalSkills`, `PrisonerMiningSkills`, `PrisonerNeed`, `PrisonerPassion`, `PrisonerPlantsSkills`, `PrisonerPsychic`, `PrisonerRapport`, `PrisonerRefuses`, `PrisonerSocialSkills`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml](../../../integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml) — `speakupbridge_prisoner` |

<!-- repowiki:compat-group {"defName":"speakupbridge_reactions","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"SpeakUpBridgeReactionsAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":["ReactToThought"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["JPT.speakup"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml"} -->
## Compatibility group: `speakupbridge_reactions`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `JPT.speakup` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **SpeakUp thought reactions**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact —; ordinal —; prefixes `ReactToThought`; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml](../../../integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml) — `speakupbridge_reactions` |

<!-- repowiki:compat-group {"defName":"speakupbridge_chatter","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"SpeakUpBridgeAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":["JPT.speakup"],"enableWhenPackageIdsLoaded":["JPT.speakup"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml"} -->
## Compatibility group: `speakupbridge_chatter`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `JPT.speakup` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **SpeakUp chatter**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact —; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages `JPT.speakup`; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml](../../../integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryCompat_SpeakUp_Groups.xml) — `speakupbridge_chatter` |

<!-- repowiki:compat-group {"defName":"speakupbridgeConversation","domain":"External","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["speakupbridge_conversation"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["JPT.speakup"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryExternalGroups_SpeakUp.xml"} -->
## Compatibility group: `speakupbridgeConversation`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `JPT.speakup` |
| Capture path | Core `External` classification or the owning adapter's External submission. |
| Outcome | adapter-submitted page using the request shape chosen by the adapter; label **SpeakUp conversation**. |
| Setup/trigger | Submit the exact event key through a shipped adapter or `PawnDiaryApi`. |
| Matcher inventory | exact `speakupbridge_conversation`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryExternalGroups_SpeakUp.xml](../../../integrations/PawnDiary.SpeakUp/1.6/Defs/DiaryExternalGroups_SpeakUp.xml) — `speakupbridgeConversation` |

<!-- repowiki:compat-group {"defName":"vsie_vent","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"VsieVentAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":["VSIE_Vent"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VanillaSocialInteractionsExpanded"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml"} -->
## Compatibility group: `vsie_vent`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VanillaSocialInteractionsExpanded` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **venting**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact `VSIE_Vent`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml](../../../integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml) — `vsie_vent` |

<!-- repowiki:compat-group {"defName":"vsie_teaching","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"VsieTeachingAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":["VSIE_Teaching"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VanillaSocialInteractionsExpanded"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml"} -->
## Compatibility group: `vsie_teaching`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VanillaSocialInteractionsExpanded` |
| Capture path | Core `Interaction` classification or the owning adapter's External submission. |
| Outcome | batched ambient note, normally one solo note per pawn/day; label **teaching & learning**. |
| Setup/trigger | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Matcher inventory | exact —; ordinal —; prefixes `VSIE_Teaching`; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml](../../../integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml) — `vsie_teaching` |

<!-- repowiki:compat-group {"defName":"friendship_relation","domain":"Romance","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["VSIE_BestFriend"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VanillaSocialInteractionsExpanded"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml"} -->
## Compatibility group: `friendship_relation`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VanillaSocialInteractionsExpanded` |
| Capture path | Core `Romance` classification or the owning adapter's External submission. |
| Outcome | paired first-person pages, one independent POV per eligible pawn; label **friendship milestones**. |
| Setup/trigger | Create the matching direct pawn-relation transition. |
| Matcher inventory | exact `VSIE_BestFriend`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml](../../../integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml) — `friendship_relation` |

<!-- repowiki:compat-group {"defName":"vsie_thoughts","domain":"Thought","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":["VanillaExpanded.VanillaSocialInteractionsExpanded"],"enableWhenPackageIdsLoaded":["VanillaExpanded.VanillaSocialInteractionsExpanded"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml"} -->
## Compatibility group: `vsie_thoughts`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VanillaSocialInteractionsExpanded` |
| Capture path | Core `Thought` classification or the owning adapter's External submission. |
| Outcome | immediate solo or route-owned page when the source is admitted; label **social afterthoughts**. |
| Setup/trigger | Give an eligible colonist a matching thought/memory and allow the thought hook or scanner to observe it. |
| Matcher inventory | exact —; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages `VanillaExpanded.VanillaSocialInteractionsExpanded`; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml](../../../integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml) — `vsie_thoughts` |

<!-- repowiki:compat-group {"defName":"vsieBirthdayGathering","domain":"External","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["vsie_birthday"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VanillaSocialInteractionsExpanded"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.Vsie/1.6/Defs/DiaryExternalGroups_Vsie.xml"} -->
## Compatibility group: `vsieBirthdayGathering`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VanillaSocialInteractionsExpanded` |
| Capture path | Core `External` classification or the owning adapter's External submission. |
| Outcome | adapter-submitted page using the request shape chosen by the adapter; label **birthday party**. |
| Setup/trigger | Submit the exact event key through a shipped adapter or `PawnDiaryApi`. |
| Matcher inventory | exact `vsie_birthday`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.Vsie/1.6/Defs/DiaryExternalGroups_Vsie.xml](../../../integrations/PawnDiary.Vsie/1.6/Defs/DiaryExternalGroups_Vsie.xml) — `vsieBirthdayGathering` |

<!-- repowiki:compat-group {"defName":"vsieFuneralGathering","domain":"External","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["vsie_funeral"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["VanillaExpanded.VanillaSocialInteractionsExpanded"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[],"sourceFile":"integrations/PawnDiary.Vsie/1.6/Defs/DiaryExternalGroups_Vsie.xml"} -->
## Compatibility group: `vsieFuneralGathering`

| Test field | Expected behavior |
|---|---|
| Required package(s) | `VanillaExpanded.VanillaSocialInteractionsExpanded` |
| Capture path | Core `External` classification or the owning adapter's External submission. |
| Outcome | adapter-submitted page using the request shape chosen by the adapter; label **funeral**. |
| Setup/trigger | Submit the exact event key through a shipped adapter or `PawnDiaryApi`. |
| Matcher inventory | exact `vsie_funeral`; ordinal —; prefixes —; suffixes —; segments —; tokens —; packages —; catch-all no |
| Dependency absent | Package/capability gates keep the group inert or release the documented core fallback; no external type is required by this XML. |
| Expected observable evidence | The named policy appears in a prompt-test capture, generated card/batch, or adapter status as described above; no evidence is expected when its dependency is absent. |
| Source | [integrations/PawnDiary.Vsie/1.6/Defs/DiaryExternalGroups_Vsie.xml](../../../integrations/PawnDiary.Vsie/1.6/Defs/DiaryExternalGroups_Vsie.xml) — `vsieFuneralGathering` |

## Unknown third-party adapters

Other mods may call the public integration API without living in this repository. Their identifiers cannot be exhaustively listed here. Validate them against [EXTERNAL_API.md](../../../EXTERNAL_API.md), the adapter's own group Defs, and the core `External` runtime entry in [Event catalog](Event-Catalog.md#runtime-source-external).

## Source of truth

- Core compatibility XML: [1.6/Defs/Compat](../../../1.6/Defs/Compat/).
- First-party adapters: [integrations](../../../integrations/).
- Public API: [EXTERNAL_API.md](../../../EXTERNAL_API.md).
