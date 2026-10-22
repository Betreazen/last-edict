# Проверки и предварительные бюджеты

Статус: план измерений. Ниже **цели для прототипа**, не результаты текущей игры.

## 1. Условия измерения

Для каждого отчёта записывать commit, toolchain, release/debug, browser/version,
ОС/CPU/GPU/RAM, viewport/backing resolution, power mode, состояние кэша и сети.
Не сравнивать C# desktop Debug с Rust Wasm Release и не называть разницу эффектом языка.

Сравнения:

- Native Rust core ↔ C# reference: корректность; вычисления — на одинаковых inputs и release builds.
- Rust native ↔ Rust Wasm: семантика и цена браузерной платформы.
- Browser baseline ↔ оптимизация: тот же renderer, сценарий и контент, меняется один фактор.
- Полный end-to-end: главный критерий продукта, включая сеть, decode/upload, UI и storage.

Для loading tests — минимум 10 cold/warm прогонов с median/p95 и исходными samples;
для frame time — минутные записи активных сцен; для server capacity — продолжительный
steady state плюс bursts. На раннем прототипе меньший набор помечается как предварительный.

## 2. Начальные бюджеты

Desktop-browser профиль: реальное устройство с интегрированной графикой, 8 ГиБ RAM,
1280×720 backing canvas; точную модель выбрать на P1. Сеть теста: 20 Мбит/с, RTT 100 мс;
отдельно медленная сеть 5 Мбит/с и обрывы. Числа ниже пересматриваются по результатам G1/G2.

| Метрика | Предварительная цель | Метод |
|---|---:|---|
| Critical bytes до интерактивного меню | ≤10 МиБ encoded | Network, включая Wasm/JS/fonts/UI |
| Интерактивное меню, cold | p95 ≤8 с на заданной сети | performance marks от navigation |
| Интерактивное меню, warm | p95 ≤2 с | Повторный вход с подтверждённым cache hit |
| Critical bytes до первой карты тестового сценария | ≤40 МиБ суммарно | Не полный объём библиотеки/кампаний |
| Cold до готовой карты | p95 ≤25 с на заданной сети | Автоматический быстрый проход меню |
| Основной UI input response | p95 ≤100 мс | Ввод во время fetch и хода ИИ |
| Активное рисование | целевой cap 60, поддержанный cap 30 | Реальная прокрутка и бой |
| CPU+подготовка кадра без ожидания vsync | p95 ≤12 мс при 60; ≤25 мс при 30 | Browser performance trace |
| Длинные блокировки main thread | нет >50 мс в steady play | Отдельно от cold startup/смены сцены |
| Суммарный пик памяти вкладки | начальная цель ≤512 МиБ | Раздельно process/Wasm/JS/estimated GPU |
| Активные RGBA-текстуры | ориентир ≤128 МиБ | Учёт размеров, формата и mipmaps |
| Server обычная команда, без сети | p95 ≤100 мс при заявленной нагрузке | Включает queue, rules, commit, projection |
| Восстановление view при reconnect | p95 ≤2 с при готовом контенте | Сеть та же; размер snapshot отдельно |

25 секунд — технический предел полного cold path для тестового сценария, а не желаемая
пауза после кнопки. Предзагрузка во время выбора уменьшает видимое ожидание, но тест
не рассчитывает на то, что человек специально долго читает брифинг.
На 5 Мбит/с одни 40 МиБ требуют около 67 секунд передачи без overhead: интерфейс
должен честно показывать прогресс, невозможно исправить эту границу одним Rust.

AI end-turn имеет отдельный бюджет после P3 и worst-case карты; не маскировать его
средним временем клика. Длительное вычисление допускает прогресс/yield, UI остаётся живым.
GPU-тайминг и общая память браузера не везде доступны точно: отчёт указывает оценку,
инструмент и ограничения, а не складывает несовместимые цифры как точное значение.

## 3. Матрица функциональной приёмки

