# Revenant Auth Server

Сервер авторизации для Revenant Launcher. ASP.NET Core 8 + JWT.

База данных выбирается автоматически:
- **Локальная разработка** — SQLite (файл `data/revenant.db`, по умолчанию из `appsettings.json`), без внешних зависимостей.
- **Продакшен (Render/Neon)** — PostgreSQL, строка вида `postgres://...` задаётся переменной окружения `DATABASE_URL`.

## Эндпоинты

| Метод | Путь | Описание |
|-------|------|----------|
| POST | `/api/auth/register` | Регистрация `{ username, password }` |
| POST | `/api/auth/login` | Вход `{ username, password }` |
| POST | `/api/auth/refresh` | Продление сессии `{ refreshToken }` |
| POST | `/api/auth/logout` | Выход `{ refreshToken }` |
| POST | `/api/auth/change-password` | Смена пароля (Bearer access-токен) |
| GET | `/api/auth/me` | Профиль + история входов (Bearer access-токен) |
| GET | `/health` | Проверка живости |

Все auth-эндпоинты ограничены rate limiter'ом: 20 запросов/мин с одного IP.

## Локальный запуск

```bash
# Обязательно: секрет JWT (сервер без него не стартует!)
export JWT_SECRET="какая-нибудь-случайная-строка-длиннее-32-символов"

dotnet run
```

Сервер поднимется на `http://0.0.0.0:10000` (порт можно поменять переменной `PORT`).
Без `DATABASE_URL` используется SQLite-файл `data/revenant.db` — база создаётся автоматически.

Тест регистрации:

```bash
curl -X POST http://localhost:10000/api/auth/register -H "Content-Type: application/json" -d "{\"username\":\"testuser\",\"password\":\"123456\"}"
```

## Деплой на Render.com

1. Загрузи этот проект в репозиторий GitHub (`RevenantAuthServer`).
2. На Render: **New → Web Service** → выбери репозиторий.
3. Настройки:
   - **Runtime**: `Docker`
   - **Region**: Frankfurt
   - **Plan**: Free
4. Во вкладке **Environment** добавь переменные:
   - `JWT_SECRET` — **обязательно** (сервер не запустится без неё). Любая случайная строка от 32 символов:
     ```powershell
     -join ((48..57)+(65..90)+(97..122) | Get-Random -Count 48 | % {[char]$_})
     ```
   - `DATABASE_URL` — строка подключения Neon/Postgres, например `postgresql://user:pass@host/db`.
5. **Create Web Service**. Через 2-3 минуты получишь URL вида
   `https://revenant-auth.onrender.com` — впиши его в лаунчер
   (`Services/AuthService.cs`, константа `ApiBaseUrl`).

### Данные на бесплатном тарифе Render

- Файловая система бесплатного инстанса **эфемерная**: SQLite-файл на нём
  сбросится при каждом редеплое. Поэтому в проде обязательно используй
  внешний PostgreSQL (Neon) через `DATABASE_URL` — данные переживут редеплои.

## Безопасность

- Пароли хранятся только как **PBKDF2-SHA256** (100 000 итераций, соль на пользователя).
- Access-токен (JWT) — 15 минут. Refresh-токен — 30 дней, ротация при каждом использовании.
- `JWT_SECRET` **обязателен**: сервер завершается при старте, если он не задан
  или короче 32 символов — никаких запасных dev-секретов.
- Ведётся история входов (IP + время, последние 5 в `/api/auth/me`).
