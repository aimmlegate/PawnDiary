# Observed conditions and event windows

These are not a second list of immediate events. An **observed condition** is a lasting state rediscovered by a scheduled comparison. An **event window** is a bounded or one-shot episode opened and optionally closed by named signals.

| Classification | Meaning in a test |
|---|---|
| Prompt-only observation | Changes selected prompt context/tone; normally creates no page. |
| Page-producing observation | A confirmed state transition can create a page. |
| Both | Can create a page and remain eligible as prompt context. |
| Bounded episode | A start/end/timeout policy tracks one episode rather than one raw notification. |

## Core observed-condition index

The XML contains **42 condition elements**: **41 runtime conditions** plus **1 abstract inheritance parent** used only to share Biotech pollution policy. The abstract parent is not a detector and has no gameplay test.
<!-- repowiki:condition-base {"name":"BiotechPollutionBase","abstract":true} -->

| Condition ID | Meaning | Detector | Classification | Prerequisite |
|---|---|---|---|---|
| `MapThreatActive` | active map threat | `MapDanger` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `ToxicFalloutActive` | toxic fallout | `GameCondition` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `SolarFlareActive` | solar flare | `GameCondition` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `AnomalyGrayFleshEvidence` | imitator infection evidence | `ThingPresent` | both page and prompt context | Anomaly content; plain-string/tracker matching no-ops when absent |
| `MetalhorrorEmergence` | metalhorror emergence | `ThingPresent` | prompt-only observation | Anomaly content; plain-string/tracker matching no-ops when absent |
| `MetalhorrorInfection` | hidden imitator infection | `MapHiddenHediff` | prompt-only observation | Anomaly content; plain-string/tracker matching no-ops when absent |
| `ColdSnapActive` | cold snap | `GameCondition` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `HeatWaveActive` | heat wave | `GameCondition` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `VolcanicWinterActive` | volcanic winter | `GameCondition` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `EclipseActive` | eclipse | `GameCondition` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `PsychicDroneActive` | psychic drone | `GameCondition` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `PsychicSootheActive` | psychic soothe | `GameCondition` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `PsychicSuppressionActive` | psychic suppression | `GameCondition` | prompt-only observation | Royalty (`Ludeon.RimWorld.Royalty`) |
| `GrayPallActive` | gray pall | `GameCondition` | prompt-only observation | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| `UnnaturalHeatActive` | unnatural heat | `GameCondition` | prompt-only observation | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| `HateChantDroneActive` | hate chanting | `GameCondition` | prompt-only observation | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| `NoxiousHazeActive` | acidic smog | `GameCondition` | prompt-only observation | Biotech (`Ludeon.RimWorld.Biotech`) |
| `DroughtActive` | drought | `GameCondition` | prompt-only observation | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| `VolcanicDebrisActive` | volcanic debris | `GameCondition` | prompt-only observation | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| `LavaFlowActive` | lava flow | `GameCondition` | prompt-only observation | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| `DarkenedSkiesActive` | darkened skies | `GameCondition` | prompt-only observation | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| `BioluminescentSporesActive` | bioluminescent spores | `GameCondition` | prompt-only observation | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| `GillRotActive` | gill rot | `GameCondition` | prompt-only observation | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| `DeepFreezeActive` | deep freeze | `GameCondition` | prompt-only observation | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| `SeasonalFloodActive` | seasonal flooding | `ThingPresent` | prompt-only observation | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| `BloodRainActive` | blood rain | `GameCondition` | prompt-only observation | Anomaly content; exact string matching no-ops when absent |
| `DeathPallActive` | death pall | `GameCondition` | prompt-only observation | Anomaly content; exact string matching no-ops when absent |
| `UnnaturalDarknessActive` | unnatural darkness | `GameCondition` | prompt-only observation | Anomaly content; exact string matching no-ops when absent |
| `PitGatePresence` | pit gate | `ThingPresent` | both page and prompt context | Anomaly content; plain-string/tracker matching no-ops when absent |
| `FleshmassHeartPresence` | fleshmass heart | `ThingPresent` | both page and prompt context | Anomaly content; plain-string/tracker matching no-ops when absent |
| `ObeliskPresence` | obelisk on the map | `ThingPresent` | prompt-only observation | Anomaly content; plain-string/tracker matching no-ops when absent |
| `HarbingerTreePresence` | harbinger trees | `ThingPresent` | prompt-only observation | Anomaly content; plain-string/tracker matching no-ops when absent |
| `NociospherePresence` | nociosphere | `ThingPresent` | prompt-only observation | Anomaly content; plain-string/tracker matching no-ops when absent |
| `UnnaturalCorpsePresence` | unnatural corpse | `PawnUnnaturalCorpse` | prompt-only observation | Anomaly content; plain-string/tracker matching no-ops when absent |
| `ThrumboVisit` | thrumbo on the map | `ThingPresent` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `AlphabeaversActive` | alphabeavers | `ThingPresent` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `CropBlightActive` | crop blight | `ThingPresent` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `AmbrosiaSprouted` | ambrosia grove | `ThingPresent` | prompt-only observation | Base game or any mod providing the exact matched identifier |
| `BiotechPollutionMeaningful` | meaningful pollution | `MapPollution` | both page and prompt context | Biotech (runtime-gated pollution reader) |
| `BiotechPollutionSevere` | severe pollution | `MapPollution` | both page and prompt context | Biotech (runtime-gated pollution reader) |
| `BiotechPollutionCritical` | critical pollution | `MapPollution` | both page and prompt context | Biotech (runtime-gated pollution reader) |

