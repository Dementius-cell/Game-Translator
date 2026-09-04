# План реализации проекта

## Roadmap + Sprint Plan

Проект: Система экранного OCR-перевода игровых субтитров и текста для Windows 11

Версия документа: 1.4 (обновлено 2026-09-04)

------------------------------------------------------------------------

# 1. Цели Roadmap

Получать работающий результат после каждого этапа, минимизировать переработки, сначала фундаментальные компоненты.

------------------------------------------------------------------------

# 2. Общая стратегия разработки

Этапы ниже сохраняют историческую карту развития и набор целевых возможностей. Текущий порядок работы определяется графом зависимостей GitHub Issues: задачу можно начать, когда выполнены её явные зависимости или владелец проекта явно разрешил параллельную подготовку.

Каждый завершённый этап должен оставлять рабочую сборку. Номер этапа сам по себе не является запретом на исправление дефекта, тест, документацию или совместимое улучшение уже существующей возможности. Жёсткие запреты и изменения, требующие решения, определяются `docs/governance/change-approval-required.md`.

## Текущий выпускной контур (ADR-030)

Штатный путь продукта — GPU Paddle detector → bounded grouping → Tesseract crop recognition → configured translator → per-region overlay. Его runtime воспроизводится через зафиксированные CPython/Paddle/model/Tesseract lock-файлы и bootstrap-скрипты. Для релизной вехи обязательны применимые quality gates: отсутствие скрытого legacy/provider/cache fallback, проверка runtime/model hashes, пакетная целостность, offline-install/recovery/rollback и разрешённая WPF-проверка. Same-host clean-root является только same-host evidence, не физическим clean host.

