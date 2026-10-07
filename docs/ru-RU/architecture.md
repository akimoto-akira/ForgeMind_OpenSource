# Архитектура ForgeMind

ForgeMind — **платформа управления игровыми движками на основе MCP**: сервер (хост) предоставляет инструменты ИИ-клиентам через протокол MCP; функции инструментов в основном внедряются плагинами. На стороне движка плагин-адаптер принимает операции через локальный TCP bridge. UE — текущая реализация; платформа расширяема на другие движки. Этот документ основан на коде, видимом в **публичной** части; коммерческие функции описаны только в общем виде в §7.

## 1. Обзор

```
┌─────────────┐   MCP (HTTP/stdio)   ┌──────────────────────┐
│ ИИ-клиент   │ ◄────────────────► │ ForgeMindServer (хост)│
└─────────────┘                     │  • Встроенные инструменты ×4│
                                    │  • PluginLoader             │
                                    │  • TCP bridge-сервер       │
                                    │  • Реестр движков         │
                                    └──────────┬───────────┘
                                               │ Локальный TCP bridge (4-байтовый префикс + JSON-конверт)
                                    ┌──────────▼───────────┐
                                    │ Плагин-адаптер UE     │
                                    │ (ForgeMindForUE)      │
                                    └──────────────────────┘
```

- **Хост** (`OpenSource\ForgeMindServer`): ядро MCP-сервера.
- **Плагины** (`OpenSource\ForgeMind.*Plugin`): регистрируют инструменты через `IPlugin`. Хост содержит только минимальный набор инструментов.
- **Адаптер движка** (`UEProject\...\Plugins\ForgeMindForUE`): плагин в редакторе. Реализует функции на стороне движка как bridge-сервер.
- **Сканирование/упаковка** (`ForgeMind.Scan.Abstractions`, `ForgeMind.Scan`, `ForgeMind.Scan.Unreal`, `ForgeMind.Speedy`, `ForgeMind.EngineScanner`): обеспечивают офлайн-индекс функций движка и упаковку SPK.
- **Примерный проект** (`UEProject\ForgeMind_5_0`): для совместных тестов и отладки.

## 2. Хост (ForgeMindServer)

