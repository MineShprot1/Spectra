# Spectra 0.10 — аккаунт Spectra через Google

В собственном профиле появилась секция «Аккаунт Spectra» и кнопка «Подключиться к сети Spectra». Окно регистрации оформлено в текущей теме лаунчера. «Продолжить с Google» открывает обычный браузер; первый вход автоматически создаёт аккаунт Spectra, следующие подключают тот же аккаунт.

В профиле можно сменить уникальное имя Spectra, посмотреть собственную подключённую почту/ID и отключиться от сети. Друзья привязаны к ID Spectra, а не к Minecraft UUID. Minecraft-ник явно неподтверждён. Поиск друга использует аккаунты сети Spectra; Minecraft-ник используется только как подсказка. Почта скрыта от друзей и поиска.

Minecraft access token больше не передаётся Worker. Microsoft остаётся для игры, Google — для подтверждения отдельного аккаунта друзей. Сессия Spectra хранится через Windows DPAPI, отдельно для Microsoft-профиля и адреса сети. Сервер хранит только SHA-256 собственного session token.

Нужно обновить обе части и настроить Google Web OAuth client на Cloudflare: вся папка src (два JS-файла), SQL-миграция 0002, переменная PUBLIC_ORIGIN и секреты GOOGLE_CLIENT_ID/GOOGLE_CLIENT_SECRET. Подробная инструкция находится в README архива Spectra-Network-Cloudflare-2.0.zip. Старый Minecraft /auth отключён. Старые учётные записи не связываются с Google по публичному UUID.

Проверены JS syntax, frontend/theme/skin tests и полный сценарий сервера на SQLite с имитацией Google. Windows/C# build, реальный Google OAuth, Cloudflare D1 и Minecraft не проверены — здесь нет .NET SDK/Windows. После настройки проверьте два аккаунта и дружбу между ними.