<!-- repowiki:condition {"defName":"MapThreatActive","label":"active map threat","conditionKey":"MapThreatActive","enabled":true,"scope":"Map","observerType":"MapDanger","pollIntervalTicks":1000,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":[],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `MapThreatActive` — active map threat

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled map danger/hostile-state comparison. |
| Timing | poll 1000 ticks; start debounce 0; end debounce 5000 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `MapThreatActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"ToxicFalloutActive","label":"toxic fallout","conditionKey":"ToxicFalloutActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["ToxicFallout"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `ToxicFalloutActive` — toxic fallout

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled active GameCondition comparison; exact names: `ToxicFallout`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `ToxicFalloutActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"SolarFlareActive","label":"solar flare","conditionKey":"SolarFlareActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["SolarFlare"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `SolarFlareActive` — solar flare

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled active GameCondition comparison; exact names: `SolarFlare`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `SolarFlareActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"AnomalyGrayFleshEvidence","label":"imitator infection evidence","conditionKey":"AnomalyGrayFleshEvidence","enabled":true,"scope":"Map","observerType":"ThingPresent","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":0,"dedupTicks":2500,"recordStartEvent":true,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["GrayFleshSample"],"suppressWhenThingDefNames":["Metalhorror","Filth_MetalhorrorDebris"],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":120000,"restartCooldownTicks":120000,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `AnomalyGrayFleshEvidence` — imitator infection evidence

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly content; plain-string/tracker matching no-ops when absent |
| Detection | Scheduled spawned-thing/filth presence comparison; exact names: `GrayFleshSample`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 0 |
| Classification | both page and prompt context |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=120000; suppressing things `Metalhorror`, `Filth_MetalhorrorDebris` |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | A transition page uses the **imitator infection evidence** meaning; while active, prompt-test output also shows its context cue when selected. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `AnomalyGrayFleshEvidence`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"MetalhorrorEmergence","label":"metalhorror emergence","conditionKey":"MetalhorrorEmergence","enabled":true,"scope":"Map","observerType":"ThingPresent","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["Metalhorror"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `MetalhorrorEmergence` — metalhorror emergence

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly content; plain-string/tracker matching no-ops when absent |
| Detection | Scheduled spawned-thing/filth presence comparison; exact names: `Metalhorror`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 5000 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `MetalhorrorEmergence`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"MetalhorrorInfection","label":"hidden imitator infection","conditionKey":"MetalhorrorInfection","enabled":true,"scope":"Map","observerType":"MapHiddenHediff","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":10000,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["MetalhorrorImplant"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `MetalhorrorInfection` — hidden imitator infection

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly content; plain-string/tracker matching no-ops when absent |
| Detection | Scheduled map-level hidden-health boolean; names are not exposed to prompt evidence: `MetalhorrorImplant`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 10000 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `MetalhorrorInfection`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"ColdSnapActive","label":"cold snap","conditionKey":"ColdSnapActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["ColdSnap"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `ColdSnapActive` — cold snap

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled active GameCondition comparison; exact names: `ColdSnap`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `ColdSnapActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"HeatWaveActive","label":"heat wave","conditionKey":"HeatWaveActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["HeatWave"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `HeatWaveActive` — heat wave

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled active GameCondition comparison; exact names: `HeatWave`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `HeatWaveActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"VolcanicWinterActive","label":"volcanic winter","conditionKey":"VolcanicWinterActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["VolcanicWinter","VolcanicAsh"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `VolcanicWinterActive` — volcanic winter

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled active GameCondition comparison; exact names: `VolcanicWinter`, `VolcanicAsh`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `VolcanicWinterActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"EclipseActive","label":"eclipse","conditionKey":"EclipseActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["Eclipse"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `EclipseActive` — eclipse

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled active GameCondition comparison; exact names: `Eclipse`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `EclipseActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"PsychicDroneActive","label":"psychic drone","conditionKey":"PsychicDroneActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["PsychicDrone"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `PsychicDroneActive` — psychic drone

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled active GameCondition comparison; exact names: `PsychicDrone`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `PsychicDroneActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"PsychicSootheActive","label":"psychic soothe","conditionKey":"PsychicSootheActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["PsychicSoothe"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `PsychicSootheActive` — psychic soothe

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled active GameCondition comparison; exact names: `PsychicSoothe`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `PsychicSootheActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"PsychicSuppressionActive","label":"psychic suppression","conditionKey":"PsychicSuppressionActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["PsychicSuppression"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Royalty"} -->
## Condition: `PsychicSuppressionActive` — psychic suppression

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Royalty (`Ludeon.RimWorld.Royalty`) |
| Detection | Scheduled active GameCondition comparison; exact names: `PsychicSuppression`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `PsychicSuppressionActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"GrayPallActive","label":"gray pall","conditionKey":"GrayPallActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["GrayPall"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Anomaly"} -->
## Condition: `GrayPallActive` — gray pall

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Detection | Scheduled active GameCondition comparison; exact names: `GrayPall`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `GrayPallActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"UnnaturalHeatActive","label":"unnatural heat","conditionKey":"UnnaturalHeatActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["UnnaturalHeat"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Anomaly"} -->
## Condition: `UnnaturalHeatActive` — unnatural heat

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Detection | Scheduled active GameCondition comparison; exact names: `UnnaturalHeat`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `UnnaturalHeatActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"HateChantDroneActive","label":"hate chanting","conditionKey":"HateChantDroneActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["HateChantDrone"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Anomaly"} -->
## Condition: `HateChantDroneActive` — hate chanting

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Detection | Scheduled active GameCondition comparison; exact names: `HateChantDrone`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `HateChantDroneActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"NoxiousHazeActive","label":"acidic smog","conditionKey":"NoxiousHazeActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["NoxiousHaze"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Biotech"} -->
## Condition: `NoxiousHazeActive` — acidic smog

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Biotech (`Ludeon.RimWorld.Biotech`) |
| Detection | Scheduled active GameCondition comparison; exact names: `NoxiousHaze`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `NoxiousHazeActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"DroughtActive","label":"drought","conditionKey":"DroughtActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["Drought"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Odyssey"} -->
## Condition: `DroughtActive` — drought

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| Detection | Scheduled active GameCondition comparison; exact names: `Drought`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `DroughtActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"VolcanicDebrisActive","label":"volcanic debris","conditionKey":"VolcanicDebrisActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["VolcanicDebris"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Odyssey"} -->
## Condition: `VolcanicDebrisActive` — volcanic debris

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| Detection | Scheduled active GameCondition comparison; exact names: `VolcanicDebris`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `VolcanicDebrisActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"LavaFlowActive","label":"lava flow","conditionKey":"LavaFlowActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["LavaFlow"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Odyssey"} -->
## Condition: `LavaFlowActive` — lava flow

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| Detection | Scheduled active GameCondition comparison; exact names: `LavaFlow`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `LavaFlowActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"DarkenedSkiesActive","label":"darkened skies","conditionKey":"DarkenedSkiesActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["DarkenedSkies"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Odyssey"} -->
## Condition: `DarkenedSkiesActive` — darkened skies

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| Detection | Scheduled active GameCondition comparison; exact names: `DarkenedSkies`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `DarkenedSkiesActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"BioluminescentSporesActive","label":"bioluminescent spores","conditionKey":"BioluminescentSporesActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["BioluminescentSpores"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Odyssey"} -->
## Condition: `BioluminescentSporesActive` — bioluminescent spores

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| Detection | Scheduled active GameCondition comparison; exact names: `BioluminescentSpores`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `BioluminescentSporesActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"GillRotActive","label":"gill rot","conditionKey":"GillRotActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["GillRot"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Odyssey"} -->
## Condition: `GillRotActive` — gill rot

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| Detection | Scheduled active GameCondition comparison; exact names: `GillRot`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `GillRotActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"DeepFreezeActive","label":"deep freeze","conditionKey":"DeepFreezeActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["DeepFreeze"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Odyssey"} -->
## Condition: `DeepFreezeActive` — deep freeze

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| Detection | Scheduled active GameCondition comparison; exact names: `DeepFreeze`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `DeepFreezeActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"SeasonalFloodActive","label":"seasonal flooding","conditionKey":"SeasonalFloodActive","enabled":true,"scope":"Map","observerType":"ThingPresent","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["SeasonalFlood"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":15000,"maxPagePawns":0,"mayRequire":"Ludeon.RimWorld.Odyssey"} -->
## Condition: `SeasonalFloodActive` — seasonal flooding

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| Detection | Scheduled spawned-thing/filth presence comparison; exact names: `SeasonalFlood`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 5000 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=15000; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `SeasonalFloodActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"BloodRainActive","label":"blood rain","conditionKey":"BloodRainActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["BloodRain"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `BloodRainActive` — blood rain

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly content; exact string matching no-ops when absent |
| Detection | Scheduled active GameCondition comparison; exact names: `BloodRain`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `BloodRainActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"DeathPallActive","label":"death pall","conditionKey":"DeathPallActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["DeathPall"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `DeathPallActive` — death pall

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly content; exact string matching no-ops when absent |
| Detection | Scheduled active GameCondition comparison; exact names: `DeathPall`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `DeathPallActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"UnnaturalDarknessActive","label":"unnatural darkness","conditionKey":"UnnaturalDarknessActive","enabled":true,"scope":"Map","observerType":"GameCondition","pollIntervalTicks":1500,"startDebounceTicks":0,"endDebounceTicks":2500,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["UnnaturalDarkness"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `UnnaturalDarknessActive` — unnatural darkness

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly content; exact string matching no-ops when absent |
| Detection | Scheduled active GameCondition comparison; exact names: `UnnaturalDarkness`. |
| Timing | poll 1500 ticks; start debounce 0; end debounce 2500 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `UnnaturalDarknessActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"PitGatePresence","label":"pit gate","conditionKey":"PitGatePresence","enabled":true,"scope":"Map","observerType":"ThingPresent","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":true,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["PitGate"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":180000,"restartCooldownTicks":120000,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `PitGatePresence` — pit gate

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly content; plain-string/tracker matching no-ops when absent |
| Detection | Scheduled spawned-thing/filth presence comparison; exact names: `PitGate`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 5000 |
| Classification | both page and prompt context |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=120000; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | A transition page uses the **pit gate** meaning; while active, prompt-test output also shows its context cue when selected. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `PitGatePresence`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"FleshmassHeartPresence","label":"fleshmass heart","conditionKey":"FleshmassHeartPresence","enabled":true,"scope":"Map","observerType":"ThingPresent","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":true,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["FleshmassHeart"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":180000,"restartCooldownTicks":120000,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `FleshmassHeartPresence` — fleshmass heart

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly content; plain-string/tracker matching no-ops when absent |
| Detection | Scheduled spawned-thing/filth presence comparison; exact names: `FleshmassHeart`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 5000 |
| Classification | both page and prompt context |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=120000; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | A transition page uses the **fleshmass heart** meaning; while active, prompt-test output also shows its context cue when selected. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `FleshmassHeartPresence`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"ObeliskPresence","label":"obelisk on the map","conditionKey":"ObeliskPresence","enabled":true,"scope":"Map","observerType":"ThingPresent","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["WarpedObelisk_Abductor","WarpedObelisk_Duplicator","WarpedObelisk_Mutator"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `ObeliskPresence` — obelisk on the map

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly content; plain-string/tracker matching no-ops when absent |
| Detection | Scheduled spawned-thing/filth presence comparison; exact names: `WarpedObelisk_Abductor`, `WarpedObelisk_Duplicator`, `WarpedObelisk_Mutator`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 5000 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `ObeliskPresence`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"HarbingerTreePresence","label":"harbinger trees","conditionKey":"HarbingerTreePresence","enabled":true,"scope":"Map","observerType":"ThingPresent","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["Plant_TreeHarbinger"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":120000,"restartCooldownTicks":600000,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `HarbingerTreePresence` — harbinger trees

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly content; plain-string/tracker matching no-ops when absent |
| Detection | Scheduled spawned-thing/filth presence comparison; exact names: `Plant_TreeHarbinger`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 5000 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=600000; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `HarbingerTreePresence`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"NociospherePresence","label":"nociosphere","conditionKey":"NociospherePresence","enabled":true,"scope":"Map","observerType":"ThingPresent","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["Nociosphere"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `NociospherePresence` — nociosphere

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly content; plain-string/tracker matching no-ops when absent |
| Detection | Scheduled spawned-thing/filth presence comparison; exact names: `Nociosphere`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 5000 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `NociospherePresence`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"UnnaturalCorpsePresence","label":"unnatural corpse","conditionKey":"UnnaturalCorpsePresence","enabled":true,"scope":"Pawn","observerType":"PawnUnnaturalCorpse","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":[],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":0,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `UnnaturalCorpsePresence` — unnatural corpse

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly content; plain-string/tracker matching no-ops when absent |
| Detection | Scheduled Anomaly tracker query for a pawn haunted by an unnatural corpse. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 5000 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=0; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `UnnaturalCorpsePresence`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"ThrumboVisit","label":"thrumbo on the map","conditionKey":"ThrumboVisit","enabled":true,"scope":"Map","observerType":"ThingPresent","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["Thrumbo"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":120000,"restartCooldownTicks":300000,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `ThrumboVisit` — thrumbo on the map

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled spawned-thing/filth presence comparison; exact names: `Thrumbo`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 5000 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=300000; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `ThrumboVisit`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"AlphabeaversActive","label":"alphabeavers","conditionKey":"AlphabeaversActive","enabled":true,"scope":"Map","observerType":"ThingPresent","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["Alphabeaver"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":120000,"restartCooldownTicks":120000,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `AlphabeaversActive` — alphabeavers

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled spawned-thing/filth presence comparison; exact names: `Alphabeaver`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 5000 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=120000; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `AlphabeaversActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"CropBlightActive","label":"crop blight","conditionKey":"CropBlightActive","enabled":true,"scope":"Map","observerType":"ThingPresent","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["Blight"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":180000,"restartCooldownTicks":60000,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `CropBlightActive` — crop blight

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled spawned-thing/filth presence comparison; exact names: `Blight`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 5000 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=60000; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `CropBlightActive`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"AmbrosiaSprouted","label":"ambrosia grove","conditionKey":"AmbrosiaSprouted","enabled":true,"scope":"Map","observerType":"ThingPresent","pollIntervalTicks":2500,"startDebounceTicks":0,"endDebounceTicks":5000,"dedupTicks":2500,"recordStartEvent":false,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":["Plant_Ambrosia"],"suppressWhenThingDefNames":[],"minPollutionFraction":0,"maxPollutionFraction":-1,"maxActiveTicks":120000,"restartCooldownTicks":600000,"maxPagePawns":0,"mayRequire":""} -->
## Condition: `AmbrosiaSprouted` — ambrosia grove

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game or any mod providing the exact matched identifier |
| Detection | Scheduled spawned-thing/filth presence comparison; exact names: `Plant_Ambrosia`. |
| Timing | poll 2500 ticks; start debounce 0; end debounce 5000 |
| Classification | prompt-only observation |
| Admission/suppression | enabled=yes; dedup=2500 ticks; restart cooldown=600000; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | No page is expected. While active, a prompt-test capture can show the selected observed-condition cue; after the end debounce it disappears. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `AmbrosiaSprouted`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"BiotechPollutionMeaningful","label":"meaningful pollution","conditionKey":"BiotechPollutionMeaningful","enabled":true,"scope":"Map","observerType":"MapPollution","pollIntervalTicks":2500,"startDebounceTicks":15000,"endDebounceTicks":15000,"dedupTicks":60000,"recordStartEvent":true,"recordEndEvent":true,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":[],"suppressWhenThingDefNames":[],"minPollutionFraction":0.1,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":30000,"maxPagePawns":2,"mayRequire":""} -->
## Condition: `BiotechPollutionMeaningful` — meaningful pollution

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Biotech (runtime-gated pollution reader) |
| Detection | Scheduled Biotech world-tile pollution threshold comparison (minimum 0.1). |
| Timing | poll 2500 ticks; start debounce 15000; end debounce 15000 |
| Classification | both page and prompt context |
| Admission/suppression | enabled=yes; dedup=60000 ticks; restart cooldown=30000; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | A transition page uses the **meaningful pollution** meaning; while active, prompt-test output also shows its context cue when selected. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `BiotechPollutionMeaningful`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"BiotechPollutionSevere","label":"severe pollution","conditionKey":"BiotechPollutionSevere","enabled":true,"scope":"Map","observerType":"MapPollution","pollIntervalTicks":2500,"startDebounceTicks":15000,"endDebounceTicks":15000,"dedupTicks":60000,"recordStartEvent":true,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":[],"suppressWhenThingDefNames":[],"minPollutionFraction":0.35,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":30000,"maxPagePawns":2,"mayRequire":""} -->
## Condition: `BiotechPollutionSevere` — severe pollution

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Biotech (runtime-gated pollution reader) |
| Detection | Scheduled Biotech world-tile pollution threshold comparison (minimum 0.35). |
| Timing | poll 2500 ticks; start debounce 15000; end debounce 15000 |
| Classification | both page and prompt context |
| Admission/suppression | enabled=yes; dedup=60000 ticks; restart cooldown=30000; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | A transition page uses the **severe pollution** meaning; while active, prompt-test output also shows its context cue when selected. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `BiotechPollutionSevere`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

