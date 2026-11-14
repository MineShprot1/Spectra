// Вставьте во вкладку «Скрипт». Дополнительный CSS не требуется.
const top = ui.region('top');
const bottom = ui.region('bottom');
ui.text({parent: top, text: 'SPECTRA', style: {fontWeight: 'bold', marginRight: '20px'}});
for (const [page, title] of [['instances','Сборки'], ['discover','Обзор'], ['settings','Настройки'], ['profile','Профиль']]) {
  const button = ui.button({parent: top, text: title});
  ui.on(button, 'click', () => ui.navigate(page));
}
const friends = ui.button({parent: top, text: 'Друзья'});
ui.on(friends, 'click', () => ui.action('friends'));
ui.text({parent: bottom, id: 'pageLabel', text: ''});
let visits = 0;
function render(current) {
  ui.update('pageLabel', {text: `${current.player.name} · ${current.page} · Переходов: ${visits++}`});
  if (current.page !== 'instances') return; // Остальные страницы — штатные, панели сохраняются.
  ui.clear();
  ui.screen();
  ui.style('root', {padding: '28px', overflow: 'auto'});
  ui.text({text: 'Моя библиотека', style: {fontSize: '24px'}});
  const grid = ui.grid({style: {marginTop: '24px'}});
  for (const game of current.instances) {
    const tile = ui.tile({parent: grid, text: `${game.name} · ${game.version}`, style: {minHeight: '120px'}});
    ui.on(tile, 'click', () => ui.launch(game.id));
  }
}
ui.onPage(render);
render(data);
