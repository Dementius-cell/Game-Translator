# ARCHITECTURE_DECISION_RECORDS.md

# Architecture Decision Records (ADR)

Проект: Game Translator

Версия: 1.0

Статус: Active

---

# Назначение документа

Данный документ фиксирует архитектурные решения проекта.

Каждое решение содержит:

* контекст;
* проблему;
* принятое решение;
* последствия;
* статус.

AI-агент обязан проверять ADR перед предложением архитектурных изменений.

Если решение помечено как ACCEPTED, менять его запрещено без явного согласования владельца проекта.

---

# ADR-001

## Использование C# как основного языка

Статус:
ACCEPTED

Дата:
2026-06-07

### Контекст

Проект ориентирован исключительно на Windows 11.

Требуется:

* работа с Windows API;
* overlay поверх игр;
* низкие задержки;
* долгосрочная поддержка.

### Решение

Использовать C# и .NET 9.

### Причины

* нативная интеграция с Windows;
* высокая производительность;
* удобная работа с WPF;
* развитая экосистема.

### Последствия

Python допускается только для исследований и прототипов.

Основной продукт всегда реализуется на C#.

---

# ADR-002

## Использование WPF

Статус:
ACCEPTED

Дата:
2026-06-07

### Контекст

Требуется стабильный desktop UI.

### Решение

Использовать WPF.

### Причины

* зрелость технологии;
* стабильность;
* совместимость с overlay;
* большое количество библиотек.

### Запрещено заменять на

* WinUI 3
* Avalonia
* MAUI
* Electron

без отдельного согласования.

---

# ADR-003

## Clean Architecture + MVVM

Статус:
ACCEPTED

### Решение

Использовать:

* Clean Architecture
* MVVM

### Причины

* независимость модулей;
* простота тестирования;
* расширяемость.

### Последствия

Нельзя создавать прямые зависимости между UI и Infrastructure.

---

# ADR-004

## Захват экрана через Windows Graphics Capture

Статус:
ACCEPTED

### Решение

Использовать Windows Graphics Capture.

### Причины

* официальное API Microsoft;
* высокая производительность;
* поддержка оконного режима;
* поддержка borderless fullscreen.

### Не использовать

* GDI Capture
* BitBlt
* DLL Hooking
* DirectX Injection

---

# ADR-005

## Двухдвижковая OCR архитектура

Статус:
ACCEPTED

### Решение

Использовать:

1. Windows OCR
2. Tesseract OCR

### Причины

Windows OCR:

* быстрее;
* хорошо работает с горизонтальным текстом.

Tesseract:

* поддерживает вертикальный текст;
* поддерживает японский и китайский.

### Последствия

Система не должна зависеть только от одного OCR движка.

---

# ADR-006

## Вертикальный японский и китайский текст

Статус:
ACCEPTED

### Решение

Использовать исключительно Tesseract OCR.

### Причины

Windows OCR нестабилен для вертикального текста.

### Последствия

Любые предложения использовать Windows OCR для вертикального текста отклоняются.

---

# ADR-007

## Архитектура переводчиков

Статус:
ACCEPTED

### Решение

Использовать Provider Pattern.

Интерфейс:

ITranslatorProvider

### Поддерживаемые провайдеры

* Google
* Azure
* Yandex

### Последствия

Добавление нового переводчика не должно требовать изменения ядра.

---

# ADR-008

## SQLite для кэша переводов

Статус:
ACCEPTED

### Решение

Использовать SQLite.

### Причины

* отсутствие сервера;
* надёжность;
* простота резервирования;
* высокая скорость.

### Не использовать

* PostgreSQL
* MySQL
* MongoDB

для MVP.

---

# ADR-009

## Хранение API-ключей

Статус:
ACCEPTED

### Решение

Использовать Windows Credential Manager.

Резерв:

DPAPI.

### Запрещено

* JSON
* SQLite
* XML
* INI
* Логи

### Причина

Предотвращение утечки ключей.

---

# ADR-010

## Overlay как отдельный модуль

Статус:
ACCEPTED

### Решение

Overlay выделяется в отдельную подсистему.

### Причины

* независимость рендеринга;
* упрощение поддержки;
* масштабируемость.

### Последствия

OCR и переводчики не должны содержать код отображения.

---

# ADR-011

## Многозонная архитектура

Статус:
ACCEPTED

### Решение

Все компоненты проектируются под множество OCR-зон.

### Причины

Однозонная архитектура потребует переписывания системы.

### Последствия

Новые функции обязаны работать с коллекцией зон.

---

# ADR-012

## Запрет внедрения в игровые процессы

Статус:
ACCEPTED

### Решение

Приложение не взаимодействует с памятью игры.