- **Точка входа**: `Program.cs` по умолчанию запускает резидентный HTTP-сервис на `127.0.0.1:8765` (Streamable HTTP + совместимость с SSE). `--stdio` переключает в режим spawn-per-client. Порт можно переопределить через `--port` / `FORGEMIND_PORT`.
- **Встроенные инструменты** (таблица инструментов `ForgeMindServerHost`, только 4):
  - `settings` (`SettingsTool`, чтение/запись конфигурации);
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks` (запрос и очистка долгих задач).
- **Загрузка плагинов**: `PluginLoader` сканирует `plugins\` (по умолчанию `<каталог хоста>\plugins\`, переопределяется через `FORGEMIND_PLUGINS_DIR`) и внедряет таблицу инструментов, реестр движков и bridge через `PluginContext`. Плагин `OnLoad` вызывает `RegisterTool` для внедрения инструментов. Пакеты плагинов поддерживают два варианта: «подкаталог = пакет плагина» (основная DLL + манифест с тем же именем + зависимости) и «свободные DLL в корне». Загрузка ленивая (активируется при первом обращении к таблице инструментов); при ошибке игнорируется только данный плагин, хост не затрагивается.
- **Реестр движков**: `EngineRegistry` перечисляет локальные движки при старте (предпочтение установкам Launcher `UE_x.y`, fallback на сборки из исходников) и внедряет плагинам снапшот только для чтения.
- **TCP bridge-сервер**: `ForgeMindServerBridgeServer`. Для обнаружения и подключения адаптера движка (§5).

## 3. Плагины и внедрение инструментов

| Плагин | Инструменты | Ответственность |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | Управление движками и проектами, запуск/сборка/операции редактора |
| `ForgeMind.Assets` | `asset` | Офлайн-чтение структуры `.uasset` (`UAssetArchive` / `BlueprintGraphReader` и т.п.) |
| `ForgeMind.Knowledge` | `knowledge` | Поиск знаний (источник каталога / источник пакета индекса, `KnowledgeStore`) |
| `ForgeMind.EngineScanner` | (CLI-инструмент, не MCP) | Перечисление движков → сканирование `UnrealScanner` → упаковка `.spk` через `ScanResultPacker` |

Хост и плагины взаимодействуют через контракт `ITool` (`Compat\ITool.cs`): имя/описание/схема параметров/`Execute` возвращает `ToolResult`. `ForgeMindServerTest` также вызывает инструменты напрямую через `ITool` (без протокола MCP) для проверки поведения.

## 4. Сканирование и SPK (основа офлайн-функций)

- `ForgeMind.Scan.Abstractions`: абстракции `IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures` и т.п.
- `ForgeMind.Scan.Unreal`: `UnrealScanner` + `DocumentEngine` (`Ue5DocumentEngine`, `HeaderScanner`, `RulesCompiler`, `UAssetScanner`, `ScanIndexStore` и т.п.) — анализирует заголовочные файлы UE, файлы правил и блюпринт-ассеты в индекс сканирования. `ScanIndexStore` — индекс в памяти во время выполнения и не записывается на диск. Это **отличается от старого ScanCache** — текущий конвейер не использует предзагрузку/распределённый кэш ScanCache (остались только исторические следы совместимости в коде; они не являются текущим механизмом).
- `ForgeMind.Scan`: `ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider` строят и читают пакеты сканирования.
- `ForgeMind.Speedy`: `SpeedyPack` предоставляет высокопроизводительный формат упаковки (`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`, с внутренним CRC, отображением каталогов, журналом предзаписи `WriteAheadLog` и т.п.).
- Использование: `ForgeMind.EngineScanner <spkPath>` генерирует `.spk`. Такие функции, как knowledge, потребляют его через `SpkScanDataProvider`.

## 5. Протокол bridge и адаптер движка

- Bridge: локальный TCP. Сообщения используют 4-байтовый префикс длины + JSON-конверт (`BridgeMessage` / `BridgeSession` / `FrameCodec`). Адаптер сначала читает `%USERPROFILE%\.forgemind\bridge.json` для обнаружения и аутентификации, затем подключение → handshake → цикл сообщений.
- На стороне сервера маршрутизация основана на имени команды к реализации адаптера. Обработчики команд выполняются в главном потоке движка (игровой поток UE) для обеспечения согласованности с состоянием редактора.
- Адаптер движка (сторона UE) находится в `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\`, модуль Editor с bridge-клиентом и аппликатором IR. Примерный проект UE совместим с 5.0–5.8.
- Чтобы добавить новый движок, достаточно реализовать изоморфный bridge-клиент и обработчики команд (`development-guide.md` §4). Изменения в хосте или плагинах не требуются.

## 6. Лицензия и безопасность (публичная часть)

- Авторизация по умолчанию: `OpenAuthorizationService` (`Authorization\Open\`) — фиксированные community-функции, **не анализирует файлы лицензий**.
- Безопасность плагинов: `PluginIntegrityVerifier` (проверка целостности манифестов), `PluginSigningKeys` (проверка подписей); `ManifestLoader` анализирует манифесты плагинов и поддерживает ServiceOverride (замена сервисов хоста абстрактными типами, напр. `IAuthorizationService`).
- Авторизация bridge: подключения адаптеров должны читать локальные учётные данные из `bridge.json` для завершения handshake (`security.md`).

## 7. Коммерческие функции (обзор)

- **Язык DSL и доменные трансляторы** (закрытый код): коммерческие функции, генерирующие ассеты движка из читаемого текста. Публичная часть содержит только слой фреймворка DSL (`ParserFactory`, `ITranslator`, `TranslatorRegistry`, `IIrConverter`, `IrConverterRegistry`, `DiagnosticReporter`, интерфейсы и реестры в `ForgeMindServer\DSL\`); конкретный синтаксис, доменные трансляторы и правила IR предоставляются коммерческими модулями и отсутствуют в репозитории.
- **Коммерческая лицензионная система** (закрытый код): включает выпуск лицензий, проверку лицензий, сервис лицензий и опциональную привязку к окружению. Конкретная реализация, алгоритмы подписей, формат файлов лицензий и логика на стороне сервера не являются публичной частью и внешне представлены лишь как «предоставление дополнительных функций».

> Версия open source предлагает полный набор инструментов open source и функций bridge. Версия Commercial использует тот же хост, механизм плагинов и протокол bridge и расширяет коммерческие функции (генерация ассетов DSL, коммерческие лицензии, enterprise-поддержка).

## 8. Таблица границ функций

| Функция | Принадлежность |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| Интерфейсы расширения DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| Правила IR | Commercial 🔒 |
| Генератор ассетов DSL | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| Реализация коммерческих лицензий | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| Система ключей/подписей | Commercial 🔒 |
| Внутренние детали протокола лицензий | Commercial 🔒 |
