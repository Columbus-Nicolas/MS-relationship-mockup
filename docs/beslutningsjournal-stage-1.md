# Beslutningsjournal — Stage 1

Backend-fundamentet blev bygget som fjorten opgaver, hver skrevet af en frisk agent og
reviewet af en anden, med en afsluttende gennemgang af hele branchen. Undervejs blev der
truffet **43 afgørelser** — fire af dem af Clara, resten af den koordinerende agent.

Journalen findes her, så de beslutninger ikke kun lever i en samtale der er lukket.
Hver afgørelse står med **hvad den koster, hvis den er forkert**, så de kan omgøres af
nogen der ved bedre.

| | |
|---|---|
| Branch | `second_iteration` |
| Commits | 31, fra `7c80c21` til `fedec9c` |
| Omfang | 90 filer, 11 migrationer, 18 entiteter |
| Tests | 82, alle grønne, mod en rigtig Postgres 16 i container |
| Build | 0 warnings, 0 errors |

Testene kører mod Testcontainers — ingen mocks, ingen in-memory-provider, som kravspecen
kræver for netop identitet, fletning og historik.

---

## De fire beslutninger Clara tog

Der blev stoppet og spurgt, hvor svaret afhang af Columbus' virkelighed frem for af koden.

### R-2 · E-mail på Columbus-brugere blev valgfri

12 af mockuppens 26 brugere har ingen e-mail. Med det oprindelige krav om en unik,
påkrævet e-mail ville det andet tomme felt være blevet afvist af det unikke indeks, og
hele seedet ville rulle tilbage. Nu betyder tom **ukendt** frem for at være en værdi —
samme princip som FR-45 anlægger for kontaktloggen.

**Koster hvis forkert:** en brugerrække kan eksistere uden login-identitet. Stage 3's
Entra-kobling skal håndtere det.

### R-4 · `.env` taget ud af git uden at omskrive historikken

Filen lå allerede committet og pushet fra august (commit `015ba58`), på fire brancher.
Den er nu untracked fra allerførste commit, men lækken står stadig i historikken på
`origin`. Alternativet — at omskrive historikken og force-pushe — ville brække alle
kollegers klon og kræve koordinering først.

**Koster hvis forkert:** de gamle Postgres- og pgAdmin-adgangskoder kan hentes ud af
historikken, indtil de roteres. **Det udestår.**

### R-25 · Det midlertidige login kræver nu aktivt tilvalg

Indtil Entra lander, kan en forespørgsel skrive hvem den er i en header, og systemet tror
på den — uden adgangskode. Før tændte den funktion sig selv på enhver server mærket
`Development`, hvilket er en almindelig fejlkonfiguration, fordi den mærkat normalt kun
styrer harmløse ting som detaljerede fejlbeskeder. Nu skal `DEV_AUTH` sættes aktivt.

Planens egen kommentar påstod, at funktionen *"cannot be reached by accident in any other
environment"*. Det kunne den godt. Koden holder nu det, kommentaren lovede.

**Koster hvis forkert:** en udvikler, der kører API'et uden for de normale
opstartsprofiler, skal sætte én miljøvariabel.

### R-38 · De 70 indlæste relationer får ingen historik

Mockuppen siger om sig selv: *"The Columbus users, every score and every note are
placeholders."* At tilskrive dem historik ville skrive begivenheder, der aldrig er sket,
ind i en append-only tabel der aldrig kan rettes. Fraværet af historik markerer nu, hvad
der er startdata.

Fortryd-funktionen i Stage 2 rammes ikke: den gamle værdi fanges automatisk, første gang
nogen redigerer en indlæst relation.

**Koster hvis forkert:** indlæste relationer har ingen registreret ophavsmand, før nogen
rører dem.

---

## Fejl fundet i planen

Planen var gennemarbejdet, men holdt ikke otte steder. Disse fund krævede enten at måle
de faktiske data eller at se hele branchen på én gang — ingen enkelt opgave kunne have
fanget dem.

