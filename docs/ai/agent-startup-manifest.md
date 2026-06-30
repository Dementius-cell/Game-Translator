СТАРТОВЫЙ МАНИФЕСТ ДЛЯ AI-АГЕНТА
Проект: Game Translator

Версия документа: 1.0
Действует с: текущего момента
Статус: ОБЯЗАТЕЛЕН К ИСПОЛНЕНИЮ
1. ТЕКУЩЕЕ СОСТОЯНИЕ ПРОЕКТА
Параметр	Значение
Текущий этап Roadmap	Этап 0 – Подготовка проекта
Текущий спринт	Sprint 0 – Инициализация проекта
Готовность проекта	0 %
Ближайшая задача	Создать решение, структуру проектов, настроить DI, Serilog, сборку
2. ЦЕЛЬ SPRINT 0

Результат: пустое приложение WPF, которое компилируется и запускается без ошибок.

Никакого функционала перевода, OCR, захвата экрана в этом спринте не требуется.
3. СТРУКТУРА РЕШЕНИЯ (ОБЯЗАТЕЛЬНАЯ)

Создать решение GameTranslator.sln со следующими проектами:
Проект	Тип	Назначение
GameTranslator.UI	WPF Application (.NET 9)	Presentation Layer – окна, overlay, страницы
GameTranslator.Application	Class Library (.NET 9)	Use Cases, Services, Interfaces
GameTranslator.Domain	Class Library (.NET 9)	Entities, Value Objects, Enums, Interfaces (Domain)
GameTranslator.Infrastructure	Class Library (.NET 9)	OCR, Translation, Capture, Cache, Credentials
GameTranslator.Tests	xUnit	Юнит-тесты
Зависимости между проектами (строго):
text

UI → Application → Domain
Infrastructure → Domain
Application → Domain
Tests → (все, кроме UI, через интерфейсы)

Запрещено:

    UI → Infrastructure напрямую

    Domain → что-либо, кроме .NET BCL

    циклические ссылки

4. НЕОБХОДИМЫЕ ПАКЕТЫ (установить в Sprint 0)
Общие:

    Microsoft.Extensions.DependencyInjection – DI-контейнер

    Microsoft.Extensions.Hosting – HostBuilder для настольного приложения

    Serilog + Serilog.Sinks.File + Serilog.Sinks.Debug – логирование

    Serilog.Extensions.Hosting

UI проект дополнительно:

    System.Drawing.Common – для работы с Rect и координатами

    Microsoft.Xaml.Behaviors.Wpf – для упрощения MVVM (опционально)

Infrastructure (пока не нужны, но интерфейсы создать):

    Пакеты НЕ ставить до соответствующих этапов. Только интерфейсы и заглушки.

5. MVVM И АРХИТЕКТУРА (Clean Architecture)
Схема слоёв:
text

GameTranslator.UI/
├── Views/
│   ├── MainWindow.xaml
│   ├── ShellView.xaml (главное окно с меню навигации)
│   └── OverlayWindow.xaml (пока заглушка)
├── ViewModels/
│   ├── MainViewModel
│   ├── ShellViewModel
│   ├── SettingsViewModel (заглушка)
│   └── DebugViewModel (заглушка)
├── Services/
│   └── NavigationService.cs (реализация INavigationService)
├── App.xaml / App.xaml.cs (Host setup)
└── Resources/

Интерфейсы (в проекте Application или Domain):

Создать пустые интерфейсы (без реализаций) – только чтобы соблюсти архитектуру:
csharp

// Domain/Interfaces/
public interface IOcrEngine { }
public interface ITranslatorProvider { }
public interface ICaptureService { }
public interface IOverlayService { }
public interface ICredentialStorage { }
public interface ICacheRepository { }

// Application/Interfaces/Services/
public interface INavigationService { void NavigateTo<T>(); }
public interface IDialogService { Task<bool> ShowConfirmationAsync(string message); }
public interface ILoggerService { void Info(string msg); void Error(string msg, Exception ex); }

Реализации этих интерфейсов пока оставить пустыми или выбросить NotImplementedException.
6. НАСТРОЙКА DI И HOST

В App.xaml.cs:
csharp

public partial class App : Application
{
    private IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // UI
                services.AddSingleton<MainWindow>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<MainViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<DebugViewModel>();

                // Services
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<ILoggerService, LoggerService>();