<!-- repowiki:condition {"defName":"BiotechPollutionCritical","label":"critical pollution","conditionKey":"BiotechPollutionCritical","enabled":true,"scope":"Map","observerType":"MapPollution","pollIntervalTicks":2500,"startDebounceTicks":15000,"endDebounceTicks":15000,"dedupTicks":60000,"recordStartEvent":true,"recordEndEvent":false,"recordScope":"MapColonists","promptEnabled":true,"matchDefNames":[],"suppressWhenThingDefNames":[],"minPollutionFraction":0.65,"maxPollutionFraction":-1,"maxActiveTicks":0,"restartCooldownTicks":30000,"maxPagePawns":2,"mayRequire":""} -->
## Condition: `BiotechPollutionCritical` — critical pollution

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Biotech (runtime-gated pollution reader) |
| Detection | Scheduled Biotech world-tile pollution threshold comparison (minimum 0.65). |
| Timing | poll 2500 ticks; start debounce 15000; end debounce 15000 |
| Classification | both page and prompt context |
| Admission/suppression | enabled=yes; dedup=60000 ticks; restart cooldown=30000; suppressing things — |
| Setup cue | Make the exact live state described by the detector persist through its start debounce. Dev Mode condition/health/spawn tools are suitable where RimWorld exposes them; otherwise the setup is scenario-dependent. |
| Expected evidence | A transition page uses the **critical pollution** meaning; while active, prompt-test output also shows its context cue when selected. |
| Source | [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml) — `BiotechPollutionCritical`; [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) — `ScanObservedConditions` |

