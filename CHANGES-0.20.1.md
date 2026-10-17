# Spectra 0.20.1

Исправлена ошибка компиляции CS1501 в MainWindow.xaml.cs: RPC serverCatalog вызывал CommunityServers.Search с тремя аргументами вместо двух. Фильтры остаются только у поиска дополнений CatalogService.Search. Сервер и установщик не менялись.