| Область | Обязательные проверки |
|---|---|
| Core | Формулы, иммунитеты, modifiers, tie-break, RNG, overflow, AI, экономика |
| Партия | Start → move → battle → aftermath → end-turn → AI → victory/defeat |
| Сейвы | Карта и активный бой, полный RNG, прошлые schema, повреждённый файл |
| UI | Все необходимые screens/states, hotkeys, right-click, scroll, IME, кириллица |
| Assets | Missing/cyclic dependencies, trim offsets, alpha, shadows, timeline events |
| Loading | Смена сценария/расы, cancel/retry, быстрый skip, cold/warm, P0 starvation |
| Cache | Quota отказ, eviction, corrupted object, offline, два client releases |
| Rendering | Порядок прозрачности, camera culling, high DPI, context loss, 30/60/120+ Hz |
| Media | Autoplay refusal, mute, seek/skip, AV-sync, codec fallback, loop points |
| Multiplayer | 2–5 людей + AI, фазы боя, reconnect, duplicate, slow client, crash |
| Deployment | Manifest-first/last порядок, rollback, pinned room, backup restore |

Матрица браузеров покрывает каждую область; на мобильном результат исследовательский,
пока платформа явно не принята как поддерживаемая. Максимальные карты и длинные партии
обязательны для проверок накопления памяти, а не только Tutorial.

## 4. Эквивалентность и visual tests

Эталон — зафиксированный G0, с перечнем известных отличий от оригинала. Differential
runner читает одни fixtures, получает нормализованные World/DomainEvents и сравнивает
поля и порядок. В hash-state исключаются wall clock, debug labels, cache, GPU и UI.
RNG trace сравнивается, чтобы одинаковый итог случайно не скрыл другое потребление генератора.

Existing tests переносить по смыслу, не по количеству. Generated parser и corpus fitting
не обязаны быть переписаны на Rust: tools могут выдавать portable fixtures, если это
уменьшает работу и оставляет воспроизводимость. Coverage связывает механику, corpus case
и новый тест. Новые ruleset tests не заменяют compatibility golden.

Visual checks: статические reference frames плюс короткие timelines карты/боя/видео.
Pixel equality обязательна для lossless reconstructed assets; итоговый GPU screenshot
может требовать ограниченный допуск из-за текстовых rasterizers/цвета, с явной причиной
и контролем anchor/слоёв. Для lossy media нужны метрики и визуальная/слуховая приёмка.

## 5. Проверка скрытой информации

Построить два World, различающихся только недоступными игроку объектами. Доступная
проекция, её snapshot/resync, ошибки, asset hints и обычная выдача catalog должны
совпасть там, где правила не раскрывают различие. Одинаковые сетевые размеры/тайминги
не обещаются без отдельной защиты побочных каналов; ограничение зафиксировать.

Проверить payload на каждом канале: HTTP, WSS, logs, HTML bootstrap, public packs,
browser cache, save export. Не использовать denylist «убрать поле position» из полной
модели: новые поля иначе начнут утекать. Проверить союзный обзор и потерю видимости.

## 6. Server load profiles

1. Idle: много комнат/WS без команд; цена соединения и heartbeat.
2. Active: реалистичная смесь move/hire/battle, транзакции БД включены.
3. AI burst: одновременные end-turn на больших картах, ограничение compute pool.
4. Reconnect storm: переподключение и player snapshots, а не только ping.
5. Slow reader: заполнение outbound queues, resync и memory bound.
6. Crash: завершение процесса до/после commit, одинаковый command_id после рестарта.
7. Storage degradation: медленная/недоступная БД, отсутствие ложных ACK.

Результат: выдерживаемое число active/idle rooms и connected players при фиксированных
p95/p99, memory, error rate. Указывать headroom для bursts. Максимум найденных TCP
соединений не равен числу работающих игровых партий.

## 7. Формат отчёта и критерий выпуска

Каждый отчёт содержит baseline, изменение, hardware/network, samples, median/p95/p99,
bytes/CPU/GPU/RAM, корректность и регрессии. Решение принимается по всему сценарию:
маленький codec, который увеличил загрузочные зависания, не считается победой автоматически.

Release gate: G0–G7 пройдены, открытых необъяснённых parity-ошибок нет, ограничения
платформ опубликованы, сохранения/rollback/восстановление испытаны. Качественные обещания
«не режет глаза» подтверждены playtest с прокруткой и короткими/длинными анимациями;
FPS счётчик сам по себе их не доказывает.