## Core event-window index

| Window ID | Meaning | Start signal | End signal | Classification | Prerequisite |
|---|---|---|---|---|---|
| `VoidMonolithDiscovery` | void monolith discovery | `ProximityLetter/received`; exact `VoidMonolith`; tokens — | — | one-shot or bounded page phase; no prompt context | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| `VoidMonolithActivation` | void monolith stirring | `VoidMonolith/activated`; exact `Stirring`; tokens — | — | one-shot or bounded page phase; no prompt context | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| `VoidMonolithWaking` | void monolith waking | `VoidMonolith/activated`; exact `Waking`; tokens — | — | one-shot or bounded page phase; no prompt context | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| `VoidMonolithVoidAwakened` | void awakened | `VoidMonolith/activated`; exact `VoidAwakened`; tokens — | — | one-shot or bounded page phase; no prompt context | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| `Birthday` | birthday | `PawnAge/birthday`; exact `Birthday`; tokens — | — | one-shot or bounded page phase; no prompt context | Base game |
| `HeartAttack` | heart attack | `Hediff/added`; exact `HeartAttack`; tokens — | — | one-shot or bounded page phase; no prompt context | Base game |
| `PrisonBreak` | prison break | `PrisonBreak/started`; exact `PrisonBreak`; tokens — | — | one-shot or bounded page phase; no prompt context | Base game |
| `AncientDanger` | ancient danger | `Letter/received`; exact `AncientShrineWarning`; tokens — | — | one-shot or bounded page phase; no prompt context | Base game |
| `RoyalAscent` | Royal Ascent | `Quest/accepted`; exact `EndGame_RoyalAscent`; tokens — | `Quest/completed`; exact `EndGame_RoyalAscent`; tokens —<br>`Quest/failed`; exact `EndGame_RoyalAscent`; tokens — | bounded episode with page phase(s) and prompt context | Royalty (`Ludeon.RimWorld.Royalty`) |
| `MechClusterLanded` | mech cluster | `Incident/executed`; exact `MechCluster`; tokens — | — | bounded episode with page phase(s) and prompt context | Royalty (`Ludeon.RimWorld.Royalty`) |
| `ShortCircuitAftermath` | short circuit | `Incident/executed`; exact `ShortCircuit`; tokens — | — | bounded prompt context; no page phase | Base game |
| `SelfTameJoined` | animal self-tamed | `Incident/executed`; exact `SelfTame`; tokens — | — | bounded prompt context; no page phase | Base game |

