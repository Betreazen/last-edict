# Сервер, доставка и эксплуатация

Статус: проект начального deployment. Вместимость определяется нагрузочным тестом.

## 1. За что платит сервер

В одиночной игре CPU-симуляция и отрисовка работают в браузере. Сервер/CDN передаёт
файлы; необязательный аккаунт/облачный save добавляет запросы, но не симуляцию.
В MP сервер выполняет команды и ИИ, хранит состояние и рассылает player views.
Он не рисует кадры и не декодирует игровые спрайты.

Rust потенциально уменьшает цену вычислений и состояние сервисов, но не сокращает
число переданных PNG/видео. Ресурсы, CDN, кэширование и backpressure дают независимый эффект.
Загрузка состояния из БД на каждый клик также не является обязательной оптимизацией:
активные комнаты остаются в памяти; неактивные сохраняются и освобождаются.

## 2. Начальная топология

```text
HTTPS domain
  /              → HTML/JS/Wasm из static storage через CDN
  /content/*     → immutable public objects через CDN
  /catalog/*     → публичные manifests с revalidation
  /api/*, /ws    → reverse proxy → один Rust service
                                      ├─ PostgreSQL
                                      └─ private scenario storage
```

Один процесс Axum/Tokio объединяет lobby, HTTP и WebSocket. Это модульный сервис,
не набор микросервисов. Axum предназначен для Tokio/Hyper, поддерживает HTTP handlers
и middleware. [Документация Axum](https://docs.rs/axum/latest/axum/).

Не нужны исходно Kubernetes, Redis, Kafka, отдельный процесс игры на комнату,
GPU на сервере и распределённые actor framework. Reverse proxy завершает TLS и задаёт
ограничения подключений. Для локальной разработки компоненты могут быть на одной машине;
это не означает, что результаты её теста обещают production-вместимость.

## 3. Комнаты и вычисления

Runtime комнаты держит компактный World, версию, очередь команд, подписчиков и timer.
Общие правила по ruleset разделяются через immutable references. Одна комната применяет
команды последовательно; комнаты могут рассчитываться параллельно.

Длинный ход ИИ отправляется в ограниченный compute pool с semaphore. Не выполнять
его прямо внутри async WebSocket handler. `spawn_blocking` сам по себе не ограничивает
число CPU-heavy заданий и начатую работу нельзя произвольно отменить; нужны admission
control и проверка отмены между порциями работы.
[Tokio spawn_blocking](https://docs.rs/tokio/latest/tokio/task/fn.spawn_blocking.html).

Ход ИИ разбивается на детерминированные команды/контрольные точки. При переполнении
очереди новые тяжёлые работы ждут с показом состояния или получают controlled retry;
нельзя создавать неограниченные tasks/threads. Между комнатами — справедливое обслуживание,
чтобы один огромный сценарий не задерживал все партии.

Состояния без подключений/ожидающие длительную паузу выгружаются после commit и
настраиваемого TTL. Список deadlines хранится устойчиво; после рестарта просроченные
таймеры превращаются в идемпотентные системные команды. Активная комната не загружается
из БД заново на каждый heartbeat.

## 4. Хранение

Минимальные сущности:

| Таблица/набор | Содержимое |
|---|---|
| users/sessions | Идентификаторы, сессии, отзыв доступа |
| content_releases | Совместимые версии, manifest hashes, статус публикации |
| rooms | Состояние lifecycle, current revision, настройки и закреплённые версии |
| room_players | Слоты/раса, права, привязка пользователя |
| room_snapshots | Текущий и контрольные snapshots, schema, checksum |
| room_commands/events | Принятые команды и результаты по revision |
| command_receipts | Idempotency key, payload hash, outcome |

Сессия/race slot не определяется доверенным client JSON. Public catalog не читает
private snapshot. Большие masters и публичные packs находятся в object storage,
не в строках БД и не в памяти web API. Начальные маленькие snapshots можно хранить
в PostgreSQL, чтобы commit команды был атомарным.

При ошибке commit игра не подтверждает команду. При рестарте загружается последний
committed snapshot; пропущенный receipt восстанавливается по БД. Если выбран append-only
режим с редкими snapshots, процедура replay проверяется crash-тестом прежде включения.
Компрессию и частоту checkpoints выбирать по CPU/IO-измерениям.

Резервирование: БД с восстановлением на момент времени, versioning/private object backups,
отдельная копия исходных ресурсов и fixtures. Реплика без истории не заменяет backup.
Проверка восстановления входит в приёмку, а не только проверка наличия backup-файла.

## 5. Публикация и CDN

Build генерирует полный release manifest с хешами всех объектов. Порядок публикации:

1. Загрузить новые immutable объекты, не заменяя старые URL.
2. Проверить наличие, полные хеши, MIME, CORS, распаковку и соответствие public/private.
3. Прогнать smoke новой версии со staging URL.
4. Атомарно переключить указатель текущего release.
5. Существующим партиям оставить их закреплённый release.

Настройки по типам:

- Wasm: `application/wasm`, Brotli/gzip как согласованное HTTP encoding.
- HTML и mutable catalog/release pointer: короткий TTL или revalidation.
- Hash-URL объектов: длительный immutable cache.
- Видео: собственный MIME, Range, без повторного общего сжатия.
- Private ответы/сейвы: не кэшируются публичным CDN.
- CORS между сайтом/CDN задан явно; ресурсы JS/WebGL/media не должны превращаться
  в opaque responses, недоступные проверке и нужным browser API.

Сжатые variants имеют корректный `Vary: Accept-Encoding`; CDN не отдаёт Brotli клиенту,
ожидающему несжатые bytes. HTTP cache semantics:
[MDN HTTP caching](https://developer.mozilla.org/en-US/docs/Web/HTTP/Guides/Caching).

Первая работающая версия не требует service worker. Он добавляется для offline-start
после устойчивого version pinning: новый worker не должен насильно подменять runtime
открытой партии или удалять используемые старые packs. Rollback переключает release
pointer; миграции схемы БД должны оставаться совместимыми с rollback-окном.

## 6. Проверка команд и соединений

HTTPS/WSS обязательно. Авторизация через серверную сессию: защищённая HttpOnly-cookie
для same-origin deployment, с явно выбранным SameSite; WebSocket upgrade проверяет
Origin и членство в комнате. Изменяющие HTTP-запросы имеют CSRF-защиту. Не хранить
долгоживущий секрет в URL query/log или публикуемых content manifests.

На соединение и пользователя задаются message-size, command-rate, outbound queue и
idle limits. Проверяются диапазоны полей, владение юнитом, ход, phase, resource costs
и доступность цели. Парсер сценария/сейва ограничивает размер распаковки, число сущностей,
глубину, размеры изображений и ссылки; data-only мод не означает неограниченное доверие.

Никнейм показывается как текст, не HTML. Private state и auth values не попадают в
telemetry. Public replays не выдаются автоматически: полный журнал может раскрывать
текущие скрытые сведения. Для диагностического доступа задаются отдельные права.

## 7. Формулы ёмкости вместо обещаний

Оценка среднего CPU:

`cores ≈ rooms × commands_per_room_second × mean_CPU_seconds_per_command`.

Добавить сериализацию, projection, сохранение и background work. Пример только для
понимания: 1 000 комнат × 0,1 команды/с × 0,005 CPU-с/команду = 0,5 ядра на сам расчёт.
Если end-turn/ИИ стоит 0,5 CPU-с, результат уже 50 ядер при том же темпе. Среднее не
описывает одновременные окончания ходов и хвост latency; нужны burst/p95/p99-тесты.

Память:

`RAM ≈ service + shared_rules + active_rooms × measured_room_bytes + connections + queues + DB/cache`.

Место в snapshots и память World — разные величины. Не выводить room_bytes из числа
игроков; большая карта, история событий, AI caches и subscriber queues могут доминировать.
Держать резерв под peaks и allocator fragmentation; Rust не устраняет их.

Трафик контента:

`CDN egress ≈ cold_downloads × required_encoded_bytes + cache_miss_updates`.

Пример: 1 000 новых игроков × 100 МБ = 100 ГБ, вне зависимости от того, написан API
на Rust или C#. CDN снимает выдачу с origin, но не делает исходящий трафик бесплатным.
Для origin действует его доля cache misses. WS-трафик считается отдельно по player-view deltas.

Начальный лабораторный стенд можно ограничить 2 vCPU/4 ГиБ для app и отдельно учитывать
БД. Это **тестовая точка**, не минимальные требования и не обещание числа игроков.
Указывать модель CPU, лимиты контейнера, расположение DB и характеристики storage.

## 8. Наблюдаемость и рост

Метрики: подключённые люди, live/sleeping rooms, command queue depth, CPU-time по типу
команды, AI turn p95/p99, commit latency, resync rate, outbound bytes, память на комнату,
CDN hit ratio, cold bytes/session, errors по client build.

Alert: длительное ожидание хода, ошибки сохранения, перегрузка compute pool, рост очередей,
невозможность скачать critical pack, несовместимые версии. Correlation — room/revision/
command_id, без секретного payload в обычных логах.

Горизонтальное масштабирование вводить при измеренной потребности. Потребуются routing
по room, один владелец комнаты, lease/fencing и транзакционная защита revision. Просто
поставить два процесса за балансировщиком недостаточно: они могут применить один ход дважды.
Сначала документировать migration/drain комнат и восстановление владельца после сбоя.
