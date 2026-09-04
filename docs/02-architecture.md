# Архитектурный документ

## Проект

Система экранного OCR-перевода игровых субтитров и текста для Windows 11

Версия документа: 1.2 (обновлено 2026-09-04)

------------------------------------------------------------------------

# 1. Архитектурные цели

Высокая скорость работы, минимальная нагрузка на CPU, работа поверх игр, поддержка нескольких OCR-зон, азиатских языков, расширяемость.

------------------------------------------------------------------------

# 2. Выбор языка программирования

Основной язык приложения: C# (.NET 9). Он отвечает за Windows API, WPF UI, capture, orchestration, перевод и overlay. Python не является вторым приложением или UI-слоем: по ADR-030 он поставляется как зафиксированный runtime узкого GPU Paddle detector worker. Его версии, модель и хеши воспроизводимо собираются через `tools/bootstrap-paddle-runtime.ps1`; recognizer остаётся в .NET/Tesseract пути.

## Производственные модули solution

В solution четыре production-модуля и один test project:

- `GameTranslator.Domain` — профильные модели, геометрия и чистая валидация;
- `GameTranslator.Application` — use cases, порты и orchestration;
- `GameTranslator.Infrastructure` — Windows/persistence/OCR/provider adapters, включая packaged Paddle worker;
- `GameTranslator.UI` — WPF/MVVM presentation и composition host;
- `GameTranslator.Tests` — verification project, не product module.

Overlay, capture, OCR, translation, cache и diagnostics называются функциональными подсистемами. Они не увеличивают число production assemblies: их контракты и реализации распределены по четырём слоям согласно dependency rule. Подробные границы описаны в README каждого модуля.

------------------------------------------------------------------------

# 3. Пользовательский интерфейс

## Основной UI
**WPF** (замена WinUI 3). Причины: стабильность, зрелость, поддержка прозрачности, аппаратное ускорение, простота реализации сложных overlay.

The main editor keeps profile selection and lifecycle actions in a persistent left rail. The right workspace is tabbed into `Zones & OCR`, `Translation`, `Overlay`, `Live & Diagnostics`, `OCR Packs`, and `Hotkeys & Settings`; compact profile details and Save/Reset remain shared editor context outside the tabs. There is no duplicate Profile tab. A profile name can be edited inline by double-clicking its left-rail card. Start/Stop Live remain in the Live workspace and are duplicated in the shared header for access from every tab; both surfaces bind the same commands, and automatic session-report creation is unchanged. The common OCR language-pack checklist has its own tab, so zone geometry and global runtime installation are not mixed. The zone surface retains its fixed logical coordinate space while a user can create, move and resize zones directly; its rendered size is presentation-only. The zone surface and OCR preprocessing cards wrap between one and two rows according to available width, and the selected zone's parameters are shown below without a duplicate zone list. Experimental web translators hide the unused stored-credential editor; official providers keep it available. Every manually editable parameter group exposes a compact circular `i` marker; hovering that marker shows a Russian explanation and the accepted range where one exists. Popup visibility is bound to the icon circle rather than its containing control, so moving the pointer onto the non-interactive popup cannot keep it open after the icon is left.

Первый запуск редактора показывает локальный семишаговый приветственный тур на русском языке. Каждый шаг выбирает соответствующую рабочую вкладку, прокручивает реальный target-контрол в видимую область и строит затемнение как геометрическую разность всего workspace и расширенных границ target; прозрачный вырез и контрастная рамка подсвечивают описываемую кнопку или группу функций. Карточка инструкции выбирает угол с минимальным пересечением с target. OCR-шаг явно требует выполнить `Check OCR language` после первого выбора языка, а при состоянии `Missing` — установить пакет через `Install OCR language` и повторить проверку. Тур объясняет профиль, переводчик, OCR-зону, настройки типовых облачков, overlay и live-режим, но не изменяет профиль, не запускает capture и не обращается к provider. Закрытие крестиком или завершение сохраняется под версионированным ключом local settings; повторный запуск тура доступен через постоянную кнопку `? Тур` в верхней панели и дублирующую кнопку во вкладке `Hotkeys & Settings`.

## Overlay
Отдельное WPF-окно без рамки, прозрачное, всегда поверх игры, click-through. Текущая реализация использует WPF overlay window и platform interop для click-through/capture exclusion. Direct2D или иной renderer могут обсуждаться отдельным ADR, если WPF-рендеринг перестанет удовлетворять измеренным требованиям.