### C1 · Skemaet havde 2 fremmednøgler, hvor det skulle have 13

Planen foreskriver præcis én og nævner aldrig resten. Begge krydstabeller havde ingen.
Reglen om ikke at efterlade noget forældreløst (FR-26) var håndhævet ét sted — når man
sletter et dashboard — og intet andet sted. Sletter man et domæne, hænger medlemskaberne
og dingler i stilhed.

Tilføjet i den afsluttende fix-bølge. **Det brækkede ingenting, hvilket *var* fundet:**
intet bevidste, at constraints'ene fandtes. Seks tests gør det nu.

**Koster hvis forkert:** intet. Det var billigst dér, hvor der endnu ikke findes
produktionsdata.

### I3 · 80 kunde-koblinger blev aldrig udtrukket

Planens udtrækningsopskrift læser mockuppens rå datablok, men koblingerne udledes af
`seedUpkeep()`, som kører *bagefter*. Alle 70 kunder stod uden en eneste
Microsoft-kontakt — hvilket er hele det spørgsmål, FR-49 findes for at besvare.

Verificeret før rettelsen: alle ni sælgere findes blandt de 102 profiler, alle 80
kunde-id'er opløses, og alle 70 kunder dækkes.

**Vigtig skelnen:** koblingerne er **læst** fra Thomas' deck — mockuppens egen kommentar
siger *"the mapping is read, not guessed"*. Ejere, kadencer og kontaktlog er derimod
**opdigtet** af samme funktion til demoen, og blev med vilje ikke seedet. De to slags må
ikke slås sammen.

**Koster hvis forkert:** intet.

### R-26 · Historikkens lås havde en sidedør

Planen lovede, at historikken *"cannot be bypassed"*. Den spærrede to af de fire indgange,
EF Core's `DbContext` har — og de to ubevogtede var dem, alle skrivninger før eller siden
når. Agenten beviste hullet ved at **slette en historikrække** gennem det.

Planens egen test kunne aldrig have fanget det, fordi den går gennem den bevogtede dør.

**Koster hvis forkert:** to ekstra overrides, der videredelegerer.

### R-21 · Seks tests, der ikke kunne fejle

Planen læste data tilbage gennem den samme forbindelse, der lige havde skrevet dem, så
testene bekræftede objekter i hukommelsen frem for databasen. Mønstret optrådte seks
steder. Det værste ramte ejer-reglen — planens egen "most worth care"-regel — som ville
have bestået, selv hvis intet blev gemt.

Afgjort én gang og lagt ind i alle berørte opgaver på forhånd.

**Koster hvis forkert:** en håndfuld tests åbner én forbindelse ekstra.

### R-34 · Den test, der skulle beskytte mod tabt arbejde, var inaktiv

Den kontrollerede noget, der allerede var sandt, før flettefunktionen kørte. Man kunne
have slettet hele historik-flytningen, og suiten ville forblive grøn — netop dér, hvor
den flyttede række er den *eneste* overlevende kopi af en persons vurdering.

**Koster hvis forkert:** to skarpere assertions.

### R-32 · En ejer kunne overleve den relation, der berettigede ham

Reglen blev håndhævet, når ejeren blev sat, og aldrig igen. Fjernede man relationen
bagefter, blev ejerskabet stående. Kravspecen siger det i nutid: *"A Microsoft person
nobody has a relation to therefore **has no owner**"*. Ellers ville en ejer blive mindet
om at kontakte nogen, de ikke har nogen registreret relation til.

**Koster hvis forkert:** at fjerne en relation rydder et ejerskab, nogen ville have
beholdt. Synligt i data og let at omgøre.

### R-23 · Dublet-detektionen kunne overse folk i stilhed

Identitetsnøglen trak mellemrum sammen; den forespørgsel, der skal fange nær-dubletter,
gjorde ikke. Et navn med dobbelt mellemrum faldt igennem **begge** sikkerhedsnet, og
kalderen fik "ingen match" — altså grønt lys til at oprette dubletten.

