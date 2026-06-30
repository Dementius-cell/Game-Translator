# Архитектурный документ

## Проект

Система экранного OCR-перевода игровых субтитров и текста для Windows 11

Версия документа: 1.1 (обновлено)

------------------------------------------------------------------------

# 1. Архитектурные цели

Высокая скорость работы, минимальная нагрузка на CPU, работа поверх игр, поддержка нескольких OCR-зон, азиатских языков, расширяемость.

------------------------------------------------------------------------

# 2. Выбор языка программирования

Основной язык: C# (.NET 9). Причины: лучший доступ к Windows API, производительность, безопасность, удобство поддержки. Python не используется как основной (допустим только для прототипов).

------------------------------------------------------------------------

# 3. Пользовательский интерфейс

## Основной UI
**WPF** (замена WinUI 3). Причины: стабильность, зрелость, поддержка прозрачности, аппаратное ускорение, простота реализации сложных overlay.

## Overlay
Отдельное окно без рамки, прозрачное, всегда поверх игры, click-through. Реализация: **SharpDX + SwapChainPanel** или Direct2D через WPF. Прозрачность и эффекты через Composition API.

------------------------------------------------------------------------

# 4. Захват экрана

Технология: Windows Graphics Capture (WGC). Поддерживаются Window Capture и Region Capture. Full Desktop Capture не используется по умолчанию.

------------------------------------------------------------------------

# 5. OCR-подсистема

Абстрактный слой `IOcrEngine` с методами DetectText(), DetectTextBlocks(), DetectTextOrientation(), GetBoundingBoxes().

## OCR движки
- **Windows OCR** – основной для латиницы и горизонтальных текстов (кроме японского/китайского).
- **Tesseract OCR** – резервный, обязателен для вертикального текста (японский, китайский).
- **PaddleOCR** – экспериментальный модуль (будущее).

------------------------------------------------------------------------

# 6. Определение направления текста

Подсистема Orientation Detector с режимами Auto, Horizontal, Vertical. Для японского поддерживаются Yokogaki и Tategaki, для китайского – горизонтальный и вертикальный.

------------------------------------------------------------------------

# 7. Предобработка изображения

Библиотека: OpenCvSharp. Операции: Resize, Sharpen, Denoise, Contrast, Brightness, Threshold, Adaptive Threshold, Morphology.

------------------------------------------------------------------------

# 8. Подсистема перевода

Архитектура Translator Provider Model. Интерфейс `ITranslatorProvider` с методами Translate(), DetectLanguage(), ValidateCredentials(). Провайдеры: Google, Azure, Яндекс. Автоматическое переключение между провайдерами не требуется – пользователь выбирает один на профиль.

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

Методы: Solid (сплошная заливка) или Dark Overlay (затемнение). Настройки: цвет, Opacity, Padding, Corner Radius. Размытие не используется.

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

Thread 1: UI, Thread 2: Screen Capture, Thread 3: OCR Processing, Thread 4: Translation, Thread 5: Overlay Rendering.

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
- Overlay: SharpDX + SwapChainPanel (или Direct2D)
- Захват экрана: Windows Graphics Capture
- OCR: Windows OCR + Tesseract
- Обработка изображения: OpenCvSharp
- Перевод: Google + Azure + Яндекс
- Кэш: SQLite (TTL 30 дней)
- Профили: JSON (версия 1.0)
- Секреты: Windows Credential Manager
- Логи: Serilog
- Архитектура: MVVM + Clean Architecture
- Автообновление: Squirrel.Windows
------------------------------------------------------------------------

# Sprint 26 Architecture Addendum: Translation Grouping and Overlay Geometry

This addendum is current as of 2026-06-30.

The active runtime pipeline is:

```text
Capture -> OCR -> Text grouping -> Cache/Translation -> Overlay snapshot -> WPF overlay
```

Responsibilities:

- OCR engines produce raw `OcrResult` blocks in frame-relative coordinates.
- `TranslationTextGroupingService` separates translation source geometry from mask source geometry.
- Translation/cache operate on `TranslationSourceResult.TextBlocks`.
- Overlay text items are positioned from translated semantic blocks.
- Overlay mask items are positioned from accepted raw source blocks.
- UI diagnostics export the raw OCR, grouped translation source, mask source, overlay geometry, timings, and cache/provider status without secrets.

Layering remains unchanged:

- `Domain` does not depend on Application, Infrastructure, or UI.
- `Application` owns grouping, pipeline orchestration, and overlay positioning services.
- `Infrastructure` owns concrete OCR/translator/cache/credential implementations.
- `UI` consumes Application services and must not directly reference Infrastructure.

For the detailed vertical CJK placement contract, see `docs/design/vertical-cjk-overlay-placement.md`.
