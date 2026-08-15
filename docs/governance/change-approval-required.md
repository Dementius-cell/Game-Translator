# Change Approval Required

Проект: Game Translator

Версия: 2.0

Статус: ACTIVE

## Назначение

Этот документ разделяет опасные изменения, решения владельца и обычную поставку. Его цель - защищать безопасность, данные и архитектуру без блокировки исправлений внутри уже принятого scope.

Если этот документ расходится с Constitution, Constitution определяет hard-инвариант. Если решение уже зафиксировано в ACCEPTED ADR, ADR определяет допустимый scope реализации.

## Три уровня изменений

### Hard Stop

Без явного одобрения владельца запрещены:

- взаимодействие с памятью игры, injection, hooks, drivers или обход античитов;
- хранение или экспорт секретов вне защищённого хранилища;
- удаление совместимости профилей, schemaVersion или миграций;
- направление зависимостей, при котором Domain зависит от UI/Infrastructure или UI напрямую зависит от Infrastructure;
- разрушительная замена платформы, слоя или persistence-формата.

Агент останавливается, создаёт Change Request и ждёт решения владельца.

### Decision Record

Issue, Change Request и ADR с одним решением владельца обязательны для изменения продуктовой семантики или большой площади воздействия. После принятия ADR его scope является разрешением на нормальную реализацию, тесты и исправления внутри описанных границ. Новое одобрение требуется только при выходе за scope.

### Normal Delivery

Bug fix, тест, additive-compatible изменение, внутренняя реализация, пакетное обновление без смены платформы и рефакторинг внутри принятого ADR выполняются через issue, impact-based tests и review. Отдельное согласование владельца не требуется.

## Change Request Format

Для Hard Stop и Decision Record используйте:

```text
CHANGE REQUEST

Причина:
Текущее решение:
Предлагаемое решение:
Преимущества:
Недостатки и риски:
Влияние на архитектуру:
Влияние на производительность:
Влияние на совместимость и миграцию:
Применимые Quality Gates:
Ожидается решение владельца проекта.
```

## Категории

### A. Архитектура

Decision Record обязателен для A-001 смены основного языка, A-002 смены архитектуры, A-003 структуры слоёв и A-005 структуры solution.

A-004 требует Decision Record только при изменении composition root, lifetime policy или правил DI между слоями. Регистрация обычной реализации, новый constructor dependency и тестовый fake являются Normal Delivery.

### B. Базовые технологии

Decision Record обязателен для замены WPF, Windows Graphics Capture, SQLite, Serilog, Windows Credential Manager или формата профилей. Patch/minor обновления и совместимые вспомогательные библиотеки не являются заменой технологии, но требуют обычной проверки лицензии, сборки и relevant tests.

### C. OCR

Hard Stop: удаление Windows OCR, Tesseract OCR или переход продукта на один OCR-движок.

Decision Record: ломающая смена публичного `IOcrEngine` контракта или новая семантика вертикального текста вне ACCEPTED ADR.

Normal Delivery: новая реализация за существующим seam, OCR bug fix, benchmark, тест, preprocessing tuning и реализация уже принятого ADR. По ADR-030 GPU Paddle detector допускается как штатный transient candidate provider, но не как recognizer и не как замена обязательных Windows OCR/Tesseract. Новый recognizer или изменение default/fallback вне scope принятого ADR требует Decision Record.

### D. Переводчики

Hard Stop: удаление обязательного credentialed Google, Azure или Yandex без согласованной замены.

Decision Record: ломающая смена `ITranslatorProvider` или модели подключения.

Normal Delivery: provider bug fix, диагностика, тесты, additive provider и experimental web smoke внутри существующего seam. Секреты остаются под правилами E.

### E. Секреты и безопасность

Hard Stop без исключений: изменение модели хранения секретов, открытое хранение в JSON/SQLite/XML/INI/логах, ослабление redaction.

### F. Взаимодействие с играми

Hard Stop без исключений: DLL/memory injection, memory reading/writing, process hooks, drivers, kernel code, античит bypass или получение текста из памяти игры. Разрешены только screen capture и OCR.

### G. Overlay

Decision Record: перенос ответственности Overlay в OCR/translator или удаление mask/translation/debug ответственности.

Normal Delivery: positioning, rendering, styling, collision fixes и реализация принятого overlay ADR без нарушения границ слоёв.

### H. Кэш

Decision Record: смена архитектуры memory + SQLite, default TTL или default cache-bypass поведения.

Normal Delivery: bug fix, очистка, статистика, тесты, observability и cache bypass, который явно инициирован пользователем/диагностикой и не становится default.

### I. Профили

Hard Stop: удаление schemaVersion, миграций или поддержки существующих профилей.

Decision Record: breaking schema change или persistence-format replacement.

Normal Delivery: additive-compatible поле с migration/compatibility test, validation fix и UI для существующих настроек.

### J. Производительность

Decision Record: изменение целевых KPI.

Measured regression больше 10% требует issue с baseline, причиной и решением, но не блокирует исследование или исправление до измерения. Производительные gates выбираются только для затронутого hot path.

### K. Roadmap

GitHub Issues являются источником актуальных зависимостей. Строгая нумерация roadmap не блокирует независимую подготовительную работу. Нельзя начинать задачу, если её собственные зависимости не закрыты или не отложены с явным решением владельца.

### L. Документация и ADR

Constitution и этот документ меняются только с одобрением владельца. ACCEPTED ADR не переписывается для смены решения: новый ADR должен пометить прежний как `SUPERSEDED` и сохранить историю. Исправление опечатки или ссылки без изменения решения является Normal Delivery.

## Quality Gates

Для каждой задачи применяются базовые gates: build, relevant tests, архитектурные границы и секреты. Дополнительно выбираются gates по impact:

- OCR: качество, bounds, language/orientation, latency;
- Overlay: positioning, mask, render, click-through, visual evidence;
- Profile: migration, import/export, validation;
- Translator/cache: provider failure, cache, TTL, redaction;
- Release: все применимые QG1-QG18.

Неприменимый gate фиксируется в issue или PR одной строкой с причиной. Его нельзя молча пропускать.

## Финальное правило

При сомнении сначала классифицируйте изменение по этому документу и приведите evidence. Сомнение само по себе не превращает normal bug fix в Hard Stop; расширение scope без evidence - превращает.
