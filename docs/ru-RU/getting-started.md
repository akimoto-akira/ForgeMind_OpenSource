# Быстрый старт ForgeMind

ForgeMind — **платформа управления игровыми движками на основе MCP**: через протокол MCP предоставляет ИИ-клиентам управление проектами Unreal Engine, чтение ассетов и операции запуска/сборки/редактирования. Текущая реализация — UE; платформа расширяема. Версия open source включает набор инструментов движка и функции bridge; коммерческая версия добавляет генерацию ассетов DSL (§5).

Все шаги этого руководства можно выполнить с частью **open source** (`OpenSource\` + `UEProject\` + `tests\`).

## 1. Требования к окружению

- .NET SDK (для сборки хоста и плагинов).
- Unreal Engine 5.0–5.8 (с редактором). Примерный проект — `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`.
- MCP-совместимый клиент (Claude Desktop / Cursor / собственный клиент).

## 2. Сборка сервера и плагинов

```powershell
# Хост
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Плагины (после сборки поместите DLL + .manifest.json с тем же именем в каталог plugins хоста)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> Не собирайте напрямую весь `ForgeMind.sln` (он содержит ссылки на коммерческие проекты; коммерческий каталог отсутствует в репозитории). Плагины также можно использовать из артефактов сборки, предоставленных дистрибьютором.

## 3. Запуск

1. **Запустите сервер** (по умолчанию: резидентный HTTP, порт 8765):

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # Или запустите собранный ForgeMindServer.exe; настраивается через --port N / --stdio / FORGEMIND_PORT
   ```

2. **Откройте редактор UE и примерный проект**: запустите `ForgeMind_5_0.uproject` в UE; плагин `ForgeMindForUE` автоматически запускает bridge-клиент в редакторе и подключается к хосту.

3. **Настройте клиент**: добавьте сервер в MCP-клиент:

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   После подключения появляется список инструментов (4 встроенных + внедрённые плагинами).

## 4. Первое использование

- `engine` — вывод информации о текущем движке;
- `project` / `project_files` — вывод примерного проекта и его файлов;
- `launch` — запуск редактора, `editor` — операции в редакторе;
- `asset` — чтение структуры ассетов проекта;
- `knowledge` — поиск по знаниям проекта.

> Долгие задачи (launch/build и т.п.) выполняются асинхронно. Результаты получаются через `get_task_result`, текущие задачи — через `list_executing_tasks`, очистка — через `cleanup_tasks`.

## 5. Коммерческая версия (DSL)

Модуль Commercial — это отдельная коммерческая редакция и не является частью репозитория open source. Коммерческая версия предлагает **генерацию ассетов DSL**: пишите блюпринты, материалы и т.п. в виде читаемого текста и конвертируйте их в ассеты движка. Конкретные инструменты DSL, функции преобразования текста и детали синтаксиса подчиняются коммерческой документации. Функции коммерческой лицензии также относятся к Commercial. OpenSource не содержит ни одного инструмента DSL.

## 6. Следующие шаги

- Архитектура и механизмы: `architecture.md`
- Список инструментов и параметры: `tools-reference.md`
- Разработка и контрибуции: `development-guide.md` / `contributing.md`
- Частые вопросы: `troubleshooting.md`
