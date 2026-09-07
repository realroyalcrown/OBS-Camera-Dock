# OBS Camera Dock pro Windows — 0.4.3 preview

**Stav vydání 0.4.3:** základní ovládání **Razer Kiyo V2 X ve Windows je funkční a ověřené uživatelem**. Logitech C920, ostatní UVC kamery a automatické funkce podle obličeje (expozice, ostření, tracking) jsou experimentální a vyžadují další doladění.

Nově: **[expozice podle obličeje, ostření na obličej a digitální trackování v OBS](FACE-FEATURES.md)**. Přístup přes novou záložku **Obličej**, vyžaduje zapnutý lokální WebSocket server OBS. Funkce jsou při spuštění vypnuté.

Samostatný helper pro Windows 10/11 x64 a OBS Custom Browser Dock. Používá systémový .NET Framework 4.8 a DirectShow rozhraní `IAMCameraControl` / `IAMVideoProcAmp`. Nepotřebuje OBS plugin, Node.js, Python ani instalaci USB ovladače. Nevytváří video stream ani capture graph.

## Spuštění

1. Spusťte `OBS Camera Dock.exe`. V oznamovací oblasti se objeví ikona aplikace; může být skrytá pod šipkou.
2. Dvojklik na ikonu otevře ovládání. V nabídce pravého tlačítka lze kopírovat URL, vyhledat kamery a helper ukončit.
3. V OBS otevřete **Docks → Custom Browser Docks…**, zadejte název `Camera` a URL **http://127.0.0.1:24680/**.
4. V horní části doku vyberte kameru. Výběr se uloží pro příští spuštění. Po připojení/odpojení kamery použijte ↻.

Helper musí zůstat spuštěný. Port používá pouze jedna instance. Aplikace nevyžaduje správce, URL ACL ani pravidlo firewallu: server naslouchá výhradně na IPv4 loopbacku. Používejte přesně adresu `127.0.0.1`, ne `localhost`.

## Kamery a ovladače