Глобальный writing-system baseline, per-zone `ContentLayoutMode`, CJK horizontal/vertical rules и подтверждённое Thai-исключение реализованы. Открыты owner-smoke для LTR-профиля (#49), отдельные Brahmic/Indic и RTL-профили (#53-#55), human validation диагностики (#34), calibration workflow (#35) и Release 1.0 (#30). Новый source-equivalent portable/RC ещё не собран; подпись и публикация GitHub Release требуют отдельного решения владельца.

------------------------------------------------------------------------

# 3. Этап 0. Подготовка проекта (1 спринт)

Создать репозиторий, структуру решения (Clean Architecture, MVVM), настроить DI, Serilog, систему сборки. Результат: пустое приложение WPF запускается.

------------------------------------------------------------------------

# 4. Этап 1. Профили игр (1-2 спринта)

Создание, редактирование, удаление, клонирование, экспорт/импорт JSON, валидация (пересечение зон, координаты). Результат: полностью рабочий менеджер профилей.

------------------------------------------------------------------------

# 5. Этап 2. Захват экрана (1 спринт)

Выбор зоны мышью (аналог «Ножниц»), сохранение зоны, захват области, предпросмотр, обновление 30+ FPS.

------------------------------------------------------------------------

# 6. Этап 3. OCR MVP (2 спринта)

Windows OCR, получение текста, координат, bounding box. Отображение распознанного текста.

------------------------------------------------------------------------

# 7. Этап 4. Overlay MVP (2 спринта)

Прозрачное WPF-окно, click-through, отображение текста, привязка к координатам OCR.

------------------------------------------------------------------------

# 8. Этап 5. Переводчики (2 спринта)

Google, Azure, Яндекс. Проверка ключей, обработка ошибок (сеть, недоступность).

------------------------------------------------------------------------

# 9. Этап 6. Полная цепочка (1 спринт)

Capture → OCR → Translate → Overlay. Первый полный рабочий цикл.

------------------------------------------------------------------------

# 10. Этап 7. Замена оригинального текста (1 спринт)

Маска Solid/Darken (без blur). Настройки: цвет, непрозрачность, padding. Оригинал не читается.

------------------------------------------------------------------------

# 11. Этап 8. Кэш переводов (1 спринт)

Memory Cache + SQLite. TTL 30 дней, ручная очистка. Статистика попаданий.

------------------------------------------------------------------------

# 12. Этап 9. Настраиваемый OCR (2 спринта)

Контраст, яркость, шумоподавление, бинаризация, резкость, масштабирование.

------------------------------------------------------------------------

# 13. Этап 10. Многозонный OCR (2 спринта)

Одновременная работа нескольких зон, независимые настройки и результаты.

------------------------------------------------------------------------

# 14. Этап 11. Горячие клавиши (1 спринт)

Старт/Пауза, скрыть overlay, настройки, выход. Глобальные хоткеи.

------------------------------------------------------------------------

# 15. Этап 12. Режим отладки (1 спринт)

Отображение зон, bounding boxes, координат, таймингов, статистики.

------------------------------------------------------------------------

# 16. Этап 13. Поддержка Tesseract (2 спринта)

Добавление TesseractOcrEngine, переключение OCR, автовыбор. Tesseract является обязательным движком и режимом по умолчанию для вертикального текста.

------------------------------------------------------------------------

# 17. Этап 14. Вертикальный текст (3 спринта)

Поддержка японского и китайского (Horizontal, Vertical, Auto). Tesseract используется по умолчанию; альтернативный экранный движок допускается только после измеримого сравнения и ADR, при сохранении Windows OCR и Tesseract.

------------------------------------------------------------------------

# 18. Этап 15. Оптимизация производительности (2 спринта)

Цели этапа: уменьшить задержку от появления стабильного исходного текста до публикации переведённого overlay, сохранив качество r29, revision/source revalidation и штатный путь ADR-030. Целевые ресурсные ориентиры сохраняются: CPU ≤25% средняя, RAM ≤500 МБ; GPU/runtime оцениваются отдельно на принятом RTX 3080 proxy для RTX 3060 8 GiB.

Baseline для этой очереди — десять owner live-сессий portable r29 от 2026-08-24. Во всех сессиях candidate pipeline оставался `Ready`, без restart, `CandidateWorkFailed` и потерянных lifecycle events. Прогретый detector показал P50 `118.3 ms` / P95 `151.5 ms`; один Tesseract crop OCR — P50 `43.7 ms` для английского и `61.6 ms` для японского. Основные локальные задержки создают синхронный запуск нескольких OCR-кандидатов, ожидание завершённой работы до следующего polling-цикла и повторные OCR-наблюдения стабильного crop. Provider latency остаётся внешним измерением и не должна маскироваться fallback или сменой default.

## 18.1. Очередь оптимизаций live candidate pipeline

1. **Выполнено — bounded asynchronous Tesseract execution.** CPU/native Tesseract recognition вынесен с вызывающего live/UI-потока в process-wide трёхслотовый bounded executor без изменения публичного `IOcrEngine`, OCR-модели или результата распознавания. Lifecycle timing отражает фактическое начало работы; focused/full gates пройдены.
2. **Выполнено — event-driven completion and overlay publication.** Завершённые candidate tasks будят отдельный сериализованный collection/publication path; revision/source authority, cancellation и overlay removal сохранены. Готовый overlay больше не обязан ждать следующего detector polling-cycle.
3. **Выполнено — remove redundant frame copies.** Неиспользуемая полная `FrameFingerprint`-копия zone frame удалена; byte-exact crop identity остаётся authority для source changes и cancellation. Дополнительная lazy materialization не потребовалась в принятом change set.
4. **Отложено решением владельца — byte-identical crop OCR reuse.** Возможен только после отдельного сравнения результатов и quality-теста. Наблюдение нового frame остаётся явным; approximate matching и reuse между различными revisions запрещены.
5. **Отложено решением владельца — non-blocking detector prewarm.** Требует отдельного решения о раннем занятии GPU/VRAM и сравнения startup/resource behavior; не может становиться readiness gate или вызывать provider/fallback.

## 18.2. Порядок и quality gates

- Первый безопасный пакет: пункты `1 → 2 → 3`, каждый с отдельными focused tests и повторным разбором privacy-safe lifecycle timings.
- После пакета повторить Release build, полный test suite, docs mini-check и owner live smoke на горизонтальном английском и вертикальном/горизонтальном CJK. Новый portable RC собирается только по отдельной команде владельца.
- Пункт 4 не объединять с первым пакетом: сначала доказать, что remaining delay действительно оправдывает изменение stability semantics.
- Пункт 5 можно исследовать отдельно, но не превращать в startup gate и не увеличивать product scope скрытым provider/cache/legacy fallback.
- Не менять provider default. Для сравнения network latency фиксировать provider identity, cache hit/miss и request timestamps. После owner-решения 2026-08-29 автоматический локальный live-report может сохранять bounded OCR, translation-input и финальный translated text; raw provider response, credentials и frame pixels не записываются, а приложение не выполняет upload диагностических файлов.
- После owner-решения 2026-09-02 уже опубликованный candidate overlay получает условный readability grace не более `2 s` только при полном пустом результате доступного detector. Возврат без промежуточного пустого overlay допустим лишь для того же candidate id и byte-exact crop source; capture loss, detector unavailable, несовпадение источника и истечение интервала очищают overlay немедленно. Порог detector, OCR/grouping, stability, revision/source authority, provider/cache policy и roadmap items 4-5 не меняются. Локальная lifecycle-диагностика дополнительно сохраняет числовой per-candidate confidence `0..1` без новых OCR/translation/provider text или frame pixels.
- Успех измеряется end-to-end latency, detector/OCR/provider/collection percentiles, allocation rate, CPU/RAM/GPU и отсутствием premature/stale overlays, а не только средним временем одного метода.

## 18.3. Адаптивная группировка вертикального CJK после r34

Owner live-проверка r34 подтвердила, что фиксированное число колонок является неверной границей для японской вертикальной разметки. Пять соседних колонок с общим vertical overlap `1.0` и horizontal gap `2 px` были разделены `4 + 1` исключительно из-за `MaximumVerticalGroupMembers = 4`; left-to-right seed дополнительно отделил правую начальную колонку японского текста. Это pre-OCR candidate-composition defect, а не общий split внутри translation grouping: в том же отчёте все `46/46` непустых completed works дали по одной translation-input group и одному translated block без failure.

- Изменение ограничено `WritingSystemGroupingProfile.CjkVertical`. Auto (`MaximumVerticalColumns = null`/missing) означает adaptive segmentation без fixed column count; явное значение `1..12` остаётся пользовательским hard override.
- Вертикальные CJK-кандидаты рассматриваются справа налево и только через непосредственных пространственных соседей. Merge требует локальной близости и whole-group coherence; transitive creep через цепочку слабых совпадений запрещён.
- Граница группы определяется shared vertical overlap, normalized adjacent gap/pitch, top/bottom/center alignment и заметным geometry/whitespace discontinuity, а не количеством колонок. Решение должно оставаться детерминированным и выдавать диагностируемую причину merge/cut вместо непрозрачной magic score.
- Подтверждённые r36 ragged-bottom реплики могут превысить обычный bottom-alignment порог только с третьей колонки, при shared overlap не ниже `0.95`, normalized top offset не выше `0.05` и каждом межколоночном gap не больше `2 px`; более широкий `6 px` контрпример остаётся разрывом группы. Это узкое послабление не применяется к explicit override или non-`CjkVertical` профилям.
- CJK target post-filter не применяет aspect ratio общей широкой рамки как единственный критерий к уже принятой многоколоночной группе. Если aggregate шире своей высоты, дополнительный путь требует не менее двух source members, полного вложения каждого member в aggregate и вертикального aspect ratio каждого member. Одиночный широкий candidate и группа хотя бы с одним горизонтальным member сохраняют прежний reject; grouping boundaries при этом не меняются.
- Horizontal/non-CJK profiles, Tesseract behavior, stability observations/durations, byte-exact source identity, revision/source revalidation, cancellation/publication authority, provider/cache policy и roadmap items 4-5 не меняются.
- Diagnostics сохраняют ordered OCR/group member bounds и resolved writing-system/orientation. После owner-решения 2026-08-29 локальный live-report также сохраняет bounded OCR, translation-input и финальный translated text; raw provider response, credentials и frame pixels остаются исключёнными, автоматической отправки отчётов нет.
- Live-only защита от незавершённой допечатываемой реплики ограничена `CjkVertical`: если новый normalized translation input является строгим prefix-extension предыдущего input того же candidate, к текущему timing preset добавляется `300 ms` quiet window. Повторное изменение сбрасывает OCR stability своим существующим source-revision путём; после стабильного полного текста guard снимается. Initial/static candidate, non-prefix OCR correction, Horizontal/non-CJK, cache key, provider retry/fallback и revision/source/cancellation/publication semantics не меняются.
- Gates: tracked regressions на `2/5/8/12` колонок, adjacent bubbles, ragged/staggered/noise/mixed layouts и explicit override; затем существующие S9/S10, Japanese/Chinese local geometry corpora, focused tests, Release build, full suite, docs mini-check и owner live smoke. Portable собирается только по отдельной команде владельца.
- Owner smoke r39 подтвердил post-filter correction на живой вертикальной китайской разметке: `41` wide multi-member completions (`12` двухколоночных, `19` трёхколоночных, `10` четырёхколоночных), из которых `33` дошли до непустого OCR и перевода, а `8` завершились OCR-empty. Групп шире четырёх source members и признаков page-wide/neighbor-bubble merge не обнаружено; точный четырёхколоночный `115x101` пример переведён как одна группа. Быстрых empty-overlay gaps короче секунды и тройных последовательных повторов слов в китайских переводах не найдено. Это owner-live acceptance узкого post-filter fix; оно не означает, что Tesseract распознаёт каждый принятый crop без ошибок.

Подробный локальный implementation handoff: `work/adaptive-cjk-vertical-grouping-handoff-20260829.md`.

## 18.4. Китайский detector preset и PP-OCRv5 research после r37

- Штатный `boxThreshold=0.60` не изменяется глобально. Per-zone `Standard` остаётся default для существующих и новых профилей; отсутствующее поле JSON десериализуется как `Standard`.
- Для ручного китайского smoke доступны opt-in `ChineseExperimental` (`0.65`) и diagnostic-only `ChineseStrictExperimental` (`0.70`). Оба режима разрешаются в `Standard` для non-Chinese language tags, используют тот же persistent Paddle predictor и не перезагружают модель между запросами.
- Headless A/B на локальных RCTW vertical, game-dialogue horizontal и MDPBench horizontal inputs показал для thresholds `0.60`, `0.65`, `0.70` соответственно `8/8/7`, `3/3/3` и `62/61/57` raw candidates. Поэтому `0.65` выбран только как безопасный opt-in для owner smoke; automatic default promotion запрещён, а `0.70` остаётся сравнительным режимом.
- Локальный offline PP-OCRv5 benchmark на тех же `11` detector crops сравнивает `PP-OCRv5_mobile_rec`, `PP-OCRv5_server_rec` и production Tesseract. Оба PP recognizer лучше распознали горизонтальный китайский game text, но вернули empty на высоком вертикальном crop, который Tesseract распознал. PP-OCRv5 не подключается к production без расширенного annotated benchmark, решения по vertical routing/packaging и отдельного ADR; локальные модели и текстовые отчёты не включаются в portable и не публикуются.
- Gates: profile round-trip/default/validation, per-request preset propagation, worker source contract, non-Chinese fallback, privacy-safe lifecycle diagnostics, focused tests, Release build, full suite, docs mini-check и owner live smoke. OCR/grouping semantics и roadmap items 4-5 не меняются.

## 18.5. BingWeb timeout/throttle visibility

- Один BingWeb HTTP request ограничен `15 s`; немедленного повтора того же запроса и автоматической смены provider нет.
- Первый последовательный timeout показывается как warning. Второй timeout немедленно открывает provider-local pause на `60 s` и показывается как error. Успешный ответ сбрасывает timeout counter.
- HTTP `429` показывается сразу, сразу открывает pause и использует корректный `Retry-After`, если он больше стандартных `60 s`. Во время pause новые network requests не отправляются.
- Уже опубликованный overlay сохраняется при Bing timeout/throttle, пока authoritative candidate остаётся актуальным. Его нельзя перепубликовать как новую revision; replacement проходит существующие geometry/source/revision checks.
- Live pipeline/UI status переносит provider id, failure kind/status, timing, consecutive count и retry interval. Каждый provider failure в локальном lifecycle дополнительно сохраняет точный failure kind, доступный HTTP status, pause state, относительный `Retry-After` и абсолютный `NextRetryAt`; сводка stopped-session report показывает `Paused`, `RetryAllowed` либо восстановление после подтверждённого network success. Raw provider response, credentials и frame pixels не записываются. Provider default/fallback policy не меняется.
- r39/r40 owner smoke зафиксировал кластеры Bing translation-stage failures около общего `15 s` candidate-work времени и последующие быстрые отказы при активной `60 s` pause. Без provider metadata старые отчёты не позволяют отделить реальные HTTP timeouts от queued work, которое затем fast-fail завершилось на уже открытой pause; поэтому такие кластеры нельзя считать числом фактически отправленных timeout-запросов. Observability follow-up закрыт source-level regression: будущий report различит `Timeout` и `Throttled`, покажет HTTP `429`, pause state и единый абсолютный retry boundary. Ни один failure не опубликовал пустой overlay поверх уже показанного текста; provider retry/fallback policy не менялась.
- Следующий source-level diagnostic слой закрывает оставшуюся неоднозначность: для каждого Bing cache miss фиксируются request id, bounded input, queue timestamp, provider start/completion и отдельные credential/translation network attempts с `WasNetworkRequestSent`, outcome и HTTP status. Paused fast-fail и cancellation отличаются от фактического timeout/429; никакой новый retry, batching, fallback или provider switch не добавляется.

## 18.6. Адаптивная группировка длинного горизонтального spaced-текста после r41

- Owner live-report от 2026-09-03 подтвердил pre-translation split длинных английских реплик: все `203/203` completed candidates имели одну translation-input group, но восемь соседних стыков в семи длинных блоках возникали сразу после группы ровно из десяти detector lines. Продолжения сохраняли не менее `80%` горизонтального перекрытия и всего `2..6 px` вертикального зазора; один блок был разделён `10 + 10 + 3`. Это ограничение candidate composition, а не поведение YandexWeb или translation grouping. Отчёт содержит owner-authorized OCR/translation text, остаётся local-only и не публикуется.
- Только `WritingSystemGroupingProfile.SpacedLeftToRight` с `MaximumHorizontalLines = null` получает adaptive Auto без fixed line count. После прежнего safety boundary в десять строк продолжение требует существующего strict overlap не менее `0.8`, вертикального зазора не более `12 px` и отсутствия одновременного абсолютного (`> 4 px`) и нормализованного (`> 0.2` median line height) скачка относительно медианного межстрочного интервала группы.
- Явный `MaximumHorizontalLines = 1..12` остаётся hard override. Все non-`SpacedLeftToRight` профили сохраняют прежний десятистрочный automatic safety limit; detector/OCR recognition, stability, revision/source revalidation, cancellation/publication, provider/cache behavior и roadmap items 4-5 не меняются.
- Gates: failing-first regression на подтверждённую геометрию `10 + 1`, coherent 23-line stack без fixed cap, significant-gap adjacent-bubble counterexample, explicit override и все шесть non-spaced profiles; затем focused grouping/OCR/architecture tests, Release build, full suite, docs mini-check и owner live smoke. Replacement portable собирается только по отдельной команде владельца.

------------------------------------------------------------------------

# 19. Этап 16. Автообновление (1 спринт)

Интеграция Squirrel.Windows или SingleFilePublisher. Проверка обновлений при старте, ручная проверка.

------------------------------------------------------------------------

# 20. Этап 17. Beta (2 спринта)

Тестирование на статичных скриншотах, оконный и borderless fullscreen режимы, разные разрешения.

------------------------------------------------------------------------

# 21. Этап 18. Release Candidate (1 спринт)

Исправление ошибок, улучшение UI (одно окно, сворачиваемые панели, зеленая/красная кнопка), документация.

Статус на 2026-09-04: актуальные developer README, user guide и review безопасных defaults подготовлены. Новый source-equivalent portable/RC после r43 не собран и не опубликован; это относится к отдельной release-задаче #30.

------------------------------------------------------------------------

# 22. Этап 19. Release 1.0 (1 спринт)

Полный состав: профили, JSON импорт/экспорт, три обязательных official translators и три отдельно выбираемых diagnostic web providers, Windows OCR + Tesseract, ADR-030 packaged detector runtime, overlay, многозонность, хоткеи, кэш (TTL 30 дней), диагностика, вертикальный текст и автообновление. Межпровайдерного fallback нет.

------------------------------------------------------------------------

# 23. Порядок реализации для ИИ-агента

Перед работой агент определяет целевую GitHub Issue, её явные зависимости, затронутые модули и применимые Quality Gates. Историческая последовательность этапов служит ориентиром, но не заменяет актуальный граф зависимостей.

Независимые исправления, тесты, документация и совместимые улучшения допускаются параллельно. Изменение архитектуры, публичного контракта, поведения по умолчанию, хранения данных или продуктовой политики оформляется решением согласно `docs/governance/change-approval-required.md`.