Mockup-data blev målt først: de er rene, så seedet var aldrig i fare. Rettelsen beskytter
fremtidig indtastning.

**Koster hvis forkert:** navne vises med sammentrukne mellemrum frem for præcis som
indtastet.

### R-29 · Ændringstypen blev gemt som et tal

Alle andre enums i modellen gemmes som tekst. Netop denne sad i den ene tabel, der er
designet til at kunne læses **uden** applikationen — og som aldrig kan korrigeres. Et bart
`0` modarbejder formålet, og en omrokering af enum'en ville lydløst omtolke al historik.

**Koster hvis forkert:** én ekstra migration.

---

## Ført videre til Stage 2

Reelle forhold, bevidst ikke lukket i Stage 1.

### R-27 · "Ingen skrivning kan omgå historikken" er en konvention, ikke en garanti

Kravspecen formulerer det som strukturelt: *"nothing else has write access to the relation
tables"*. Det leverede er en aftale, håndhævet af review. En symmetrisk lås blev afvist,
fordi den ville brække seederen, flettefunktionen og flere tests på én gang — seederen kan
ikke bruge skrivevejen, da den kører uden en indlogget bruger.

Kommentaren i koden siger nu ærligt, hvad den er. Det afsluttende review anbefalede at
stramme det **sammen med fortryd-funktionen**, hvor den strukturelle ændring er informeret
af, hvad fortryd faktisk har brug for.

**Koster hvis forkert:** en fremtidig bidragyder kan skrive en relation uden historik, og
intet stopper dem.

### R-42 · Gravsten og det unikke indeks er ikke enige

Når to profiler flettes, bliver den ene en gravsten. Matcheren udelukker den — men det
unikke indeks dækker den stadig. Så en importør får "ingen match" og kan derefter
**heller ikke** oprette profilen; den rammer en rå indeksfejl.

Utilgængeligt i dag, fordi der ikke findes nogen oprettelses- eller import-funktion. Skal
lukkes **før** en sådan lander.

**Anbefalet løsning:** lad matcheren *fortælle* om gravstenen og hvem personen nu er.
Ikke et filtreret indeks — det ville tillade to levende profiler at dele nøgle og dermed
bryde garantien om, at et gammelt id altid opløses til én person.

**Koster hvis forkert:** en importør møder en uforståelig databasefejl i stedet for et
brugbart svar.

### I9 · Kravspecens §4.2 lover et fuldt CRUD-API i Stage 1

Det leverede er tre læse-endpoints. Planens 14 opgaver bad aldrig om skrive-endpoints, så
her er det **planen**, der er uenig med kravspecen — og kravspecen vinder. At bygge det nu
er ikke det rigtige svar, men det bør stå skrevet, at Stage 4 ejer det, frem for at §4.2
stille forbliver uopfyldt.

**Koster hvis forkert:** skrivevejen er kun afprøvet via direkte kald i tests, aldrig over
HTTP.

### R-15 · Pakkeversioner er ikke låst

Nogle pakker er pinnet eksakt, andre flyder. Den blanding brækkede byggeriet én gang under
Task 2. Central Package Management blev udskudt, fordi planens senere opgaver indeholdt
kommandoer, der ville opføre sig anderledes under det.

**Koster hvis forkert:** skal afgøres når CI lander — en CI uden låste versioner bygger
ikke det samme to gange.

---

## De øvrige 28 afgørelser

Mekaniske eller lokale valg.

