# GameTranslator.UI

Developer README для WPF presentation/composition слоя. Модуль отвечает за пользовательский интерфейс, visual interaction glue и platform presentation services, но не за бизнес-правила OCR, переводчиков или кэша.

## Ответственность

- WPF startup, `MainWindow`, views, resources и ограниченный code-behind.
- MVVM view models, commands, validation и presentation state.
- Profile rail и вкладки `Zones & OCR`, `Translation`, `Overlay`, `Live & Diagnostics`, `Hotkeys & Settings`, `OCR Packs`.
- Семишаговый welcome tour, parameter help, capture-region picker и test/debug views.
- WPF overlay с раздельными mask, translation и debug layers.
- Composition host, global hotkeys, dialogs, logging и default local-storage paths.

## Граница зависимостей

`UI` имеет project reference только на `Application`. `Infrastructure` собирается и копируется как внешний composition module, затем загружается через `IApplicationServiceModule`; прямой `UI -> Infrastructure` reference запрещён.

Business rules остаются в `Domain`/`Application`; concrete external integrations — в `Infrastructure`. Code-behind допустим только для WPF lifecycle, visual tree и взаимодействия, которое нельзя разумно выразить в view model.

## Первый запуск и defaults

Пустые provider/languages и отсутствие зон не позволяют случайно начать сетевую обработку. Тур не изменяет профиль, не запускает capture и не вызывает provider. Default live timing — `Balanced`; debug overlay выключен. Полный пользовательский сценарий описан в [руководстве](../../docs/user-guide.md).

## Проверка

Для view-model/XAML changes запускайте focused UI/headless WPF tests. Видимый UI запускается только когда это действительно нужно для ручной проверки; overlay/focus/click-through changes дополнительно требуют соответствующего visual/manual gate. См. [AGENTS.md](AGENTS.md).
