# Скриншоты README: где лежат и как обновлять

Решение пользователя Р32 (22.10.2026): на странице проекта — скриншоты и сравнения «оригинал / Last Edict», но **вне истории git**:
на них графика оригинала, а в репозитории её нет ни байта (Р3). Картинки — файлы выпуска GitHub с тегом `media`
(`https://github.com/Betreazen/last-edict/releases/download/media/<файл>`); README ссылается на них. Замена файла выпуска не трогает
git, старые снимки удаляются без переписывания истории.

## Когда снимать

Периодически, пока идёт работа над своей версией, — **после каждого заметного изменения вида или поведения** (новое окно, полоса,
широкий экран, новая механика в бою) и в конце каждого этапа. Каждый раз:

1. снять заново то, что изменилось (и, если нужно, новый экран);
2. для отличия от оригинала — пара «Disciples II / Last Edict» одним изображением (как `compare-battle-queue.png`);
3. обновить файлы выпуска `media`;
4. в README: строку в «Progress» (дата, что изменилось, ссылка на картинку), статус в таблице «How it differs from the original»
   (✅ / 🚧 / 📋), при новом экране — ячейку в «Gallery».

## Как снимать

`py tools/publish/screenshots.py --out <временный каталог> [--only <имя>]` — игра открывает окно 800×600 (снимка без окна у Godot нет),
проходит по сценарию и кладёт PNG и уменьшенный `*-small.png` (≤ 200 КБ). Нужны импортированные данные (`D2_ASSETS_DIR` — графика) и
сохранения, по которым идут сцены: сейчас это сохранения корпуса эталона (`D2_CORPUS_DIR=../disciples-godot/corpus`); в git их нет.
Снимки — только во временный каталог, **не в репозиторий**.

Снимки оригинала для сравнения — экраны, снятые в самой игре (F12 под зондом эталона, `../disciples-godot/tools/shot_pairs.py`
раскладывает их по экранам в `.cache/shots`), или кадр «было» сборкой начала этапа: `git worktree add ../le-before <коммит>`, туда —
`cp -rn data/. ../le-before/data/` (без замены файлов коммита; новые поля содержимого, которых та сборка не знает, — убрать), затем
`screenshots.py --repo ../le-before --only <имя>`; рабочую копию потом убрать `py tools/wt_remove.py ../le-before` (без `--branch`).
Широкие снимки (`wide-map`, `settings`, `controls`, `gamepad`, `options-old` — окно оригинала для пары) — 1280×720 в режиме «чёткий».
Пара склеивается рядом с подписями: `py tools/publish/compare.py <было> <стало> --left … --right … --out <каталог>/compare-<имя>.png`
(EB Garamond из `data/fonts`, рядом `-small.png` ≤ 200 КБ).

Сейчас в выпуске: `main-menu`, `world-map`, `battle`, `battle-queue`, `city`, `spellbook`, `hero`, `diplomacy` (каждый — полный и
`-small`), `compare-battle-queue` (+ `-small`); с Э5 — `compare-widescreen`, `compare-settings`, `controls`, `gamepad` (каждый + `-small`).

## Как выложить

```bash
gh release create media --title "Media for README" --notes "Screenshots for the README, outside the git history." --prerelease <файлы>   # первый раз
gh release upload media <файлы> --clobber                                                                                                  # дальше
```

Обновлять выпуск `media` по ходу работы пользователь разрешил (22.10); если разрешения Claude Code не пускают — дать пользователю
готовую команду.
