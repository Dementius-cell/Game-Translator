# OCR Language Pack Management

Status: approved production UX helper, 2026-07-03.

## Purpose

The application needs a compact way to verify and prepare OCR language packs for the common game/comic scenarios used during calibration and manual smoke work:

- Windows OCR: `en-US`, `ja-JP`, `ko-KR`, `th-TH`, `zh-CN`, `zh-HK`, `zh-TW`.
- Tesseract OCR: `eng`, `jpn`, `jpn_vert`, `tha`, `kor`, `chi_sim`, `chi_sim_vert`, `chi_tra`, `chi_tra_vert`.

## UX

The profile editor exposes an OCR language-pack checklist inside the OCR settings area:

- Check common: checks all common Windows OCR and Tesseract targets through `IOcrLanguagePackService`.
- Install Tesseract: downloads missing common Tesseract `.traineddata` files only.
- Windows OCR command: shows an OCR-only elevated PowerShell command for Windows capabilities.

The existing selected-zone buttons remain available for checking/installing the OCR language used by the current zone.

## Safety Boundaries

The app must not silently run elevated PowerShell, install Windows capabilities, add `Language.Basic` packs, or alter the user's keyboard/input language list. Windows OCR installation remains an explicit user/admin action outside the app.

Tesseract traineddata files are safe for the app to download into the configured local `tessdata` directory through the existing language-pack service.

## Testing

Smoke tests cover:

- the checklist contains and checks the common Windows OCR and Tesseract targets;
- the batch install command installs only Tesseract targets;
- the Windows OCR helper shows an OCR-only command and does not include keyboard-language mutation commands.