<!-- repowiki:window {"defName":"VoidMonolithDiscovery","label":"void monolith discovery","windowKey":"VoidMonolithDiscovery","enabled":true,"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"timeoutTicks":-1,"dedupTicks":2500,"restartOnStart":false,"keepActive":false,"recordScope":"SubjectPawn","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"ProximityLetter","signal":"received","matchDefNames":["VoidMonolith"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[]} -->
## Window: `VoidMonolithDiscovery` — void monolith discovery

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Start boundary | `ProximityLetter/received`; exact `VoidMonolith`; tokens — |
| End boundary | — |
| Lifetime | keepActive=no; timeout=-1; restartOnStart=no; still-present things —; factions — |
| Page phases | start=yes; end=no; timeout=no; scope=`SubjectPawn` |
| Classification | one-shot or bounded page phase; no prompt context |
| Setup cue | Emit the exact start signal with the required def name/token. If the window persists, emit its end signal or let the timeout/still-present probe close it. |
| Expected evidence | Configured page phases use the **void monolith discovery** meaning. A persistent prompt-enabled window can also appear in prompt-test context until it closes. Duplicate starts inside 2500 ticks are suppressed. |
| Source | [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml) — `VoidMonolithDiscovery`; [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs) — `ProcessEventWindowSignal` |

<!-- repowiki:window {"defName":"VoidMonolithActivation","label":"void monolith stirring","windowKey":"VoidMonolithActivation","enabled":true,"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"timeoutTicks":-1,"dedupTicks":2500,"restartOnStart":false,"keepActive":false,"recordScope":"SubjectPawn","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"VoidMonolith","signal":"activated","matchDefNames":["Stirring"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[]} -->
## Window: `VoidMonolithActivation` — void monolith stirring

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Start boundary | `VoidMonolith/activated`; exact `Stirring`; tokens — |
| End boundary | — |
| Lifetime | keepActive=no; timeout=-1; restartOnStart=no; still-present things —; factions — |
| Page phases | start=yes; end=no; timeout=no; scope=`SubjectPawn` |
| Classification | one-shot or bounded page phase; no prompt context |
| Setup cue | Emit the exact start signal with the required def name/token. If the window persists, emit its end signal or let the timeout/still-present probe close it. |
| Expected evidence | Configured page phases use the **void monolith stirring** meaning. A persistent prompt-enabled window can also appear in prompt-test context until it closes. Duplicate starts inside 2500 ticks are suppressed. |
| Source | [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml) — `VoidMonolithActivation`; [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs) — `ProcessEventWindowSignal` |

<!-- repowiki:window {"defName":"VoidMonolithWaking","label":"void monolith waking","windowKey":"VoidMonolithWaking","enabled":true,"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"timeoutTicks":-1,"dedupTicks":2500,"restartOnStart":false,"keepActive":false,"recordScope":"SubjectPawn","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"VoidMonolith","signal":"activated","matchDefNames":["Waking"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[]} -->
## Window: `VoidMonolithWaking` — void monolith waking

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Start boundary | `VoidMonolith/activated`; exact `Waking`; tokens — |
| End boundary | — |
| Lifetime | keepActive=no; timeout=-1; restartOnStart=no; still-present things —; factions — |
| Page phases | start=yes; end=no; timeout=no; scope=`SubjectPawn` |
| Classification | one-shot or bounded page phase; no prompt context |
| Setup cue | Emit the exact start signal with the required def name/token. If the window persists, emit its end signal or let the timeout/still-present probe close it. |
| Expected evidence | Configured page phases use the **void monolith waking** meaning. A persistent prompt-enabled window can also appear in prompt-test context until it closes. Duplicate starts inside 2500 ticks are suppressed. |
| Source | [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml) — `VoidMonolithWaking`; [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs) — `ProcessEventWindowSignal` |

<!-- repowiki:window {"defName":"VoidMonolithVoidAwakened","label":"void awakened","windowKey":"VoidMonolithVoidAwakened","enabled":true,"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"timeoutTicks":-1,"dedupTicks":2500,"restartOnStart":false,"keepActive":false,"recordScope":"SubjectPawn","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"VoidMonolith","signal":"activated","matchDefNames":["VoidAwakened"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[]} -->
## Window: `VoidMonolithVoidAwakened` — void awakened

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Start boundary | `VoidMonolith/activated`; exact `VoidAwakened`; tokens — |
| End boundary | — |
| Lifetime | keepActive=no; timeout=-1; restartOnStart=no; still-present things —; factions — |
| Page phases | start=yes; end=no; timeout=no; scope=`SubjectPawn` |
| Classification | one-shot or bounded page phase; no prompt context |
| Setup cue | Emit the exact start signal with the required def name/token. If the window persists, emit its end signal or let the timeout/still-present probe close it. |
| Expected evidence | Configured page phases use the **void awakened** meaning. A persistent prompt-enabled window can also appear in prompt-test context until it closes. Duplicate starts inside 2500 ticks are suppressed. |
| Source | [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml) — `VoidMonolithVoidAwakened`; [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs) — `ProcessEventWindowSignal` |

<!-- repowiki:window {"defName":"Birthday","label":"birthday","windowKey":"Birthday","enabled":true,"enableWhenPackageIdsLoaded":[],"timeoutTicks":-1,"dedupTicks":2500,"restartOnStart":false,"keepActive":false,"recordScope":"SubjectPawn","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"PawnAge","signal":"birthday","matchDefNames":["Birthday"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[]} -->
## Window: `Birthday` — birthday

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game |
| Start boundary | `PawnAge/birthday`; exact `Birthday`; tokens — |
| End boundary | — |
| Lifetime | keepActive=no; timeout=-1; restartOnStart=no; still-present things —; factions — |
| Page phases | start=yes; end=no; timeout=no; scope=`SubjectPawn` |
| Classification | one-shot or bounded page phase; no prompt context |
| Setup cue | Emit the exact start signal with the required def name/token. If the window persists, emit its end signal or let the timeout/still-present probe close it. |
| Expected evidence | Configured page phases use the **birthday** meaning. A persistent prompt-enabled window can also appear in prompt-test context until it closes. Duplicate starts inside 2500 ticks are suppressed. |
| Source | [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml) — `Birthday`; [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs) — `ProcessEventWindowSignal` |

<!-- repowiki:window {"defName":"HeartAttack","label":"heart attack","windowKey":"HeartAttack","enabled":true,"enableWhenPackageIdsLoaded":[],"timeoutTicks":-1,"dedupTicks":2500,"restartOnStart":false,"keepActive":false,"recordScope":"SubjectPawn","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"Hediff","signal":"added","matchDefNames":["HeartAttack"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[]} -->
## Window: `HeartAttack` — heart attack

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game |
| Start boundary | `Hediff/added`; exact `HeartAttack`; tokens — |
| End boundary | — |
| Lifetime | keepActive=no; timeout=-1; restartOnStart=no; still-present things —; factions — |
| Page phases | start=yes; end=no; timeout=no; scope=`SubjectPawn` |
| Classification | one-shot or bounded page phase; no prompt context |
| Setup cue | Emit the exact start signal with the required def name/token. If the window persists, emit its end signal or let the timeout/still-present probe close it. |
| Expected evidence | Configured page phases use the **heart attack** meaning. A persistent prompt-enabled window can also appear in prompt-test context until it closes. Duplicate starts inside 2500 ticks are suppressed. |
| Source | [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml) — `HeartAttack`; [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs) — `ProcessEventWindowSignal` |

<!-- repowiki:window {"defName":"PrisonBreak","label":"prison break","windowKey":"PrisonBreak","enabled":true,"enableWhenPackageIdsLoaded":[],"timeoutTicks":-1,"dedupTicks":2500,"restartOnStart":false,"keepActive":false,"recordScope":"Map","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"PrisonBreak","signal":"started","matchDefNames":["PrisonBreak"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[]} -->
## Window: `PrisonBreak` — prison break

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game |
| Start boundary | `PrisonBreak/started`; exact `PrisonBreak`; tokens — |
| End boundary | — |
| Lifetime | keepActive=no; timeout=-1; restartOnStart=no; still-present things —; factions — |
| Page phases | start=yes; end=no; timeout=no; scope=`Map` |
| Classification | one-shot or bounded page phase; no prompt context |
| Setup cue | Emit the exact start signal with the required def name/token. If the window persists, emit its end signal or let the timeout/still-present probe close it. |
| Expected evidence | Configured page phases use the **prison break** meaning. A persistent prompt-enabled window can also appear in prompt-test context until it closes. Duplicate starts inside 2500 ticks are suppressed. |
| Source | [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml) — `PrisonBreak`; [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs) — `ProcessEventWindowSignal` |

<!-- repowiki:window {"defName":"AncientDanger","label":"ancient danger","windowKey":"AncientDanger","enabled":true,"enableWhenPackageIdsLoaded":[],"timeoutTicks":-1,"dedupTicks":2500,"restartOnStart":false,"keepActive":false,"recordScope":"SubjectPawn","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":false,"startSignals":[{"source":"Letter","signal":"received","matchDefNames":["AncientShrineWarning"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[]} -->
## Window: `AncientDanger` — ancient danger

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game |
| Start boundary | `Letter/received`; exact `AncientShrineWarning`; tokens — |
| End boundary | — |
| Lifetime | keepActive=no; timeout=-1; restartOnStart=no; still-present things —; factions — |
| Page phases | start=yes; end=no; timeout=no; scope=`SubjectPawn` |
| Classification | one-shot or bounded page phase; no prompt context |
| Setup cue | Emit the exact start signal with the required def name/token. If the window persists, emit its end signal or let the timeout/still-present probe close it. |
| Expected evidence | Configured page phases use the **ancient danger** meaning. A persistent prompt-enabled window can also appear in prompt-test context until it closes. Duplicate starts inside 2500 ticks are suppressed. |
| Source | [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml) — `AncientDanger`; [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs) — `ProcessEventWindowSignal` |

<!-- repowiki:window {"defName":"RoyalAscent","label":"Royal Ascent","windowKey":"RoyalAscent","enabled":true,"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"timeoutTicks":1200000,"dedupTicks":2500,"restartOnStart":false,"keepActive":true,"recordScope":"MapWitness","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":true,"startSignals":[{"source":"Quest","signal":"accepted","matchDefNames":["EndGame_RoyalAscent"],"matchTokens":[]}],"endSignals":[{"source":"Quest","signal":"completed","matchDefNames":["EndGame_RoyalAscent"],"matchTokens":[]},{"source":"Quest","signal":"failed","matchDefNames":["EndGame_RoyalAscent"],"matchTokens":[]}],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[]} -->
## Window: `RoyalAscent` — Royal Ascent

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Royalty (`Ludeon.RimWorld.Royalty`) |
| Start boundary | `Quest/accepted`; exact `EndGame_RoyalAscent`; tokens — |
| End boundary | `Quest/completed`; exact `EndGame_RoyalAscent`; tokens —<br>`Quest/failed`; exact `EndGame_RoyalAscent`; tokens — |
| Lifetime | keepActive=yes; timeout=1200000; restartOnStart=no; still-present things —; factions — |
| Page phases | start=yes; end=no; timeout=no; scope=`MapWitness` |
| Classification | bounded episode with page phase(s) and prompt context |
| Setup cue | Emit the exact start signal with the required def name/token. If the window persists, emit its end signal or let the timeout/still-present probe close it. |
| Expected evidence | Configured page phases use the **Royal Ascent** meaning. A persistent prompt-enabled window can also appear in prompt-test context until it closes. Duplicate starts inside 2500 ticks are suppressed. |
| Source | [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml) — `RoyalAscent`; [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs) — `ProcessEventWindowSignal` |

<!-- repowiki:window {"defName":"MechClusterLanded","label":"mech cluster","windowKey":"MechClusterLanded","enabled":true,"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"timeoutTicks":180000,"dedupTicks":60000,"restartOnStart":false,"keepActive":true,"recordScope":"Map","recordStartEvent":true,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":true,"startSignals":[{"source":"Incident","signal":"executed","matchDefNames":["MechCluster"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":["Mechanoid"]} -->
## Window: `MechClusterLanded` — mech cluster

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Royalty (`Ludeon.RimWorld.Royalty`) |
| Start boundary | `Incident/executed`; exact `MechCluster`; tokens — |
| End boundary | — |
| Lifetime | keepActive=yes; timeout=180000; restartOnStart=no; still-present things —; factions `Mechanoid` |
| Page phases | start=yes; end=no; timeout=no; scope=`Map` |
| Classification | bounded episode with page phase(s) and prompt context |
| Setup cue | Emit the exact start signal with the required def name/token. If the window persists, emit its end signal or let the timeout/still-present probe close it. |
| Expected evidence | Configured page phases use the **mech cluster** meaning. A persistent prompt-enabled window can also appear in prompt-test context until it closes. Duplicate starts inside 60000 ticks are suppressed. |
| Source | [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml) — `MechClusterLanded`; [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs) — `ProcessEventWindowSignal` |

<!-- repowiki:window {"defName":"ShortCircuitAftermath","label":"short circuit","windowKey":"ShortCircuitAftermath","enabled":true,"enableWhenPackageIdsLoaded":[],"timeoutTicks":15000,"dedupTicks":30000,"restartOnStart":false,"keepActive":true,"recordScope":"Map","recordStartEvent":false,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":true,"startSignals":[{"source":"Incident","signal":"executed","matchDefNames":["ShortCircuit"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[]} -->
## Window: `ShortCircuitAftermath` — short circuit

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game |
| Start boundary | `Incident/executed`; exact `ShortCircuit`; tokens — |
| End boundary | — |
| Lifetime | keepActive=yes; timeout=15000; restartOnStart=no; still-present things —; factions — |
| Page phases | start=no; end=no; timeout=no; scope=`Map` |
| Classification | bounded prompt context; no page phase |
| Setup cue | Emit the exact start signal with the required def name/token. If the window persists, emit its end signal or let the timeout/still-present probe close it. |
| Expected evidence | Configured page phases use the **short circuit** meaning. A persistent prompt-enabled window can also appear in prompt-test context until it closes. Duplicate starts inside 30000 ticks are suppressed. |
| Source | [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml) — `ShortCircuitAftermath`; [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs) — `ProcessEventWindowSignal` |

<!-- repowiki:window {"defName":"SelfTameJoined","label":"animal self-tamed","windowKey":"SelfTameJoined","enabled":true,"enableWhenPackageIdsLoaded":[],"timeoutTicks":30000,"dedupTicks":30000,"restartOnStart":false,"keepActive":true,"recordScope":"Map","recordStartEvent":false,"recordEndEvent":false,"recordEndWithoutActive":false,"recordTimeoutEvent":false,"promptEnabled":true,"startSignals":[{"source":"Incident","signal":"executed","matchDefNames":["SelfTame"],"matchTokens":[]}],"endSignals":[],"stillPresentThingDefNames":[],"stillPresentFactionDefNames":[]} -->
## Window: `SelfTameJoined` — animal self-tamed

| Test field | Source-verified expectation |
|---|---|
| Prerequisite | Base game |
| Start boundary | `Incident/executed`; exact `SelfTame`; tokens — |
| End boundary | — |
| Lifetime | keepActive=yes; timeout=30000; restartOnStart=no; still-present things —; factions — |
| Page phases | start=no; end=no; timeout=no; scope=`Map` |
| Classification | bounded prompt context; no page phase |
| Setup cue | Emit the exact start signal with the required def name/token. If the window persists, emit its end signal or let the timeout/still-present probe close it. |
| Expected evidence | Configured page phases use the **animal self-tamed** meaning. A persistent prompt-enabled window can also appear in prompt-test context until it closes. Duplicate starts inside 30000 ticks are suppressed. |
| Source | [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml) — `SelfTameJoined`; [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs) — `ProcessEventWindowSignal` |

## Compatibility observations and windows

Package-specific conditions and windows are listed in [Compatibility](Compatibility.md), beside the package and adapter that makes them reachable. This page intentionally keeps the core inventories separate.

## Source of truth

- Core conditions: [1.6/Defs/DiaryObservedConditionDefs.xml](../../../1.6/Defs/DiaryObservedConditionDefs.xml).
- Core windows: [1.6/Defs/DiaryEventWindowDefs.xml](../../../1.6/Defs/DiaryEventWindowDefs.xml).
- Lifecycle implementation: [Source/Core/DiaryGameComponent.ObservedConditions.cs](../../../Source/Core/DiaryGameComponent.ObservedConditions.cs) and [Source/Core/DiaryGameComponent.EventWindows.cs](../../../Source/Core/DiaryGameComponent.EventWindows.cs).
