# Архитектурный документ

## Проект

Система экранного OCR-перевода игровых субтитров и текста для Windows 11

Версия документа: 1.1 (обновлено)

------------------------------------------------------------------------

# 1. Архитектурные цели

Высокая скорость работы, минимальная нагрузка на CPU, работа поверх игр, поддержка нескольких OCR-зон, азиатских языков, расширяемость.

------------------------------------------------------------------------

# 2. Выбор языка программирования

Основной язык приложения: C# (.NET 9). Он отвечает за Windows API, WPF UI, capture, orchestration, перевод и overlay. Python не является вторым приложением или UI-слоем: по ADR-030 он поставляется как зафиксированный runtime узкого GPU Paddle detector worker. Его версии, модель и хеши воспроизводимо собираются через `tools/bootstrap-paddle-runtime.ps1`; recognizer остаётся в .NET/Tesseract пути.

------------------------------------------------------------------------

# 3. Пользовательский интерфейс

## Основной UI
**WPF** (замена WinUI 3). Причины: стабильность, зрелость, поддержка прозрачности, аппаратное ускорение, простота реализации сложных overlay.

The main editor keeps profile selection and lifecycle actions in a persistent left rail. The right workspace is tabbed into `Zones & OCR`, `Translation`, `Overlay`, `Live & Diagnostics`, `OCR Packs`, and `Hotkeys & Settings`; compact profile details and Save/Reset remain shared editor context outside the tabs. There is no duplicate Profile tab. A profile name can be edited inline by double-clicking its left-rail card. Start/Stop Live remain in the Live workspace and are duplicated in the shared header for access from every tab; both surfaces bind the same commands, and automatic session-report creation is unchanged. The common OCR language-pack checklist has its own tab, so zone geometry and global runtime installation are not mixed. The zone surface retains its fixed logical coordinate space while a user can create, move and resize zones directly; its rendered size is presentation-only. The zone surface and OCR preprocessing cards wrap between one and two rows according to available width, and the selected zone's parameters are shown below without a duplicate zone list. Experimental web translators hide the unused stored-credential editor; official providers keep it available.

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

------------------------------------------------------------------------

# 6. Определение направления текста

Подсистема Orientation Detector с режимами Auto, Horizontal, Vertical. Для японского поддерживаются Yokogaki и Tategaki, для китайского – горизонтальный и вертикальный.

------------------------------------------------------------------------

# 7. Предобработка изображения

Текущая реализация не закрепляет обязательную библиотеку обработки изображений. Поддерживаются настройки contrast, brightness, sharpness, noise reduction, threshold и scale. Adaptive threshold, deskew, inversion, morphology или внешняя библиотека допускаются после измеримого benchmark и в рамках действующего OCR seam.

------------------------------------------------------------------------

# 8. Подсистема перевода

Архитектура Translator Provider Model. `ITranslatorProvider` из Application задаёт переводческий seam. Credentialed Google, Azure и Yandex являются поддерживаемыми провайдерами; пользователь выбирает официальный provider в профиле. Experimental WebAuto может выполнять диагностический fallback между web-провайдерами, но не является release default и не заменяет official providers.

------------------------------------------------------------------------

# 9. Кэш переводов

Тип: SQLite. Таблицы: Translations, LanguagePairs, Statistics. TTL по умолчанию 30 дней, поддержка ручной очистки.

------------------------------------------------------------------------

# 10. Профили игр

Формат JSON (System.Text.Json). Структура: Profile, OCRZones, OverlaySettings, OCRSettings, TranslatorSettings, Hotkeys. Версионирование через `schemaVersion`. Миграции при изменении схемы.

### Пример профиля (v1.0) – см. отдельную спецификацию.

------------------------------------------------------------------------

# 11. Хранилище секретов

Основное: Windows Credential Manager. Резерв: DPAPI. Запрещено хранить ключи в JSON, INI, XML, SQLite.

------------------------------------------------------------------------

# 12. Overlay Engine

Отдельный модуль со слоями: OCR Layer, Mask Layer (Solid/Darken), Translation Layer, Debug Layer. Возможности: прозрачность, обводка, тени, автоперенос, масштабирование.

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

Библиотека: Serilog. Режимы: Error, Warning, Info, Debug, Trace. Автоматическая ротация логов. Логируются только ошибки и предупреждения (без API-ключей).

------------------------------------------------------------------------

# 18. База данных

SQLite для кэша переводов, статистики, настроек приложения. Не используется для API ключей.

------------------------------------------------------------------------

# 19. Потоки выполнения

UI остаётся на UI thread. Capture, OCR, translation и cache выполняются асинхронно вне UI thread с cancellation и измерением latency; конкретное число закреплённых потоков не является архитектурным контрактом.

По ADR-030 pipeline работает с transient candidate regions внутри настроенной OCR-зоны. После стабилизации региона его OCR, cache lookup, translation и overlay delivery являются независимой cancellable цепочкой: готовый overlay отображается прогрессивно и не ждёт соседние регионы. Смена или исчезновение региона отменяет только его цепочку; общая отмена кадра не должна скрывать уже готовые неизменённые регионы. GPU candidate detector является default Infrastructure-адаптером за Application seam и может передавать только candidate bounds; Windows OCR и Tesseract остаются обязательными OCR-движками.

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