------------------------------------------------------------------------

# 4. Захват экрана

Технология: Windows Graphics Capture (WGC). Поддерживаются Window Capture и Region Capture. Full Desktop Capture не используется по умолчанию.

------------------------------------------------------------------------

# 5. OCR-подсистема

Абстрактный слой `IOcrEngine` принимает `OcrRequest` и возвращает `OcrResult` с текстовыми блоками и bounding boxes. Orientation и preprocessing являются частью request/pipeline, а не отдельными обязательными методами интерфейса.

## OCR движки
- **Windows OCR** – быстрый вариант для поддерживаемого горизонтального текста.
- **Tesseract OCR** – обязательный recognizer для вертикального японского и китайского текста и каждого принятого candidate crop; также поддерживается как fallback для отсутствующих Windows OCR language packs, когда такой путь выбран пользователем.
- **GPU Paddle detector worker** – штатный provider transient candidate bounds по ADR-030. Его output не является финальным OCR text и не заменяет Windows OCR или Tesseract.

Нормальный one-shot и live маршрут: `GPU Paddle detector → bounded grouping → Tesseract crop recognition → configured translator → per-region overlay`. Failure detector/runtime приводит к видимому degraded result без автоматического legacy full-page OCR, full-frame retry, provider/cache fallback. Legacy full-page orchestration сохранён только за явным `TranslationPipelineRunOptions.LegacyFullPage` для диагностики и compatibility.

ADR-031 добавляет per-zone `ContentLayoutMode` как единый Application policy seam для candidate grouping strategy, candidate overlay layout и minimum live refresh interval. Единственный принятый режим `DialogComic` сохраняет текущую ADR-030 семантику: automatic bounded writing-system grouping, centered per-region overlay и нулевой minimum interval. Saved profile, pipeline state identity, OCR request, UI editor и live scheduler несут один и тот же mode. Historical full-page grouping fields остаются compatibility-only и не управляют candidate path.

Candidate detector также принимает сохранённый per-zone `TextCandidateDetectorPreset`. `Standard` использует штатные Paddle post-process параметры `threshold=0.30`, `boxThreshold=0.60`, `unclipRatio=1.20`. Экспериментальные `ChineseExperimental` (`boxThreshold=0.65`) и `ChineseStrictExperimental` (`0.70`) действуют только для китайских OCR language tags; для японского, английского и остальных языков они безопасно разрешаются обратно в `Standard`. Predictor и модель остаются одним persistent worker, а параметры передаются на каждый запрос без reload. Preset изменяет только detector post-processing и не меняет recognizer, grouping/stability, revision/source revalidation, cancellation/publication или provider/cache policy. Диагностика detector preset содержит только requested/effective preset, числовые thresholds, количество кандидатов и агрегаты confidence — без OCR/translation/provider text.

Live publication применяет условный `MinimumCandidateOverlayVisibleDuration` только к краткому полному выпадению доступного detector: уже показанный candidate overlay может оставаться видимым до `2 s`, но вернувшийся candidate обязан иметь тот же id и byte-exact crop source. Capture loss, detector unavailable, source mismatch и окончание интервала немедленно публикуют актуальный пустой snapshot; revision/source revalidation и единая cancellation/publication authority не ослабляются. Локальный lifecycle report сохраняет per-candidate detector confidence как ограниченное число `0..1`; это не добавляет frame pixels или новый диагностический текст.

------------------------------------------------------------------------

# 6. Определение направления текста

Подсистема Orientation Detector с режимами Auto, Horizontal, Vertical. Для японского поддерживаются Yokogaki и Tategaki, для китайского – горизонтальный и вертикальный.

------------------------------------------------------------------------

# 7. Предобработка изображения

Текущая реализация не закрепляет обязательную библиотеку обработки изображений. Поддерживаются настройки contrast, brightness, sharpness, noise reduction, threshold и scale. Adaptive threshold, deskew, inversion, morphology или внешняя библиотека допускаются после измеримого benchmark и в рамках действующего OCR seam.

------------------------------------------------------------------------

# 8. Подсистема перевода