### Запрещено

* DLL Injection
* Memory Reading
* Memory Injection
* Process Hooking
* Driver Injection

### Причины

* совместимость;
* безопасность;
* отсутствие конфликтов с античитами.

### Последствия

Получение текста возможно только через Screen Capture + OCR.

---

# ADR-013

## Архитектура кэша

Статус:
ACCEPTED

### Решение

Двухуровневый кэш:

Level 1:
Memory Cache

Level 2:
SQLite Cache

TTL:
30 дней

### Причины

Снижение нагрузки на переводчики.

### Последствия

Перед вызовом API всегда выполняется проверка кэша.

---

# ADR-014

## Формат профилей

Статус:
ACCEPTED

### Решение

Использовать JSON.

Обязательное поле:

schemaVersion

### Причины

* простота обмена;
* читаемость;
* миграции версий.

### Последствия

Любое изменение структуры требует механизма миграции.

---

# ADR-015

## Порядок реализации проекта

Статус:
ACCEPTED

### Решение

Разработка выполняется строго по Roadmap.

### Последствия

Запрещено начинать:

* вертикальный текст;
* Tesseract интеграцию;
* оптимизацию;

до появления рабочей цепочки:

Capture
→ OCR
→ Translate
→ Overlay

---

# ADR-016

## Vertical OCR Translation Overlay Layout

Status:
ACCEPTED

Date:
2026-07-04

### Context

Vertical Japanese and Chinese OCR blocks are usually narrow columns. Russian translations are often much longer than the original vertical text. The current fixed-bounds or center-expanded horizontal layout can either make the translated text unreadable or let it grow into a long horizontal strip that overlaps neighboring columns and comic bubbles.

Real no-UI GoogleWeb overlay smoke runs on 2026-07-04 showed this clearly on Japanese two-column and three-column vertical fixtures. OCR grouping was correct, but the translated overlay needed a vertical-source-specific placement policy.

### Options

1. Keep strict source bounds and reduce font size until the text fits.
2. Let translated text expand freely from the source center.
3. Use a vertical-source wrap box: start with a controlled width based on the original column width, wrap translated text inside it, then grow vertically around the original block center before reducing font size.
4. Use a bubble-wide comic layout solver that places translations inside the whole detected speech bubble or group.
5. Use caption mode outside the original text area.

### Decision

Use option 3 as the default vertical OCR translation layout.

For vertical OCR sources:

* keep mask bounds tied to the original OCR semantic bounds;
* separate mask bounds from translation text bounds;
* initialize translation text width from the source column width, defaulting to approximately two source widths;
* clamp translation width between a minimum readable width and the available OCR zone bounds;
* wrap translated text inside that box;
* grow the translation box upward and downward around the original source center before reducing font size;
* reduce font size only after width and vertical growth limits are reached;
* if text still cannot fit, keep deterministic clipping or ellipsis and surface the case as a debug/quality warning.

Comic bubble-wide placement remains a future enhancement, not the default. It may be implemented later as a higher-level layout solver for `NearbyBlocks` groups.

### Reasons

* It keeps original text masking precise and conservative.
* It avoids unreadably tiny text for long Russian translations.
* It prevents translated vertical text from becoming unbounded horizontal strips.
* It preserves the existing overlay layering model: OCR layer, mask layer, translation layer, debug layer.
* It can be implemented inside the overlay positioning service without changing OCR or translator interfaces.
* It is backward-compatible with existing profiles because it refines layout behavior for vertical sources instead of changing profile schema.

### Consequences

Positive:

* Vertical CJK translations become more readable by default.
* Mask placement remains stable and does not over-cover neighboring UI.
* Translation text can use nearby whitespace without pretending that the mask area is also the text layout area.

Negative:

* Translation text bounds may be wider and taller than the original OCR block.
* Dense comic pages may still need collision avoidance between neighboring translated blocks.
* Very long translations can still require font reduction or quality warnings.

Compatibility:

* No profile migration is required.
* Existing horizontal text behavior is unchanged.
* Existing overlay debug output should show both mask bounds and translation text bounds when they differ.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-07-04.

---

# ADR TEMPLATE

Использовать для новых решений.

---

# ADR-XXX

Название решения

Статус:
PROPOSED | ACCEPTED | REJECTED | SUPERSEDED

Дата:
YYYY-MM-DD

## Контекст

Описание проблемы.

## Варианты

Вариант 1

Вариант 2

Вариант 3

## Решение

Выбранное решение.

## Причины

Почему оно выбрано.

## Последствия

Плюсы.

Минусы.

Влияние на совместимость.

## Требуется миграция

Да / Нет

## Одобрено

Имя владельца проекта
