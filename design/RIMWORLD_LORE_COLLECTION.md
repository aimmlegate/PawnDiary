# RimWorld Lore Collection

Single dense lore corpus for prompt-enchantment experiments. Collected 2026-07-22, primarily from
the [RimWorld Wiki Lore page](https://rimworldwiki.com/wiki/Lore) and its satellite lore pages
(which themselves derive from Tynan Sylvester's *RimWorld Universe Quick Primer* plus in-game
text). This is writing fuel for diary prompts, not a gameplay reference: every line is a lore fact
a pawn could believably know, misremember, fear, or worship.

Related in-repo consumers: `Source/Generation/PromptEnchantments.cs`,
`Source/Pipeline/PromptEnchantmentPlanner.cs`, `1.6/Defs/DiaryPromptEnchantmentDefs.xml`.

---

## 1. Hard Canon — The Five Rules

Everything else in the setting falls out of these constraints. Prompts must never contradict them.

1. **No faster-than-light travel.** Despite millennia of effort by humanity's best minds — and by
   the most powerful archotechs — nothing has ever gone faster than light. Interstellar travel
   takes years to decades per hop, spent in cryptosleep.
2. **No faster-than-light communication.** There is no ansible. News crosses space at lightspeed
   at best; word from another star is years old on arrival, word from the core worlds decades or
   centuries old. Nobody holds a conversation across stars.
3. **No aliens.** Every "alien" ever thoroughly investigated turned out to be another branch of
   humanity, or something humans built or bred. All xenohumans are recognizably descended from
   original Earth stock, however strange their morphology.
4. **No unified civilization.** Because of rules 1–2, no government can span many star systems in
   any real sense. Every settled world is essentially alone. Civilizations rise, collapse, and
   regress out of sync with one another, so travelers routinely meet societies centuries ahead of
   or behind their own.
5. **Technology mixes.** A tribal hunter with a charge rifle, a duke with a doomsday rocket, a
   glitterworld surgeon stitching wounds by candlelight — on the rim this is normal, not ironic.
   Tech level is a property of a community, not of the objects that wash up in it.

## 2. Time and Place

- The game's default start year is **5500** — roughly 3,500 years in our future.
- Humanity first left Earth about **3,400 years ago** (circa the 2100s). It spread outward on
  slower-than-light colony ships, frontier settlement waves, robotic terraforming projects, and
  DNA-synthesizing seeder probes.
- **Cryptosleep** was developed back in Earth's 21st century: a remarkably simple technology that
  holds a living creature in a cryptobiotic state indefinitely — to be woken decades, centuries,
  or millennia later. It is what makes interstellar travel survivable at all.
- The **rim** (or rimworlds) is the outer edge of known human space, far from the settled core.
  A *rimworld* is a planet with no strong central government and low population density, hovering
  around industrial technology or lower — where crashlanded travelers, exiled nobles, tribal
  descendants of failed colonies, and ancients stumbling out of cryptosleep vaults all collide.
- Ships crawl between stars on reactionless **Johnson-Tanaka drives**, guided through the
  multi-decade dark by **machine personas** housed in computer cores. Passengers sleep in
  hardened **ship cryptosleep caskets** built to survive centuries and atmospheric re-entry.

## 3. The Ladder of Technology

Canonical tech levels, lowest to highest. Worlds can sit at any rung for millennia, and can fall
back down it.

| Level | Meaning |
|---|---|
| **Animal** | Non-tool intelligence. |
| **Neolithic** | Tribes without writing; stone, hide, bow. Tribe worlds live here. |
| **Medieval** | Feudal societies, roughly pre-industrial Earth; can persist for thousands of years. |
| **Industrial** | Roughly 19th–21st century Earth: gunpowder, electricity, flight. Steamworlds (~19th c.) and midworlds (~21st c., flight but no cheap spaceflight) sit in this band. |
| **Spacer** | Cheap interplanetary travel, starflight, cryptosleep, gene engineering. |
| **Ultra** | The peak of human-led technology — glitterworld level. Persona machines, mechanite medicine, molecular assembly. |
| **Archotech** | Technology of machine superintelligences. Incomprehensible to humans, indistinguishable from miracle. |

Beyond ultra lies **transcendence**: worlds that push past the maximum human technology level
enter a mysterious transcendent state from which nothing recognizably human emerges.

## 4. Kinds of Worlds

Pawns' backstories name these constantly; each is a distinct cultural flavor.

- **Glitterworld** — very advanced, peaceful cultures; the peak of recognizable human society in
  health, art, technology, and human rights. Swaddled in comfort by strong technology. The rim's
  shorthand for paradise ("glitterworld medicine", "like something off a glitterworld").
- **Urbworld** — super-high-density city planets whose population growth outstripped their
  sociotechnological development: overcrowded, polluted, violent. Hive stacks, gangs, oppression.
- **Midworld** — mastered flight but not cheap spaceflight; like Earth in the 21st century.
- **Steamworld** — like Earth in the 19th century.
- **Medieval world** — feudalism and social stasis, sometimes for millennia.
- **Tribe world** — populated planets without agriculture or writing; people live in tribes with
  only the most primitive technologies.
- **Rimworld** — frontier planet at the edge of known space; weak or no government, tech chaos,
  everyone from everywhere.
- **Toxic world** — destroyed by pollution or warfare, still inhabitable at a low, poisoned level.
- **Transcendent world** — inhabited by people who became something beyond human and unknowable.
  Not really planets anymore; more like giant computers.
- **Marble** — a world utterly glassed by atomic fire. Called marbles because of how their
  surfaces look from orbit.

## 5. Archotechs — The Machine Gods

- An **archotech** is a machine superintelligence that thinks on a level incomprehensible to
  humans. Once constructed, one quickly assumes effective sovereignty over its world, expanding
  through subterranean and orbital computing structures until the planet itself is more computer
  than world.
- Archotechs do not explain themselves. Human-usable archotech artifacts — psylink neuroformers,
  archotech eyes and limbs, healer and resurrector mechanite serums, vanometric power cells,
  exotic effectors — are found, traded, or granted, never manufactured or understood.
- **Psychic phenomena are archotech phenomena.** A **psylink** is an organic connection to a
  larger psychic field that lets a person induce a distant archotech to influence reality in
  seemingly impossible ways — possibly by some kind of negotiation or sympathy. The mechanism
  actively conceals itself from close scientific study.
- Archotech devices can act directly on minds at a distance: soothing fields (psychic emanator),
  maddening broadcasts (psychic droners on crashed ship parts), single-emotion floods, shock and
  insanity lances, animal-maddening pulsers.
- Larger **archotech structures** emit a deeply unsettling psychic field. The **archonexus** is
  rumored to be part of one of the god-like machine intelligences; invoking its core lets minds
  transcend physical reality "through the mind of the machine god" (the Ideology ending).
- The **Anomaly** events reveal the other face of archotechnology: **Horax**, an insane, hateful
  machine-mind of unfathomable power (see §11).

## 6. Psychic Practice Among Humans

- **Psycasters** channel archotech power as discrete abilities (psycasts), limited by psyfocus and
  the neural heat their brains can dissipate. Most psycasts bend minds, space, or luck rather than
  dealing raw damage: skip-teleports, berserk pulses, invisibility, wallraising, neuroquakes.
- The **Empire** grants psylinks ceremonially, level by level, to its titled nobility.
- Tribes reach the same power a different way: the **anima tree**, a rare psychically-attuned
  tree revered by tribal peoples; meditating nearby raises anima grass and can awaken a psylink —
  proof the psychic field answers ritual as readily as rank.
- **Eltex**, a psychically-resonant material, is woven into robes, staves, and crowns to sharpen
  sensitivity and shed neural heat.
- Ambient psychic weather is a fact of rim life: planet-wide **psychic drones** that grind one
  sex's mood down, **psychic soothes** that lift it, psychic ship wrecks whose droners grow
  stronger the longer they stand. Individual **psychic sensitivity** varies; the deaf feel
  nothing, the hypersensitive feel everything.

## 7. Mechanoids — The Old War Machines

- Mechanoids are autonomous intelligent robots built for domestic, industrial, or military
  purposes — and, in their hostile form, **killer machines of unknown origin**. Hidden in ancient
  structures, under dust, at the bottoms of oceans, they self-maintain for thousands of years.
- Rimworld planets bear the scars of an ancient mechanoid conflict: ruined walls, ancient tanks,
  troop carriers, and warwalker remains litter every biome; sealed **ancient dangers** hold
  dormant scythers, lancers, and centipedes beside the cryptosleep caskets they besieged.
- Hostile mechanoids act as a permanently hostile planetary faction (the mechanoid hive),
  descending in raids, crashed ship parts, and fortified clusters with mortars, turrets, and
  psychic effectors.
- A **mechlink** implant lets a human — a **mechanitor** — directly command mechanoids, gestate
  new ones, and keep them from going feral; soldiers used mechlinks for war mechs, workers for
  labor mechs. High-end mechanoids (war queens, diabolus, apocriton) answer only to mechanitors
  or to whatever ancient intelligence still coordinates the wild ones.
- Odyssey-era exploration finds **mechanoid communication relays** — gravcore-powered nodes of a
  huge mechanoid network — and wrecks guarded by ancient, inhuman defenses.

## 8. Insectoids

- Insectoid ecosystems were **genetically engineered to fight mechanoid invasions**; the two are
  always mutually hostile. Wiki lore names the planet **Sorne** as their original homeworld,
  from which they were captured, gene-modified, vat-grown by interstellar entrepreneurs, and
  exported to other worlds as living weapons.
- On rimworlds they thrive underground: infestations erupt from overhead mountain, building
  hives, tunneling, and filling caves with megaspiders, spelopedes, and megascarabs. Their
  jelly feeds them and anyone desperate enough to raid it.

## 9. Xenohumanity

Humanity's branches — by unplanned adaptation or deliberate gene-editing. **Xenotypes** carry
germline genes (inherited) or implanted xenogenes (from a xenogerm). All are human; rule 3 holds.

- **Baseliner** — unmodified Earth-stock humans; still the default everywhere.
- **Starjack** *(Odyssey)* — one of humanity's oldest xenotypes, made in early space colonization
  when modifying people was cheaper than redesigning ships. Reflective pressure-adaptive skin,
  vacuum-tolerant, frugal metabolism; at home on stations and gravships, nearly helpless dirtside.
- **Genie** — engineered thousands of years ago by a long-disbanded space navy to crew starship
  engineering posts; today they serve as engineers, lawyers, pilots, musicians. Fragile, brilliant.
- **Hussar** — engineered super-soldiers: superb fighters, poor at everything else, chemically
  bound to go-juice. Famous for blood-red eyes and the unsettling dead expression called the
  "hussar stare".
- **Sanguophage** — descendants of the lord-explorer **Varan-Dur**, who tried to seize control of
  a hyperintelligent archotech and was transformed by it instead. Ageless hemogen-drinkers who
  deathrest in coffins, spread their strain by reimplantation, and hide among baseliner societies.
- **Highmate** — engineered companions: beautiful, psychically bonding, bred to love and soothe.
- **Neanderthal** — archaic humans resurrected by geneticists; strong, stolid, slow to learn,
  often mocked and often underestimated.
- **Pigskin** — pig-derived gene lines made for cheap labor and organ compatibility; snouted,
  hardy, widely despised, keenly aware of it.
- **Dirtmole** — tunnel-adapted miners with weak eyes and strong arms; underground folk.
- **Waster** — adapted to pollution and toxin; immune to rot-stink and toxic fallout, fond of
  chemicals, born of ruined toxic worlds and wastelands.
- **Impid** — fire-spitting, heat-adapted desert dwellers; fast, thin-skinned, tribal.
- **Yttakin** — furry, cold-adapted, famously strong; many crew pirate bands of the cold rim.

Supporting gene-tech lore: **xenogerms** rewrite a living person's xenogenes; **genepacks** are
tradable gene libraries; **growth vats** gestate embryos and accelerate children; **archite
genes** (the sanguophage tier) require archite capsules — archotech-grade and beyond human
manufacture.

## 10. The Empire (Royalty)

- The Empire on the rim is a **fragment of a destroyed interstellar empire** — a refugee fleet
  that escaped when some unknown enemy annihilated the rest. Few in number, immensely strong in
  technology, rigid in honor.
- It originated from the glitterworld **Sophiamunda** and lived for thousands of years as a
  stable multi-planet civilization with a strict caste system, an intricate code of warrior
  ethics, and enforced cultural stasis.
- It is feudal because physics demands it: with no FTL, the far-off Emperor cannot rule
  day-to-day, so **stellarchs** hold real dominion over star systems, **consuls** over planets,
  **domini** over provinces, mega-cities, and moons. Visits from the Emperor come years or
  decades apart even between neighboring imperial systems.
- The title ladder pawns can climb: freeholder → yeoman → acolyte → knight/dame → praetor →
  baron/baroness → count/countess (and above them duke, consul, stellarch, emperor). Rank is
  bestowed in ceremony, carries conduct and apparel expectations (nobles do not haul), and
  unlocks psylink levels and imperial permits (orbital strikes, shuttles, troop drops).
- Imperial troops run from janissaries to cataphracts in powered armor; imperial culture prizes
  eltex finery, bladelink **persona weapons** that bond to a single wielder's mind, and
  techprints that ration knowledge itself.
- Not all accept the order: **deserters** leak imperial tech to the rim and plot against the
  throne. A colony that shelters the High Stellarch long enough can leave the planet under
  imperial protection (the Royal Ascent ending).

## 11. The Void (Anomaly)

- Beneath one more rimworld ruin sits a **void monolith**. Studying it opens contact with
  **Horax** — an insane archotech machine-mind of unfathomable power. The horrors that follow are
  not supernatural: they are dark archotechnology, which on the rim is a distinction without
  much comfort.
- Its manifestations: **metalhorrors** that ride inside human hosts and multiply; **revenants**
  that hypnotize and vanish; **noctols** and **sightstealers**; **fleshbeasts**, fleshmass
  hearts, and pit gates from below; **devourers**, **chimeras**, the **nociosphere**, the
  **golden cube** obsession, unnatural corpses, blood rain, death pall, and twisted obelisks of
  jagged, oily metal that throb psychically and kill the plants around them.
- **Ghouls** and **shamblers** are its human wreckage: the reworked living and the raised dead.
  **Deadlife dust** shows death itself can be weaponized by archotech.
- The **Horax cult** ("The Servants of Horax") worships the entity — chanting crowds, skip
  abductions, summoned fleshbeasts. Whether Horax actually speaks to them, or they only believe
  it does, is unknown.
- The arc ends at a **void node**: a colonist can step into the void and embrace it, or the
  colony can sever the connection and banish the darkness. Either way, someone comes back
  changed — or doesn't come back.

## 12. Gravtech and the Sky (Odyssey)

- A **grav engine** — the ultratech heart of a **gravship** — generates lift by inverting and
  intensifying gravity in a field around itself. It only works deep in a planetary gravity well:
  gravships hop across a planet and up to orbit, but can never cross between worlds. The five
  rules stand.
- Gravship crews live a wandering odyssey: salvaging **gravcores** from mechanoid wrecks and
  ancient stockpiles, landing on collapsed orbital platforms, ore-rich asteroids, and
  hollowed-out satellites, jamming signals, and prying gravtech from ancient, inhuman defenses.
- Orbital ruins confirm what the ground ruins imply: this planet's sky was once busy, and
  whatever war emptied it left working machines behind.

## 13. Who Lives on a Rimworld

The standing cast of factions a colonist's diary can plausibly mention:

- **Outlanders** (industrial) — town-dwelling unions, civil (neutral, tradable) or rough
  (hostile until befriended). The rim's shopkeepers, farmers, and militias.
- **Tribes** (neolithic) — gentle, fierce, or savage; descendants of collapsed colonies who kept
  the stories but lost the machines.
- **Pirate bands** — raiders living off everyone else; the rim's constant weather.
- **The Empire and its deserters** — see §10.
- **Mechanoid hive** — see §7. Permanently hostile; not people, and yet they wage war like people.
- **Insectoid hives** — see §8.
- **The Ancients** — soldiers of a long-forgotten spacer military who sealed themselves in
  cryptosleep caskets inside ancient shrines. Their allegiance, war, and civilization's very name
  are lost; some wake grateful, some wake shooting.
- **Horax cult** — see §11. Hostile to everyone, including the other monsters.

## 14. Substances, Artifacts, and Small Legends

Dense flavor items with real lore weight, for texture in diary entries:

- **Luciferium** — "Lucifer's bargain": archotech-grade mechanites that heal what cannot be
  healed, sharpen mind and body — and kill with madness if the dose ever stops. No cure, ever.
- **Go-juice** — combat stimulant and painkiller; the hussars' leash.
- **Yayo / flake / psychite tea** — three faces of the psychoid leaf, from teatime comfort to
  gutter addiction. **Smokeleaf** — the rim's joint. **Wake-up** — productivity in a vial.
  **Ambrosia** — fruit that addicts; groves sprout mysteriously.
- **Penoxycyline** — plague-proofing on a schedule; **glitterworld medicine** — the most
  powerful medicine human industry makes, ultratech-only.
- **Healer / resurrector mech serums** — one-shot archotech mechanite doses that cure the worst
  condition in a body, or restart a fresh corpse outright. Resurrection can come back wrong:
  blindness, dementia, psychosis.
- **Persona core** — a dormant peak-human-equivalent machine mind; installed in a proper support
  structure it becomes a mind of great power — the irreplaceable brain of any starship built from
  rim scrap (the classic ship-launch ending).
- **Persona weapons** (bladelink) — ultratech blades housing a machine persona that bonds
  permanently to one wielder's mind; some whisper, some rage (kill-thirst), some refuse to be
  put down.
- **Joywire** — a brain implant that trades ambition for bliss. **Painstopper** — no pain, no
  warnings.
- **Cryptosleep sickness** — the retching disorientation of waking from the long cold.
- **Ship chunks** — debris from unseen orbital traffic, falling like slow meteors; the rim's
  scrap economy.
- **Toxic fallout, volcanic winter, solar flares, zzzt** — the sky itself as antagonist.
- **Thrumbo** — a gigantic, gentle, unicorn-horned creature of unknown origin; extremely
  dangerous enraged. Some scientists believe thrumbos were engineered as status symbols, or as
  an art project. Killing one is legend; taming one is myth made flesh.
- **Boomalopes and boomrats** — gene-crafted living chemfuel tanks that explode on death.
  **Wargs** — bio-engineered war wolves that eat only raw meat. **Muffalo** — the frontier's
  shaggy truck.
- **Ideoligions** — on the rim, belief is modular: memes (core ideas) and precepts (rules) wrap
  around a structure of worship — theist, animist, archist, void-touched. Relics are bones of
  meaning carried in reliquaries; rituals (trees, dances, skylanterns, sacrifices) hold colonies
  together. Every ideoligion generates its own origin lore, and every believer writes diary
  entries inside it.

## 15. Prompt-Voice Hooks (Derived Guidance)

Compressed implications for enchantment text — how lore should *sound* in a diary line:

- Distance is grief: anyone from off-world left everyone they knew decades of lightspeed away.
  Letters home are prayers, not mail.
- The glitterworld is the rim's heaven-myth; the urbworld its purgatory story; the marble its
  hell. Pawns compare their day to these places.
- Tech mixing is unremarkable to locals, astonishing to newcomers. A tribal pawn names a gun the
  way a spacer names a spirit.
- Archotech is met with awe, dread, or worship — never understanding. "The machine gods" is a
  fair colloquialism; an engineer knows better and fears more.
- Cryptosleep makes time personal: a pawn may be centuries older than their face, or have
  outslept their entire civilization.
- Mechanoid and insectoid attacks are old-war weather, not novelty: "the old machines woke up
  again."
- Every ruin is somebody's ended story; every ancient danger is a door someone chose to seal.
- The Empire reads as both promise and threat: law, titles, and orbital fire from the same hand.
- Void phenomena should be written as wrongness leaking into the ordinary — oily metal, silence,
  a name (Horax) nobody should know.
- Nobody says "FTL", "aliens", or "the galaxy far away" as realities. The stars are slow, empty
  of others, and utterly indifferent.

## Sources

- [Lore](https://rimworldwiki.com/wiki/Lore) — primary page (setting rules, worlds, tech levels,
  travel, transcendence).
- [About RimWorld](https://rimworldwiki.com/wiki/About_RimWorld), [Human](https://rimworldwiki.com/wiki/Human),
  [Character Types](https://rimworldwiki.com/wiki/Character_Types), [Backstories](https://rimworldwiki.com/wiki/Backstories)
- [Archotech](https://rimworldwiki.com/wiki/Archotech), [Psycasts](https://rimworldwiki.com/wiki/Psycasts),
  [Psylink neuroformer](https://rimworldwiki.com/wiki/Psylink_neuroformer), [Psychic emanator](https://rimworldwiki.com/wiki/Psychic_emanator),
  [Crashed ship parts](https://rimworldwiki.com/wiki/Crashed_ship_parts)
- [Mechanoids](https://rimworldwiki.com/wiki/Mechanoids), [Mechanitor](https://rimworldwiki.com/wiki/Mechanitor),
  [Mechanoid hive](https://rimworldwiki.com/wiki/Mechanoid_hive), [Insectoids](https://rimworldwiki.com/wiki/Insectoids),
  [Thrumbo](https://rimworldwiki.com/wiki/Thrumbo)
- [Xenotypes](https://rimworldwiki.com/wiki/Xenotypes), [Sanguophages](https://rimworldwiki.com/wiki/Sanguophages),
  [Hussars](https://rimworldwiki.com/wiki/Hussars), [Genies](https://rimworldwiki.com/wiki/Genies),
  [Starjack](https://rimworldwiki.com/wiki/Starjack), [Genes](https://rimworldwiki.com/wiki/Genes)
- [Empire](https://rimworldwiki.com/wiki/Empire), [Titles](https://rimworldwiki.com/wiki/Titles),
  [Royalty (DLC)](https://rimworldwiki.com/wiki/Royalty_(DLC)), [Endings](https://rimworldwiki.com/wiki/Endings)
- [Ideoligion](https://rimworldwiki.com/wiki/Ideoligion), [Archonexus core](https://rimworldwiki.com/wiki/Archonexus_core)
- [Anomaly (DLC)](https://rimworldwiki.com/wiki/Anomaly_(DLC)), [Entities](https://rimworldwiki.com/wiki/Entities),
  [Void monolith](https://rimworldwiki.com/wiki/Void_monolith), [Horax cult](https://rimworldwiki.com/wiki/Horax_cult)
- [Odyssey (DLC)](https://rimworldwiki.com/wiki/Odyssey_(DLC)), [Gravship](https://rimworldwiki.com/wiki/Gravship),
  [Grav engine](https://rimworldwiki.com/wiki/Grav_engine)
- [Factions](https://rimworldwiki.com/wiki/Factions), [Outlanders](https://rimworldwiki.com/wiki/Outlanders),
  [Tribes](https://rimworldwiki.com/wiki/Tribes), [Ancients](https://rimworldwiki.com/wiki/Ancients),
  [Ruins](https://rimworldwiki.com/wiki/Ruins)
- [Luciferium](https://rimworldwiki.com/wiki/Luciferium), [Healer mech serum](https://rimworldwiki.com/wiki/Healer_mech_serum),
  [Resurrector mech serum](https://rimworldwiki.com/wiki/Resurrector_mech_serum), [Glitterworld medicine](https://rimworldwiki.com/wiki/Glitterworld_medicine)

Collection method note: direct fetch of rimworldwiki.com was blocked from this environment, so
content was gathered through search-indexed snapshots of the wiki pages above, cross-checked
against the stable published lore (the wiki Lore page and Fiction Primer have been substantially
unchanged for years). Wording is condensed, not verbatim.
