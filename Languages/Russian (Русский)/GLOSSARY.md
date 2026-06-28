# Глоссарий русской локализации Pawn Diary

Канонические переводы игровых терминов RimWorld, использованные в этом переводе. Источник —
**официальные языковые паки** игры (`Data/<Core|Royalty|Ideology|Biotech|Anomaly>/Languages/Russian (Русский).tar`).
При добавлении новых строк сверяйтесь с этой таблицей, чтобы термины оставались
едиными и совпадали с официальным переводом. Если термина здесь нет — извлеките его из
тех же паков (`tar -xf` → grep по defName), а не переводите на слух.

> Не игровые термины (общая лексика, тон, литературные приёмы персон) переводятся свободно.
> Манеры письма (`DiaryPersonaDef`) — отдельный случай: см. [DiaryPersonaDefs.xml](DefInjected/PawnDiary.DiaryPersonaDef/DiaryPersonaDefs.xml)
> (реконструкция на русской традиции, без имён авторов), а не дословный перевод.

## Интерфейс и подключение моделей

Эти слова не из официального перевода RimWorld, но используются в видимом UI мода. Если термин
можно понятно сказать по-русски, не оставляйте англицизм без нужды.

| English / technical | Русский в UI |
|---|---|
| endpoint | адрес API |
| API row / API config | подключение |
| routing | распределение / выбор моделей |
| prompt studio | редактор запросов |
| system prompt | системная инструкция |
| forced model | модель для события |
| tag | метка |
| label | название |
| LLM | модель |

`API`, `OpenAI`, `URL`, `Bearer`, `XML`, `UTF-8`, имена HTTP-заголовков и имена моделей оставляйте
как технические названия протоколов, продуктов, форматов или конкретных полей.

## Поселение и люди

| English | Русский (официально) |
|---|---|
| colonist / pawn | поселенец |
| colony | поселение |
| slave / slavery | раб / рабство |
| prisoner / prison break | пленник / побег |
| research bench | стол для исследований |
| Social log / interaction log | журнал общения |

## Угрозы и события

| English | Русский |
|---|---|
| raid | налёт |
| raider | налётчик |
| drop pod | транспортная капсула |
| drop-pod raid | налёт из транспортных капсул |
| infestation | насекомые |
| quest | задание |
| heat wave | жара |
| flu | грипп |
| mental break | нервный срыв |
| berserk (mental state) | слепая ярость |
| inspiration | воодушевление |
| mood / thought | настроение / мысль |
| masterwork / legendary (quality) | шедевр (шедевральный) / легенда (легендарный) |
| gravship | гравикорабль |

## Ритуалы, способности, статусы (DLC)

| English | Русский |
|---|---|
| ritual | ритуал |
| psychic ritual | псионический ритуал |
| ability | способность |
| psycast / psycaster | пси-способность / псионик |
| anima tree | древо души |
| royal title | титул |
| ideoligion | идеолигия |
| ideology role | роль |
| xenotype | ксенотип |
| ritual roles: author / target / participant / spectator / invoker | устроитель / цель / участник / зритель / заклинатель |

## Наркотики и их действие (hediff)

| English | Русский |
|---|---|
| alcohol high | опьянение |
| hangover | похмелье |
| ambrosia / ambrosia warmth | амброзия / тепло амброзии |
| go-juice / go-juice high | Go-сок / действие Go-сока |
| luciferium / luciferium high | люциферий / действие люциферия |
| flake / flake high | хлопья психина / кайф от хлопьев психина |
| psychite tea / high | психиновый чай / кайф от психинового чая |
| yayo / yayo high | порошок психина / кайф от порошка психина |
| smokeleaf / smokeleaf high | дымолист / кайф от дымолиста |
| wake-up | бодрин |

## Аномалия (Anomaly DLC)

| English | Русский |
|---|---|
| anomaly | аномалия |
| gray flesh | серая плоть |
| metalhorror / metalhorror suspicion | металлическая жуть / подозрение на металлическую жуть |
| void monolith | монолит пустоты |
| cube / cube interest / withdrawal / rage | куб / интерес к кубу / кубическая ломка / кубический гнев |
| revenant / revenant hypnosis | ревенант / гипноз ревенанта |
| inhumanized | обесчеловечивание |
| corpse torment | месть мертвеца |
| the void | пустота |

## Состояние и обстановка (контекст запроса)

| English | Русский |
|---|---|
| downed | обездвижен |
| pain shock | болевой шок |
| corpse | труп |
| outdoors / indoors | на улице / в помещении |
| bleeding | кровотечение |
| pain | боль |
