# GameTranslator.Domain

Developer README для чистого доменного слоя. Модуль содержит данные и инварианты продукта и не знает о WPF, Windows API, файловой системе, OCR/translation SDK, логировании или DI.

## Ответственность

- `Profiles/`: `GameProfile`, OCR-зоны, абсолютная/относительная геометрия, OCR/overlay/grouping settings, `ContentLayoutMode` и detector preset.
- `ProfileValidator`: чистые правила валидации и стабильные коды ошибок.
- Совместимая схема профиля `1.0`; отсутствующие additive-поля получают безопасные defaults.

## Граница зависимостей

`Domain` не имеет project references. На него могут ссылаться `Application`, `Infrastructure` и тесты; обратные зависимости запрещены. IO, JSON, миграции, репозитории и platform-specific logic принадлежат другим слоям.

## Как изменять

Изменение формы профиля требует проверки старого JSON, import/export, миграции и валидации. Геометрия должна оставаться явной и поддерживать несколько независимых зон. Breaking schema change или потеря совместимости требует отдельного решения владельца.

## Проверка

Запускайте focused domain/profile tests и architecture dependency tests; при изменении профиля — также migration и import/export regressions. Общие правила находятся в [AGENTS.md](AGENTS.md) и [архитектуре](../../docs/02-architecture.md).
