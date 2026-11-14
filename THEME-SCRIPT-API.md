# Живые темы — API v2 (совместим с файлами format: 1)

Откройте карандаш в навигации. Панель справа снизу имеет вкладки «Скрипт» и «CSS». Макет — сам лаунчер. Автопредпросмотр запускается через 600 мс после ввода; его можно отключить. Ctrl+Enter применяет вручную. Панель перетаскивается за заголовок, изменяет размер за нижнюю грань и сворачивается.

«Отменить изменения» и крестик возвращают снимок оформления. «Сохранить» сохраняет отдельную кастомную тему и оставляет её активной. Экспорт включает поля script и css в .spectratheme. Ctrl+Shift+F12 отключает кастомную тему, закрывает редактор и сохраняет системную «Спектру» (theme: dark) в тёмном режиме.

## Скрипт

Стандартный JavaScript, включая async/await. Выполняется в Worker отдельного служебного iframe. У Worker нет DOM, WebView bridge, доступа к хранилищу аккаунтов или сети. API передаёт только команды оформления и разрешённые действия. Начальная загрузка ограничена 3 секундами; есть watchdog, ограничения элементов, сообщений и анимаций. CSS выполняется в настоящем окне. Бесконечный цикл не блокирует редактор.

```js
ui.screen(); // временно скрыть штатное содержимое и навигацию
ui.container({ id: 'home', style: { padding: '32px' } });
ui.text({ parent: 'home', text: 'Мои игры' });
ui.grid({ id: 'games', parent: 'home' });
for (const game of data.instances) {
  const tile = ui.tile({ parent: 'games', text: game.name });
  ui.on(tile, 'click', () => ui.launch(game.id));
}
```

Создание: ui.container, text, button, image, tile, grid, input, slider, select и create(tag, props). Возвращается строковый ID, используемый в parent и других методах. У каждого элемента свой необязательный id, text, className и style. Изображения: src и alt; input: type/value; slider: min/max/step/value; select: options[{value,label}]. Допустимые теги: div, span, button, img, section, nav, h1, h2, p, input, select.

Изменение: ui.style(idOrSelector, object), update(id, {text,value,style}), focus(id), remove(id), clear(). clear(target = "root") очищает выбранную область и её события/анимации; панели и их кнопки в других областях сохраняются. Селекторы штатных элементов доступны только для изменения стилей; заголовок окна защищён. Собственный корень — root / #themeCustomRoot.

События: ui.on(id, 'click'|'pointerenter'|'pointerleave'|'keydown'|'focus'|'blur'|'input'|'change', callback). В callback передаётся объект {type,key,value}. ui.animate(id, keyframes, {duration,iterations,easing}); CSS @keyframes также поддерживается. Действия ui.launch(instanceId), ui.navigate('instances'|'versions'|'discover'|'profile'|'settings'), ui.action('friends') выполняются по пользовательскому нажатию/клавише. ui.navigate открывает штатный экран, сохраняя Worker, состояние скрипта и собственные панели. Логи: ui.log(text).

Данные: data.player.name, data.page, data.instances[{id,name,version,loader,icon}]. Это публичные данные без учётных токенов. При навигации объект data обновляется. ui.onPage(callback) получает обновлённый снимок после завершения перехода. Вызовите свой render(data) также при первом запуске. ui.onPage возвращает функцию отписки. Обработчик навигации сам по себе не даёт права запускать игру.

## CSS

```css
#themeCustomRoot { background: linear-gradient(135deg, #173c83, #2467b2); color: white; }
#theme-ui-home .tile { min-height: 140px; transition: transform .2s; }
#theme-ui-home .tile:hover, #theme-ui-home .tile:focus { transform: translateY(-6px); }
```

ID элемента x отображается как #theme-ui-x и [data-theme-node="x"]. Панель редактора использует Shadow DOM и отдельные стили.

Разработка runtime: после изменения theme-runtime-worker.js выполнить node tools/build-theme-runtime.cjs. Проверка синхронизации: node tools/build-theme-runtime.cjs --check.

## Постоянные панели и страницы

`const top = ui.region('top'); const bottom = ui.region('bottom');` создают области в верхней и нижней панелях. Передайте результат в `parent` при создании элементов. По умолчанию штатное содержимое панели скрывается. `ui.region('top', {replace: false})` возвращает штатное содержимое рядом с собственным. Управление окном и область перетаскивания остаются штатными. Стили собственных областей: `ui.style('top', {...})`, `ui.style('bottom', {...})`; CSS: `#themeRegion-top`, `#themeRegion-bottom`. Общий лимит 500 элементов относится ко всем областям вместе.

При переходе собственное содержимое `root` временно скрывается, штатная страница открывается под постоянными панелями. Чтобы заменить страницу, вызовите `ui.clear()` и `ui.screen()` внутри `ui.onPage`. Если не вызывать `ui.screen()`, штатная страница остаётся доступной. CSS темы продолжает действовать на все страницы. Скрипт останавливается при смене темы, аварийном сбросе или ошибке/зависании.

Готовый пример: `examples/themes/persistent-shell.js`. Формат .spectratheme остаётся прежним; старые скрипты работают, но ui.navigate больше не завершает их.
