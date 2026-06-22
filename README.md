# MD Redactor

MD Redactor — нативное Windows 10 desktop-приложение для редактирования Markdown-файлов. Приложение открывает `.md`, показывает текст во встроенном WYSIWYG-редакторе и сохраняет изменения обратно в тот же файл.

## Стек

- .NET 10 и WPF.
- WebView2 для встроенного web-интерфейса.
- CommunityToolkit.Mvvm для базовой MVVM-инфраструктуры.
- TypeScript + Vite + ProseMirror для `web/editor`.
- Vitest для unit-тестов web-части.
- xUnit для тестов `MDRedactor.Core`.

## Сборка

```powershell
.\scripts\build.ps1
```

Скрипт проверяет git, .NET 10 SDK, Node.js, npm и WebView2 Runtime, собирает web-редактор, запускает web-тесты, затем выполняет restore/build/test для .NET solution.

Web-часть можно проверить отдельно:

```powershell
Set-Location .\web\editor
npm install
npm run build
npm test
```

## Запуск

```powershell
dotnet run --project .\src\MDRedactor.App\MDRedactor.App.csproj
```

Перед запуском нужно собрать web-часть через `.\scripts\build.ps1` или вручную выполнить `npm run build` в `web\editor`.

## Основной сценарий

1. Откройте Markdown-файл.
2. Выделите фрагмент в отформатированном документе.
3. Нажмите `Enter`.
4. Фрагмент станет правкой, справа появится карточка `#id`, а поле комментария сразу получит фокус.

Если выделение пересекается с существующей правкой, новая правка не создается: приложение активирует уже существующую правку и показывает русское уведомление. `Ctrl+S` сохраняет Markdown обратно в файл вместе с короткими тегами правок.

## Хранение правок

Правки будут храниться только внутри Markdown-файлов. Проект не использует базу данных, sidecar-json или отдельное хранилище изменений рядом с документом.