Архитектура Translator Provider Model. `ITranslatorProvider` из Application задаёт переводческий seam. Credentialed Google, Azure и Yandex являются поддерживаемыми провайдерами; пользователь выбирает официальный provider в профиле. Credentialless `GoogleWeb`, `BingWeb` и `YandexWeb` доступны только как отдельно выбираемые диагностические провайдеры. Агрегирующий web-provider и автоматический fallback между ними не регистрируются; ни один web-provider не является release default и не заменяет official providers.

`BingWeb` ограничивает один HTTP request собственным тайм-аутом `15 s`. Первый последовательный timeout является warning, второй открывает provider-local pause на `60 s`, а HTTP `429` открывает pause сразу и учитывает более длинный корректный `Retry-After`. Успех сбрасывает timeout counter; немедленного повтора того же запроса, смены provider или скрытого fallback нет. Пока source candidate остаётся authoritative, timeout/throttle не очищает уже опубликованный overlay и не даёт старой revision новых прав на публикацию. Локальный live-report сохраняет для каждого provider failure его provider id, точный failure kind, доступный HTTP status, pause state, относительный `Retry-After`, абсолютный `NextRetryAt` и consecutive-failure count; raw provider response, credentials и frame pixels не записываются.

Для `BingWeb` live candidate path дополнительно различает время постановки cache miss перед трёхслотовым translation limiter, начало provider invocation и каждую реально отправленную HTTP-попытку. Credential-page GET и translation POST имеют отдельные attempt id, timestamps, outcome и доступный HTTP status; fast-fail во время pause явно записывается как `WasNetworkRequestSent=false`. Отменённая candidate work сохраняет уже начатые attempt diagnostics через отдельную thread-safe очередь и не получает права на overlay publication. В отчёт попадает только bounded translation input; response body, endpoint query/form, credentials и tokens не сохраняются.

------------------------------------------------------------------------

# 9. Кэш переводов

Тип: SQLite. Таблицы: Translations, LanguagePairs, Statistics. TTL по умолчанию 30 дней, поддержка ручной очистки.

Одинаковые одновременные cache misses коалесцируются по точному ключу, не сериализуя независимые тексты. Узкий `YandexWeb`-only output sanitizer исправляет только доказанные патологические циклы provider output на miss, memory hit и persistent hit; намеренно повторяющийся source и все остальные providers сохраняют прежнюю семантику.

------------------------------------------------------------------------

# 10. Профили игр

Формат JSON (System.Text.Json). Структура: Profile, OCRZones, OverlaySettings, OCRSettings, TranslatorSettings, Hotkeys. Версионирование через `schemaVersion`. Миграции при изменении схемы.

### Пример профиля (v1.0) – см. отдельную спецификацию.

------------------------------------------------------------------------

# 11. Хранилище секретов

Основное: Windows Credential Manager. Резерв: DPAPI. Запрещено хранить ключи в JSON, INI, XML, SQLite.

------------------------------------------------------------------------

# 12. Overlay Engine

Отдельная логическая подсистема со слоями: OCR Layer, Mask Layer (Solid/Darken), Translation Layer, Debug Layer. Её contracts/positioning находятся в Application, а WPF rendering — в UI; отдельной пятой production assembly нет. Возможности: прозрачность, обводка, тени, автоперенос, масштабирование.

------------------------------------------------------------------------

# 13. Система маскирования

Методы: Solid (сплошная заливка) или Dark Overlay (затемнение). Настройки: цвет, Opacity, Padding, Corner Radius. Blur не является текущим MVP-режимом; AI/content-aware реконструкция игрового изображения запрещена.

------------------------------------------------------------------------

# 14. Горячие клавиши

Библиотека: Windows API (RegisterHotKey).

------------------------------------------------------------------------

# 15. Система профилей

Profile Manager: создание, редактирование, копирование, удаление, экспорт, импорт, валидация.

------------------------------------------------------------------------

# 16. Отладочная подсистема

Debug Overlay отображает OCR зоны, bounding boxes, координаты, исходный текст, перевод, метрики (OCR Time, Translation Time, Render Time, FPS, CPU, RAM, Cache Hit Rate).

------------------------------------------------------------------------

# 17. Система логирования

Библиотека: Serilog. Локальный application log принимает Debug/Info/Warning/Error события, подавляет шум Microsoft ниже Warning, ротируется ежедневно и хранит до 14 файлов. API-ключи и токены не логируются.

