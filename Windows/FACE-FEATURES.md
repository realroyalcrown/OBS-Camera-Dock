# Funkce podle obličeje — Windows 0.4.3 preview

Panel **Obličej** přidává expozici podle jasu obličeje, kontrastní ostření v oblasti obličeje a digitální trackování pomocí výřezu a zoomu zdroje v OBS. Přepínače jsou nezávislé a po startu helperu vypnuté.

## Rychlé spuštění

1. Ukončete předchozí helper z ikony v oznamovací oblasti. Spusťte nové `OBS Camera Dock.exe`.
2. V OBS otevřete **Nástroje → Nastavení serveru WebSocket** a povolte server. Výchozí port je **4455**. Heslo nechte zapnuté.
3. V doku na `http://127.0.0.1:24680/` vyberte USB kameru a klikněte na **Obličej**. Panel je také přímo na `http://127.0.0.1:24680/face`.
4. Zadejte port a heslo OBS a klikněte na **Připojit**. Heslo se neukládá na disk a po odeslání zmizí z formuláře.
5. Vyberte scénu a její zdroj kamery. Nabídka obsahuje přímé zdroje typu **Zařízení pro záznam obrazu** (`dshow_input`); kamery uvnitř skupiny či vnořené scény vyberte v jejich vlastní scéně nebo je do cílové scény vložte přímo.
6. Zapněte požadované funkce a stiskněte **Spustit**. Se všemi přepínači vypnutými běží jen náhled a detekce.

Scéna se při připojení předvybere podle aktuální programové scény. Kamera musí v OBS skutečně poskytovat obraz. Helper nespouští streamování, nahrávání ani virtuální kameru a sám nemění programovou scénu.

## Co funkce dělají

**Expozice podle obličeje:** ze středu obličejové oblasti měří medián jasu. Podle odchylky upravuje ruční expozici nebo gain, nejvýše jednou za sekundu. Posuvník nastavuje cílový jas 90–180 z rozsahu 0–255. Časy expozice určuje ovladač; při zvyšování expozice algoritmus nepřekračuje přibližně 1/32 s, aby omezil rozmazání pohybem. Při malých odchylkách preferuje jemné změny gainu. Nejde o změnu jasu celé scény v OBS.

**Ostření na obličej:** jde o softwarové kontrastní ostření. Helper mění focus kamery a porovnává normalizovanou ostrost obličejové oblasti. Po první širší sérii měření zpřesní nejlepší nastavení. Další krátké hledání provede při poklesu ostrosti, nejdříve za 8 sekund. První ostření může trvat několik sekund a dočasně rozostřit obraz. Při velkém pohybu měření přeruší. Po krátkém rozostření může nejvýše 1,5 sekundy měřit poslední známou oblast obličeje; tato odhadnutá oblast neřídí expozici ani tracking. Funkce vyžaduje ručně ovladatelný focus, nenahrazuje chybějící motor ostření.

Pro expozici a ostření musí zdroj OBS odpovídat USB kameře vybrané v hlavním doku. Helper porovná systémové ID zařízení a při neshodě funkce nespustí. Původní hodnoty a AUTO/MAN režimy si uloží v paměti a při zastavení obnoví. Během měření focusu pozastaví změny expozice.

**Trackování:** upravuje pouze transformaci konkrétní položky ve vybrané scéně. Obličej se snaží držet uprostřed při zachování rozměrů původního záběru. Plynule posouvá výřez a mění zoom podle velikosti obličeje, nejvýše do nastaveného limitu 1–3×. Původní ořez bere jako hranici, mimo kterou se neposouvá. U okraje obrazu tedy nemusí být možné obličej přesně vystředit. Kamera se fyzicky neotáčí; digitální zoom snižuje dostupné obrazové rozlišení.

Zelený rámeček je pouze v náhledu helperu, do vysílání se nekreslí. Při více lidech se nejprve vybere největší obličej a následně se sleduje jeho poloha; nejde o rozpoznávání totožnosti. Po ztrátě obličeje se přeruší řízení expozice a focusu a po 3 sekundách se záběr začne plynule rozšiřovat.

## Zastavení a obnovení