                // Заглушки (пока временные)
                services.AddSingleton<IOcrEngine, DummyOcrEngine>();
                services.AddSingleton<ITranslatorProvider, DummyTranslatorProvider>();
                services.AddSingleton<ICaptureService, DummyCaptureService>();
                services.AddSingleton<IOverlayService, DummyOverlayService>();
            })
            .UseSerilog((context, config) =>
            {
                config.ReadFrom.Configuration(context.Configuration)
                      .WriteTo.File("logs/game_translator_.txt", rollingInterval: RollingInterval.Day)
                      .WriteTo.Debug();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        base.OnStartup(e);
    }
}

Заглушки (Dummy-классы) положить в Infrastructure/Dummy/ – они просто реализуют интерфейсы и ничего не делают.
7. НАВИГАЦИЯ (MVP)

    ShellView – основное окно с ContentControl, привязанным к текущей ViewModel через INavigationService.

    По умолчанию открывается страница MainViewModel (главная с зелёной/красной кнопкой – пока кнопка без логики).

    SettingsViewModel и DebugViewModel пока заглушки – просто текст "Настройки" / "Отладка".

8. SERILOG – ТРЕБОВАНИЯ

    Логировать запуск и остановку приложения.

    Логировать критические ошибки (все catch должны логироваться).

    Запрещено логировать API-ключи (пока их нет, но правило на будущее).

9. QUALITY GATES ДЛЯ SPRINT 0 (обязательно)
Gate	Описание
QG1	Решение компилируется без ошибок
QG2	Нет предупреждений (или минимум обоснованных)
QG3	Отсутствуют циклические зависимости между проектами
QG4	Приложение запускается и показывает пустое окно
QG5	Все интерфейсы определены в правильных слоях (Domain/Application)
QG6	Все заглушки выбрасывают NotImplementedException с понятным сообщением
QG7	Serilog пишет в файл logs/game_translator_*.txt
QG8	DI не выбрасывает исключения при резолвинге
10. ЧТО ЗАПРЕЩЕНО В SPRINT 0

    Начинать реализацию OCR, перевода, захвата экрана, overlay.

    Добавлять реальную логику в IOcrEngine, ITranslatorProvider и т.д.

    Устанавливать Tesseract, OpenCvSharp, Windows OCR nuget-пакеты.

    Писать код для вертикального текста, масок, кэша, горячих клавиш.

    Менять структуру проектов или добавлять новые слои.

    Использовать WinUI 3, Avalonia, MAUI.

11. ОЖИДАЕМЫЙ РЕЗУЛЬТАТ ПОСЛЕ SPRINT 0

    Решение открывается в Visual Studio / Rider.

    Выполняется dotnet build без ошибок.

    При запуске появляется окно WPF (можно пустое, но без крашей).

    В папке bin/Debug создаётся папка logs и в неё пишется файл лога.

    DI работает, окно закрывается корректно.

12. СЛЕДУЮЩИЙ ШАГ ПОСЛЕ SPRINT 0

Переход к Sprint 1 – Базовая архитектура (навигация, ShellView, страницы, сервисы).
Но этот манифест покрывает только Sprint 0.
13. КОМАНДА AI-АГЕНТУ

    Не задавай вопросов – все ответы уже в документах.

    Создай структуру решения и файлы согласно этому манифесту.

    После выполнения выдай отчёт в формате, описанном в MASTER PROMPT.md (список файлов, изменений, рисков, готовность).

    Если что-то неясно – сначала проверь Project Constitution, ADR, Change Approval.

    Ничего не меняй в требованиях – строго следуй манифесту.

КОНТРОЛЬНАЯ СУММА (для самопроверки агента)

    Создано 5 проектов в решении

    Установлены NuGet пакеты (только разрешённые)

    Настроен HostBuilder + DI

    Serilog пишет в файл

    Интерфейсы определены в Domain/Application

    Dummy-реализации в Infrastructure

    MainWindow открывается

    Нет ошибок компиляции

Только после выполнения всех пунктов – переходить к следующему спринту.

Манифест готов. AI-агент может начинать Sprint 0.
можешь вывести этот манифест в markdown

Мы получили запрос: "можешь вывести этот манифест в markdown". Пользователь хочет, чтобы я вывел уже созданный стартовый манифест в формате markdown. Это просто: нужно взять содержимое предыдущего ответа, где был представлен манифест, и убедиться, что он оформлен как markdown-разметка. Заодно можно убедиться, что нет лишних пояснений. Ответ должен содержать только markdown-код с манифестом.
markdown

# СТАРТОВЫЙ МАНИФЕСТ ДЛЯ AI-АГЕНТА