- Razer Kiyo V2 X, Logitech HD Pro Webcam C920 a další UVC kamery dostupné přes Windows DirectShow.
- Zobrazuje se pouze podporovaná podmnožina: expozice, focus, zoom, pan, tilt, jas, kontrast, saturace, ostrost, white balance, backlight a gain.
- AUTO/MAN se nabídne jen tam, kde ovladač hlásí podporu obou režimů.
- Rozsah a krok se načítají z ovladače. Ne všechny UVC kamery podporují všechny parametry; proprietární extension-unit ovladače nejsou implementované.
- Windows expozice je v log₂ sekundách: např. −6 = 1/64 s. Dok ukazuje skutečně podporované časy, proto požadavek na 1/60 s může znamenat nejbližší 1/64 s. Viz [Microsoft CameraControlProperty](https://learn.microsoft.com/en-us/windows/win32/api/strmif/ne-strmif-cameracontrolproperty).
- Focus ve Windows používá 0–100 % nativního rozsahu ovladače. Směr blízko/daleko je závislý na zařízení. macOS převod zůstává zachovaný.
- ISO je orientační zobrazení rozsahu gainu, nikoli kalibrovaná citlivost senzoru.
- Reset vrací číselné výchozí hodnoty ovladače a zapíná AUTO tam, kde je dostupné.

Samotná detekce kamery není potvrzením plné kompatibility. Ovladač může odmítnout změnu nebo souběžné otevření s OBS; API tuto chybu zobrazí. Pokud jiná aplikace mění stejné parametry, mohou se přepisovat.

## Presety

Ukládají se odděleně pro každou kameru do:

```text
%LOCALAPPDATA%\OBS Camera Dock\<identifikátor kamery>\presets-windows.json
```

Soubor `selected-camera.txt` obsahuje poslední výběr. Přesunutí kamery do jiného USB portu může změnit její systémový identifikátor a tím i adresář presetů. macOS presety se automaticky neimportují: nativní rozsahy a zejména expozice jsou jiné.

Kiyo zachovává chování `rrc_base`: po prvním úspěšném připojení v rámci běhu helperu aplikuje MAN expozici nejblíže 1/60 s, gain odpovídající zobrazenému ISO 400, jas 48 %, focus 68 % a WB 4200 K, pokud jsou podporované. Tento preset se při startu přepíše. Ostatním kamerám helper při spuštění nastavení nemění. Načtení uživatelského presetu nastaví nejdříve hodnoty a až poté AUTO režimy.

Zápis presetů používá dočasný soubor a atomické nahrazení se zálohou `.bak`. Chyby zápisu nejsou ignorovány. Při chybě ovladače může být preset aplikován jen částečně; aplikace chybu oznámí.

## Sestavení

V kořeni repozitáře:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1
```

Výsledek: `dist\windows\OBS Camera Dock.exe`. HTML je vloženo přímo do EXE. Build používá systémový C# kompilátor; neprovádí stahování balíčků. Před novým sestavením běžící helper ukončete.

## Diagnostika a testy

```powershell
# Diagnostika pouze čte stav; neaplikuje startup preset.
& '.\dist\windows\OBS Camera Dock.exe' --camera C920 --diagnose "$PWD\c920.json"
& '.\dist\windows\OBS Camera Dock.exe' --camera Kiyo --diagnose "$PWD\kiyo.json"

# Server bez ikony; --data-dir umožní oddělit testovací presety.
& '.\dist\windows\OBS Camera Dock.exe' --headless --data-dir "$PWD\dist\test-data"

# PowerShell 7: self-test + HTTP testy při běžícím helperu.
pwsh -File .\scripts\test-windows.ps1
# Volitelně Node.js pro testy převodů sdíleného UI.
node .\scripts\test-windows-ui.cjs
```

EXE je Windows GUI aplikace, diagnostiku zapisuje do uvedeného souboru. Pro synchronní čekání ve skriptech použijte `Start-Process -Wait`. Chyby při spuštění v headless/diagnostickém režimu jsou v `%TEMP%\OBS-Camera-Dock-error.txt`.

Ověřeno v tomto vývojovém prostředí: sestavení x64, self-testy rozsahů a presetů, HTTP testy, převody UI pro Windows i macOS, načtení doku a výběr kamer. Detekovány Kiyo V2 X, C920 a Elgato 4K S. Uživatel potvrdil funkčnost základní Windows verze při běžném spuštění. Ve vývojovém sandboxu otevření USB zařízení nadále vrací `0x80070005 E_ACCESSDENIED`. Stav nových funkcí podle obličeje je popsán v `FACE-FEATURES.md`.

Při „Přístup byl odepřen“ zkontrolujte **Nastavení → Soukromí a zabezpečení → Kamera → Přístup ke kameře / Povolit desktopovým aplikacím přístup ke kameře**. Ověřte EXE spuštěné běžným dvojklikem mimo omezené vývojové prostředí. Pokud chyba zůstává, zkuste ukončit aplikace používající kameru a opakovat diagnostiku. Souběh s OBS je nutné ověřit na konkrétním zařízení.

## API

Původní cesty `/api/state`, `/api/control`, `/api/rescan`, `/api/reset` a `/api/presets/*` zůstávají. Windows stav přidává `cameras: [{id,name}]`, `deviceId` a u expozice `encoding: "log2Seconds"`.

Nová cesta: `POST /api/camera` s JSON `{"id":"<id ze seznamu cameras>"}`. POST požadavky vyžadují `Content-Type: application/json`. Volitelné `cameraDeviceId` chrání požadavky před zápisem do jiné kamery po přepnutí. Cizí HTTP Origin a Host se odmítají.

Zdrojové soubory macOS a jeho sestavovací skript zůstávají v repozitáři. Sdílené UI rozlišuje škálu expozice podle metadat API. Licence GPL-3.0, viz kořenové `LICENSE` a `NOTICE.md`.

## Ikona a aktualizace

Verze 0.4.3 má vlastní ikonu kamery s korunou v EXE i oznamovací oblasti. Nabídka pravého tlačítka obsahuje odkaz realroyalcrown.eu, verzi sestavení a Zkontrolovat aktualizace.

Kontrola probíhá pouze na vyžádání přes GitHub API (nejvýše 100 posledních vydání). Hledá novější publikovaný balíček OBS-Camera-Dock-Windows-X.Y.Z-preview.zip nebo OBS-Camera-Dock-Windows-X.Y.Z.zip, včetně preview vydání. Po potvrzení otevře stažení v prohlížeči. ZIP rozbalte, ukončete předchozí helper a spusťte nové EXE. Nastavení v LOCALAPPDATA zůstává zachované. Nejde o automatické přepsání běžící aplikace. Windows balíček musí být nejprve publikovaný jako příloha GitHub Release; lokální sestavení není automaticky zveřejněné.