Отдельный автоматический live-session report является локальной диагностикой, а не Serilog/upload-каналом. По явному owner-решению он может сохранять bounded ordered OCR, translation-input и итоговый translated text: не более `16` entries каждого типа и `512` UTF-16 code units на entry, с однострочной нормализацией. Общий UTF-8 report ограничен `99,000,000` bytes с сохранением header и newest tail. Credentials, raw HTTP/provider response и frame pixels исключены; автоматической отправки отчётов нет.

------------------------------------------------------------------------

# 18. Локальные данные

SQLite используется для persistent translation cache и его статистики. Профили и UI settings сохраняются отдельными JSON-файлами под **%LOCALAPPDATA%\GameTranslator**; API-ключи находятся только в Windows Credential Manager.

------------------------------------------------------------------------

# 19. Потоки выполнения

UI остаётся на UI thread. Capture, OCR, translation и cache выполняются асинхронно вне UI thread с cancellation и измерением latency; конкретное число закреплённых потоков не является архитектурным контрактом.

По ADR-030 pipeline работает с transient candidate regions внутри настроенной OCR-зоны. После стабилизации региона его OCR, cache lookup, translation и overlay delivery являются независимой cancellable цепочкой: готовый overlay отображается прогрессивно и не ждёт соседние регионы. Смена или исчезновение региона отменяет только его цепочку; общая отмена кадра не должна скрывать уже готовые неизменённые регионы. GPU candidate detector является default Infrastructure-адаптером за Application seam и может передавать только candidate bounds; Windows OCR и Tesseract остаются обязательными OCR-движками.

Native Tesseract crop recognition выполняется через process-wide bounded executor на три concurrent slots с отдельным disposable engine для каждого request. Завершение candidate work сигнализирует отдельную сериализованную collection/publication operation, поэтому revision-valid overlay может появиться до следующего detector poll. Reconciliation не создаёт неиспользуемую полную копию zone-frame fingerprint; byte-exact candidate crop identity остаётся source authority. Grouping confirmation сочетает требуемое число одинаковых наблюдений с minimum wall-clock duration, а geometry jitter сохраняет identity только при one-to-one match с прежним member count, IoU не ниже `0.95` и отклонением каждой внешней границы не более `4 px` от discovery anchor.

Вертикальный CJK post-filter проверяет широкую многоколоночную группу по сохранённым исходным detector-column bounds: дополнительный путь допускается только для двух и более полностью содержащихся вертикальных members. Одиночный широкий candidate, группа с горизонтальным member и non-vertical-CJK semantics сохраняют прежнюю проверку; границы и разделение баблов по-прежнему определяет bounded writing-system grouping до OCR.

------------------------------------------------------------------------

# 20. Архитектурный стиль

MVVM + Clean Architecture. Слои: Presentation (WPF), Application, Domain, Infrastructure.

------------------------------------------------------------------------

# 21. Производительность (пересмотренные цели)

- OCR цикл: ≤ 200 мс (MVP), цель ≤ 100 мс для будущих версий.
- Overlay: ≤ 16 мс.
- Потребление памяти: до 500 МБ.
- CPU: средний ≤ 25%, пики до 50% на CPU 6 ядер / 12 потоков (2020+).

------------------------------------------------------------------------

# 22. Автообновление

В MVP включено автообновление через Squirrel.Windows или SingleFilePublisher. Проверка обновлений при старте и по кнопке в интерфейсе.

------------------------------------------------------------------------

# 23. Будущие расширения

Новые OCR-движки, переводчики, языки, поддержка нескольких мониторов, локальные LLM-переводчики, база готовых профилей игр.

------------------------------------------------------------------------

# Итоговый рекомендуемый стек (обновлён)

- Язык: C# (.NET 9)
- UI: WPF
- Overlay: WPF overlay window; другой renderer только по измеренному ADR
- Захват экрана: Windows Graphics Capture
- OCR: GPU Paddle candidate detector + bounded grouping + Tesseract crop recognition; Windows OCR и Tesseract обязательны
- Обработка изображения: текущий preprocessing pipeline; библиотека не закреплена
- Перевод: Google + Azure + Яндекс
- Кэш: SQLite (TTL 30 дней)
- Профили: JSON (версия 1.0)
- Секреты: Windows Credential Manager
- Логи: Serilog
- Архитектура: MVVM + Clean Architecture
- Автообновление: Squirrel.Windows