| Nr. | Afgørelse | Koster hvis forkert |
|---|---|---|
| R-1 | Seeder og flettefunktion skriver relationer direkte; planens modsatte sætning er en fejl | Seedede relationer får ingen oprettelses-historik |
| R-3 | Seed-tests får deres egen database på samme container | Beviser ikke, at seederen sameksisterer med andre data |
| R-5 | Dev-login-testene bruger den form, der faktisk har en database | Ingen |
| R-6 | Udtrækningsscriptet manglede en konstant og fejlede | Ingen; mekanisk |
| R-7 | Brugernes kompetencer blev tabt af planens seeder — nu med | En kolonne bærer data, intet læser endnu |
| R-8 | Skabelonrester fra `dotnet new` slettet | Ingen |
| R-9 | Seed-blokken placeret før `app.Run()` | Ingen |
| R-10 | `seed.json` kopieres til output — ellers fejler hver seed-test | Ingen; påkrævet for at koden kan køre |
| R-11 | Exit-kriteriet er indfriet med felter til stede og tomme | Stagen ser tyndere ud, end planens prosa antyder |
| R-12 | Processens arbejdsmappe holdes ude af git | Ingen |
| R-13 | To ekstra skabelonfiler fjernet ud over det bestilte | To trivielt genskabelige filer |
| R-14 | Planens fixture-kode brugte en forældet konstruktør | Én linje i en privat initialisering |
| R-17 | Utestet "findes ikke"-gren fik sin test | Én ekstra test |
| R-18 | Systemboard rapporteres frem for "har domæner" | En fremtidig controller skal bruge to forespørgsler |
| R-19 | Intet filtreret indeks nu; hullet ført til Task 14 | Dubletter af "Unmarked"-typen bliver mulige senere |
| R-20 | Enums ligger i egne filer, som planens fil-lister kræver | Nogle små filer at slå sammen senere |
| R-22 | Kompetence-kolonnen får ingen databasestandard | Skrivninger uden om EF skal angive listen |
| R-24 | Mistænkte dubletter matches kun på navn | Støjende forslag ved mange ens navne |
| R-28 | Audit-kolonner asserteres nu mod databasen | To ekstra forbindelser i tests |
| R-30 | Append-only-testene redigerer nu, ikke kun sletter | Ingen |
| R-31 | Ompegning af historik ved fletning er den ene tilladte undtagelse | Flettet historik skal findes via gravstenen |
| R-33 | To ekstra tests for domæne- og delte links bliver stående | To ekstra tests |
| R-35 | Man kan ikke længere flette ind i en gravsten | En fletning afvises, hvor nogen ville have tilladt den |
| R-36 | Kæder af gravsten flades ikke ud nu | En senere opslagsfunktion skal følge kæden |
| R-37 · R-40 | Fem mindre fund foldet ind i eksisterende rettelser | Synligt noteret som afvigelse fra normal disciplin |
| R-39 | Fire assertions låser beslutningerne om seedets indhold | Fire linjer |
| R-41 | En for bred scope-grænse blev rettet | Omkring femten linjers test |
| R-43 · R-44 | Tre små fund parkeret; udviklerprofilen tænder dev-login med vilje | En misvisende kommentar overlever; én linje at fjerne |

---

## Det, der udestår

**Rotér adgangskoderne.** `.env` er untracked nu, men ligger stadig i git-historikken på
`origin` fra commit `015ba58` i august, på fire brancher. Postgres- og
pgAdmin-adgangskoderne skal skiftes.

**API'et må ikke nå en offentligt tilgængelig URL, før Entra lander i Stage 3.**
`GET /api/ms-profiles` har ingen adgangskontrol — korrekt for denne stage, men det
serverer navngivne Microsoft-medarbejderes data.

**Stage 0 er ikke lukket.** Der er ingen pipeline, ingen Dockerfile og ingen
infrastrukturdefinition i repoet. Stage 2's exit-kriterium kræver at gendanne data *"into
a test environment"*, og der findes ikke noget miljø. Kravspecens TEC-06 udpeger netop
dette som første iterations fejl.

---

*En webudgave af denne journal findes som Artifact:
<https://claude.ai/artifact/VkkWSDUy4nroJPYKsvwE9D> (privat, indtil linket deles).*