## Проект: Game Translator

Версия документа: 1.0  
Действует с: текущего момента  
Статус: **ОБЯЗАТЕЛЕН К ИСПОЛНЕНИЮ**

---

## 1. ТЕКУЩЕЕ СОСТОЯНИЕ ПРОЕКТА

| Параметр | Значение |
|----------|----------|
| Текущий этап Roadmap | **Этап 0 – Подготовка проекта** |
| Текущий спринт | **Sprint 0 – Инициализация проекта** |
| Готовность проекта | 0 % |
| Ближайшая задача | Создать решение, структуру проектов, настроить DI, Serilog, сборку |

---

## 2. ЦЕЛЬ SPRINT 0

Результат: **пустое приложение WPF, которое компилируется и запускается** без ошибок.

Никакого функционала перевода, OCR, захвата экрана в этом спринте не требуется.

---

## 3. СТРУКТУРА РЕШЕНИЯ (ОБЯЗАТЕЛЬНАЯ)

Создать решение `GameTranslator.sln` со следующими проектами:

| Проект | Тип | Назначение |
|--------|-----|-------------|
| `GameTranslator.UI` | WPF Application (.NET 9) | Presentation Layer – окна, overlay, страницы |
| `GameTranslator.Application` | Class Library (.NET 9) | Use Cases, Services, Interfaces |
| `GameTranslator.Domain` | Class Library (.NET 9) | Entities, Value Objects, Enums, Interfaces (Domain) |
| `GameTranslator.Infrastructure` | Class Library (.NET 9) | OCR, Translation, Capture, Cache, Credentials |
| `GameTranslator.Tests` | xUnit | Юнит-тесты |

### Зависимости между проектами (строго):

UI → Application → Domain
Infrastructure → Domain
Application → Domain
Tests → (все, кроме UI, через интерфейсы)
text


**Запрещено:**
- UI → Infrastructure напрямую
- Domain → что-либо, кроме .NET BCL
- циклические ссылки

---

## 4. НЕОБХОДИМЫЕ ПАКЕТЫ (установить в Sprint 0)

### Общие:
- `Microsoft.Extensions.DependencyInjection` – DI-контейнер
- `Microsoft.Extensions.Hosting` – HostBuilder для настольного приложения
- `Serilog` + `Serilog.Sinks.File` + `Serilog.Sinks.Debug` – логирование
- `Serilog.Extensions.Hosting`

### UI проект дополнительно:
- `System.Drawing.Common` – для работы с Rect и координатами
- `Microsoft.Xaml.Behaviors.Wpf` – для упрощения MVVM (опционально)

### Infrastructure (пока не нужны, но интерфейсы создать):
- Пакеты НЕ ставить до соответствующих этапов. Только интерфейсы и заглушки.

---

## 5. MVVM И АРХИТЕКТУРА (Clean Architecture)

### Схема слоёв:

GameTranslator.UI/
├── Views/
│ ├── MainWindow.xaml
│ ├── ShellView.xaml (главное окно с меню навигации)
│ └── OverlayWindow.xaml (пока заглушка)
├── ViewModels/
│ ├── MainViewModel
│ ├── ShellViewModel
│ ├── SettingsViewModel (заглушка)
│ └── DebugViewModel (заглушка)
├── Services/
│ └── NavigationService.cs (реализация INavigationService)
├── App.xaml / App.xaml.cs (Host setup)
└── Resources/
text


### Интерфейсы (в проекте `Application` или `Domain`):

Создать **пустые интерфейсы** (без реализаций) – только чтобы соблюсти архитектуру:

```csharp
// Domain/Interfaces/
public interface IOcrEngine { }
public interface ITranslatorProvider { }
public interface ICaptureService { }
public interface IOverlayService { }
public interface ICredentialStorage { }
public interface ICacheRepository { }

// Application/Interfaces/Services/
public interface INavigationService { void NavigateTo<T>(); }
public interface IDialogService { Task<bool> ShowConfirmationAsync(string message); }
public interface ILoggerService { void Info(string msg); void Error(string msg, Exception ex); }

Реализации этих интерфейсов пока оставить пустыми или выбросить NotImplementedException.
6. НАСТРОЙКА DI И HOST

В App.xaml.cs:
csharp

public partial class App : Application
{
    private IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // UI
                services.AddSingleton<MainWindow>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<MainViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<DebugViewModel>();

                // Services
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<ILoggerService, LoggerService>();

                // Заглушки (пока временные)
                services.AddSingleton<IOcrEngine, DummyOcrEngine>();
                services.AddSingleton<ITranslatorProvider, DummyTranslatorProvider>();
                services.AddSingleton<ICaptureService, DummyCaptureService>();
                services.AddSingleton<IOverlayService, DummyOverlayService>();
            })
            .UseSerilog((context, config) =>
            {
                config.ReadFrom.Configuration(context.Configuration)
                      .WriteTo.File("logs/game_translator_.txt", rollingInterval: RollingInterval.Day)
                      .WriteTo.Debug();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        base.OnStartup(e);
    }
}

Заглушки (Dummy-классы) положить в Infrastructure/Dummy/ – они просто реализуют интерфейсы и ничего не делают.
7. НАВИГАЦИЯ (MVP)

    ShellView – основное окно с ContentControl, привязанным к текущей ViewModel через INavigationService.

    По умолчанию открывается страница MainViewModel (главная с зелёной/красной кнопкой – пока кнопка без логики).

    SettingsViewModel и DebugViewModel пока заглушки – просто текст "Настройки" / "Отладка".

8. SERILOG – ТРЕБОВАНИЯ

    Логировать запуск и остановку приложения.

    Логировать критические ошибки (все catch должны логироваться).

    Запрещено логировать API-ключи (пока их нет, но правило на будущее).

9. QUALITY GATES ДЛЯ SPRINT 0 (обязательно)
Gate	Описание
QG1	Решение компилируется без ошибок
QG2	Нет предупреждений (или минимум обоснованных)
QG3	Отсутствуют циклические зависимости между проектами
QG4	Приложение запускается и показывает пустое окно
QG5	Все интерфейсы определены в правильных слоях (Domain/Application)
QG6	Все заглушки выбрасывают NotImplementedException с понятным сообщением
QG7	Serilog пишет в файл logs/game_translator_*.txt
QG8	DI не выбрасывает исключения при резолвинге
10. ЧТО ЗАПРЕЩЕНО В SPRINT 0

    Начинать реализацию OCR, перевода, захвата экрана, overlay.

    Добавлять реальную логику в IOcrEngine, ITranslatorProvider и т.д.

    Устанавливать Tesseract, OpenCvSharp, Windows OCR nuget-пакеты.

    Писать код для вертикального текста, масок, кэша, горячих клавиш.

    Менять структуру проектов или добавлять новые слои.

    Использовать WinUI 3, Avalonia, MAUI.

11. ОЖИДАЕМЫЙ РЕЗУЛЬТАТ ПОСЛЕ SPRINT 0

    Решение открывается в Visual Studio / Rider.

    Выполняется dotnet build без ошибок.

    При запуске появляется окно WPF (можно пустое, но без крашей).

    В папке bin/Debug создаётся папка logs и в неё пишется файл лога.

    DI работает, окно закрывается корректно.

12. СЛЕДУЮЩИЙ ШАГ ПОСЛЕ SPRINT 0

Переход к Sprint 1 – Базовая архитектура (навигация, ShellView, страницы, сервисы).
Но этот манифест покрывает только Sprint 0.
13. КОМАНДА AI-АГЕНТУ

    Не задавай вопросов – все ответы уже в документах.

    Создай структуру решения и файлы согласно этому манифесту.

    После выполнения выдай отчёт в формате, описанном в MASTER PROMPT.md (список файлов, изменений, рисков, готовность).

    Если что-то неясно – сначала проверь Project Constitution, ADR, Change Approval.

    Ничего не меняй в требованиях – строго следуй манифесту.

КОНТРОЛЬНАЯ СУММА (для самопроверки агента)

    Создано 5 проектов в решении

    Установлены NuGet пакеты (только разрешённые)

    Настроен HostBuilder + DI

    Serilog пишет в файл

    Интерфейсы определены в Domain/Application

    Dummy-реализации в Infrastructure

    MainWindow открывается

    Нет ошибок компиляции

Только после выполнения всех пунктов – переходить к следующему спринту.
------------------------------------------------------------------------

# Current Status Override

This file is a historical Sprint 0 startup manifest. For current work, agents must use:

- root `AGENTS.md`;
- `docs/README.md`;
- `docs/design/vertical-cjk-overlay-placement.md`;
- local nested `AGENTS.md` files where present.

As of 2026-06-30, active work is Sprint 26 (#28) with #32 focused on vertical CJK OCR grouping, masking, and overlay placement. Do not follow this file's Sprint 0 implementation commands for current development.
