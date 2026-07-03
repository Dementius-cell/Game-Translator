# Tesseract Local Language Data

Status: active local dependency note.

Tesseract `.traineddata` files are runtime/test dependencies, not source artifacts. Keep them outside normal commits. The repository ignores `tessdata/`, so a developer can place language data under the project root without polluting the worktree.

Tesseract documentation describes using a directory supplied by `TESSDATA_PREFIX`, and selecting multiple OCR languages with the `-l LANG[+LANG]` syntax. The project also checks the repository-local `tessdata/` folder when available.

## Project Baseline

For manual real-OCR runs and vertical/CJK calibration work, keep this baseline available locally:

| File | Purpose |
| --- | --- |
| `eng.traineddata` | English UI and mixed Latin text |
| `jpn.traineddata` | Japanese horizontal text |
| `jpn_vert.traineddata` | Japanese vertical text |
| `tha.traineddata` | Thai dialogue text |
| `kor.traineddata` | Korean text |
| `chi_sim.traineddata` | Simplified Chinese horizontal text |
| `chi_sim_vert.traineddata` | Simplified Chinese vertical text |
| `chi_tra.traineddata` | Traditional Chinese horizontal text |
| `chi_tra_vert.traineddata` | Traditional Chinese vertical text |

The codes match `TesseractLanguageCatalog` in the application layer. Do not rename files to Windows language tags such as `ja-JP`; Tesseract expects traineddata codes such as `jpn` and `jpn_vert`.

## Local Setup

Use one of these layouts:

```text
Game-Translator/
  tessdata/
    eng.traineddata
    jpn.traineddata
    jpn_vert.traineddata
    ...
```

or set `TESSDATA_PREFIX` to another folder that contains those files.

Check the local machine with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-tessdata.ps1
```

For automation-friendly output:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-tessdata.ps1 -Json
```

Use `-FailOnMissing` only when a workflow truly requires real OCR data. Deterministic CI must keep using committed fixtures and mocks unless the issue explicitly promotes a real-OCR gate.
