# Changelog

## 0.4.3 Windows preview

- Vlastní vícerozměrová ikona kamery s korunou v EXE a oznamovací oblasti.
- Odkaz realroyalcrown.eu a verze sestavení v nabídce lišty.
- Ruční kontrola GitHub aktualizací pro Windows a nabídka stažení novějšího ZIP balíčku.
- Testy výběru aktualizace a načtení vložené ikony.

## 0.4.2 Windows preview

- Opraveno porovnání OBS a DirectShow identifikátoru kamery: dekódování OBS znaků #22 a #3A před přesným porovnáním cesty při spuštění i průběžné kontrole.
- Regresní testy shody kamery, jiného kusu stejného modelu a chybějící identity.

## 0.4.1 Windows preview

- Pohyb výřezu běží nezávisle na detekci na samostatném OBS spojení s cílem 30 aktualizací/s.
- Časově řízené vyhlazování polohy a zoomu, omezení rychlosti a potlačení drobného chvění detekce.
- Zastavení čeká na dokončení pohybu před obnovou záběru; zachována ochrana ručních změn.
- Test s pozastavenou detekcí naměřil přibližně 30 aktualizací/s; prošly integrační a regresní testy.

## 0.4.0 Windows preview

- Nový panel Obličej: lokální náhled z OBS, vestavěná Windows detekce, expozice a kontrastní ostření podle obličejové oblasti.
- Digitální trackování přes výřez/zoom konkrétního zdroje v OBS, plynulý pohyb, maximální zoom a návrat při ztrátě obličeje.
- Lokální OBS WebSocket 5.x s autentizací, výběr scény a zdroje, kontrola shody zdroje s USB kamerou.
- Obnova původní transformace, záznam pro zotavení po výpadku a ochrana ručních změn v OBS.
- Testy detektoru, algoritmů a integrační WebSocket test. Opravena obnova nulových neaktivních rozměrů bounds vracených OBS.

## 0.3.0 Windows preview

- Samostatný Windows x64 helper s ikonou v oznamovací oblasti, lokálním HTTP API a vloženým webovým dokem.
- DirectShow discovery a standardní ovladače UVC kamer, výběr kamery a zapamatování výběru.
- Windows log₂ expozice, nativní rozsahy a kroky, detekce podpory AUTO/MAN.
- Presety oddělené pro každou kameru, atomické ukládání a hlášení chyb ovladače.
- Sestavení bez stahování závislostí a testy API, presetů a převodů UI.
- Stav: build a softwarové testy prošly; skutečné změny na Kiyo/C920 blokuje E_ACCESSDENIED při otevření v testovacím prostředí.

## 0.2.2 — 2026-08-14

- Add Expo / Obraz / Optika dial panels with AUTO/MAN toggles.
- Add per-panel presets persisted to Application Support.
- Add factory preset `rrc_base` (1/60, ISO 400, brightness 48, focus 68, WB 4200 K) applied automatically on helper startup.
- Invert focus dial so higher values focus nearer.
- Invert white-balance arc color gradient.
- Expose pan, tilt, and backlight when the camera reports them.

## 0.2.1 — 2026-08-14

- Compact the OBS browser dock UI for ~450×320 panels.
- Collapse the header into a single status row.
- Render controls as dense single-line rows (label / slider / value).
- Keep action buttons pinned while allowing the control list to scroll.

## 0.1.1 — 2026-07-19

- Fixed an infinite loop while parsing a composite USB device with non-video interfaces before its UVC control interface.
- Parse the configuration descriptor while its owning IOUSB device interface is still valid.
- Bound descriptor traversal by `wTotalLength` and validate every `bLength`.
- Keep the first matching UVC control interface instead of leaking and replacing it during enumeration.
- Add a fallback open/request/close sequence for devices that reject a direct UVC request.
- Verified all exposed controls as supported on a connected Razer Kiyo V2 X (USB VID `0x1532`, PID `0x0E0C`) on macOS 26.5.2.

## 0.1.0 — 2026-07-19

- Initial OBS browser dock and macOS menu-bar helper.