- **Zastavit a obnovit** ukončí analýzu, vrátí původní ovladače kamery a transformaci OBS.
- Zavření stránky analýzu nezastaví; běží v helperu. Ukončení helperu přes jeho nabídku provede obnovení.
- Ruční změna ovladače, načtení presetu, reset nebo výběr jiné kamery analýzu zastaví před provedením požadované změny.
- Pokud mezitím ručně upravíte transformaci v OBS, tracking se zastaví a vaši změnu nepřepíše. Případné vrácení původního záběru potvrďte tlačítkem **Obnovit původní záběr**.
- Pro případ pádu nebo výpadku spojení se původní transformace zapisuje do `%LOCALAPPDATA%\OBS Camera Dock\face-tracking-recovery.json`. Po novém připojení k OBS ji lze obnovit. Automatické funkce se po restartu samy nerozbíhají.
- Ovladače USB kamery se po násilném ukončení procesu automaticky neobnoví, protože jejich předchozí stav je pouze v paměti. Lze je nastavit ručně nebo načíst preset.

## Zpracování obrazu a provoz

Obraz se čte z lokálního OBS přes WebSocket 5.x jako zmenšený náhled široký 640 pixelů. Analýza má horní limit přibližně 4 snímky/s; skutečná rychlost závisí na OBS a počítači. Není určena pro rychlé sportovní pohyby. Náhledy se drží pouze v paměti, neukládají se jako soubory a neposílají se do cloudu.

Detekci zajišťuje vestavěný Windows `FaceDetector`. Build využívá systémová WinRT metadata, bez externího modelu nebo balíčků. Helper neotevírá další video stream z USB kamery. Viz [Windows FaceAnalysis](https://learn.microsoft.com/en-us/uwp/api/windows.media.faceanalysis) a [OBS WebSocket protokol](https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md).

Pohyb výřezu od verze 0.4.1 běží samostatně s cílem 30 aktualizací/s přes druhé lokální OBS spojení. Poloha a zoom se průběžně vyhlazují i mezi výsledky detekce. Náhled v panelu zůstává omezený rychlostí analýzy; plynulost pohybu posuzujte přímo v OBS. Skutečná frekvence závisí na odezvě OBS.

## Stav ověření

- Verze 0.4.1: test bez nových výsledků detekce naměřil 34 změn, medián intervalu 34,1 ms a přibližně 30 aktualizací/s. Ověřeno zastavení bez pozdějších zápisů a ochrana ruční úpravy. Plynulost na živém zdroji čeká na uživatelské ověření.
- Sestavení Windows x64, původní API a UI testy, presety a nativní detektor Windows.
- Testy expozice, hledání maxima ostrosti, mezí výřezu a zachování velikosti záběru.
- Integrační test přes skutečný WebSocket klient s testovacím OBS serverem: autentizace, dělené síťové zprávy, náhled, ztráta obličeje, obnova výřezu a ochrana ruční změny.
- Živá detekce a trackování na uživatelově zdroji ve scéně FullCam byly spuštěny bez chyby. Při testování byla nalezena a opravena obnova nulových rozměrů neaktivního ohraničení OBS; původní záběr byl následně obnoven bez nevyřízeného záznamu obnovy. Zdroj bez obrazu je odmítnut před zahájením trackování.
- Základní Windows ovladače předchozí verze uživatel potvrdil jako funkční. Nové automatické řízení expozice a focusu je potřeba doladit na živé kameře při běžném spuštění EXE; vývojový sandbox odmítá otevření USB zařízení.

## API a testy pro vývoj

`GET /api/face/state`, `GET /api/face/preview` a `POST /api/face/connect`, `/api/face/sources`, `/api/face/start`, `/api/face/stop`, `/api/face/restore`. POST používá JSON a stejnou kontrolu Host/Origin jako ruční API. `/api/face/connect` očekává `port` a `password`; `/api/face/start` očekává `scene`, `itemId`, booleany `exposure`, `focus`, `tracking`, číselné `target` a `maxZoom`.

Pro izolované testování lze helper spustit s `--port 24682 --data-dir <testovací adresář>`. Potom spusťte `test-windows.ps1 -BaseUrl http://127.0.0.1:24682`. Integrační test `node scripts/test-face-obs.cjs http://127.0.0.1:24682 <cesta k prázdnému JPEG>` používá pouze testovací server na portu 4466. Nespouštějte ho proti pomocnému procesu s aktivním živým trackováním. Při kompilaci nového EXE musí být cílový soubor uvolněný.
