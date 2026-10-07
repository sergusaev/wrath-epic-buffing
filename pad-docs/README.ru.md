# Buff It 2 The Limit (Groups): меню групп баффов

[English version](README.md)

Форк [Buff It 2 The Limit](https://github.com/Gh05d/wrath-epic-buffing) 1.21.2 (MIT; авторы Vek17, factubsio, Gh05d) добавляет меню групп баффов для Pathfinder: Wrath of the Righteous и Pathfinder: Kingmaker. В консольном интерфейсе им целиком управляют с геймпада — PC-экрана книги заклинаний оригинала там нет, — в PC-интерфейсе с клавиатуры и мышью (см. [Управление](#управление)).

## Установка

1. Установить Unity Mod Manager для игры.
2. Собрать мод (см. [Сборка](#сборка)) или взять релиз.
3. WotR: положить `BuffIt2TheLimit.dll` и `Info.json` в `<игра>/Mods/BuffIt2TheLimit/` (в справке для Steam Deck — `mods`; Proton регистр не различает). В UMM мод называется «Buff It 2 The Limit (Groups)». Он заменяет оригинальный мод: у них один id и одни файлы настроек. BubbleBuffs он тоже заменяет, но его настройки (`bubblebuff-*.json`) не читает.
4. Kingmaker: положить `PadBuffsKingmaker.dll` и `Info.json` в `<игра>/Mods/PadBuffsKingmaker/`.

## Открытие меню

Меню открывается клавишей меню из настроек мода:

- **Kingmaker:** по умолчанию F7 (записывается в новый файл настроек).
- **WotR:** клавиша, которую оригинальный мод называет «открыть меню баффов». Задать её в PC-экране книги заклинаний в режиме мыши и клавиатуры или записать в файл настроек (`OpenBuffMenuKey`, см. [Файлы](#файлы)); в [справке по Steam Deck](https://github.com/sergusaev/pathfinder-mods/blob/main/docs/steam-deck.ru.md#3-установка-модов) есть готовая команда, которая ставит F7.

**Steam Deck с нуля** — Unity Mod Manager для обеих игр, `startup.json`, вся раскладка Steam Input (окно UMM на R4, это меню на L5, мышь на правом трекпаде): [pathfinder-mods/docs/steam-deck.ru.md](https://github.com/sergusaev/pathfinder-mods/blob/main/docs/steam-deck.ru.md).

F-клавиш на геймпаде нет, поэтому клавишу назначают на свободную кнопку в Steam Input. На Steam Deck: Настройки контроллера → Изменить раскладку → Задние кнопки → L5 → клавиша F7, обычное одиночное нажатие без задержек. Жесты распознаёт сам мод (`PadGestures.cs`):

| Кнопка меню | Действие |
|---|---|
| короткое нажатие | открыть или закрыть меню (срабатывает через 0,35 с: мод ждёт, не будет ли второго нажатия) |
| удержание 0,6 с | наложить встроенную группу Long («10 мин/ур и дольше») |
| двойное нажатие | наложить встроенную группу Important («Раунды») |

Результат каждого каста — жестом или из меню — всплывает вверху экрана на 5 с с причинами неудач и пишется в журнал боя игры. Переключаемые способности считаются как касты: включена — наложено, уже была включена — уже висело.

Почему жесты не в Steam Input: с привязкой Long Press Steam шлёт ещё и клавишу обычного нажатия, а Double Press до игры не доходил. Сочетаний с Shift избегать: при коротком нажатии Steam Input не досылает клавишу с задержкой старта, приходит только Shift.

## Управление

**Главная страница**

| Кнопка | Действие |
|---|---|
| ↑↓ (крестовина или левый стик, удержание повторяет) | выбор строки |
| A на группе | наложить группу |
| Y на группе | состав группы |
| A на «Каст в бою» | вкл/выкл каст в бою |
| A на «Группы» | список групп |
| A на «Цели баффов» | редактор целей |
| A на «Справка» | подробная справка с условным примером; вверх/вниз — прокрутка, LB/RB — по разделам |
| X | наложить все показанные группы по очереди |
| B | закрыть |

В строке группы: сколько баффов включено, сколько из нужных висит (жёлтый — часть слетела, зелёный — всё на месте), через сколько истечёт ближайший, сколько выключено галочкой. После каста — сколько наложено и сколько уже висело, неудачи с причинами.

**Группы**

| Кнопка | Действие |
|---|---|
| A на группе | состав группы |
| A на «+ Новая группа» | создать (откроется страница названия) |
| Y | переименовать и сменить длительность |
| X | показать или скрыть в главном меню |
| RB дважды за 3 с | удалить свою группу; встроенные удалить нельзя, подсказки RB у них нет |
| B | назад |

**Состав группы**

| Кнопка | Действие |
|---|---|
| ↑↓ | бафф: сверху баффы группы с галочками, ниже серым остальные, подходящие по длительности первыми |
| A | добавить в группу или убрать из неё |
| X | галочка: выключить или включить бафф только в этой группе, цели сохраняются |
| Y | подбор по длительности: все баффы партии с длительностью группы, добавляются выключенными |
| → | цели выбранного баффа (редактор целей) |
| B | назад |

Колонки: имя, длительность (раунды / минуты / 10 мин / часы / перекл.), кому (на себя / на партию / 1 исполнитель / нет целей), цели «стоит/можно».

**Название группы** — экранная клавиатура: крестовина выбирает клавишу, A вводит, X стирает, Y — готово, LB/RB меняют длительность, B — отмена. Особые клавиши: «Aa» (заглавная), «EN/RU» (раскладка), «Пробел». Работает и клавиатура Steam (Steam + X): мод читает `Input.inputString`. Пустое название — группа называется по длительности.

**Цели баффов**

| Кнопка | Действие |
|---|---|
| ↑↓ | бафф (удержание повторяет: 0,35 с до первого повтора, дальше каждые 0,08 с) |
| ←→ | персонаж в полоске партии |
| A | вкл/выкл цель у выбранного персонажа |
| Y | вся партия (если все уже отмечены — снять всех) |
| X | автоматические цели (сам / партия) |
| LB / RB | вкладки: Назначенные, Все, затем каждая группа |
| B | назад (в состав группы, если пришли оттуда) |

Полоска партии: зелёный — цель отмечена; золотой — курсор; зачёркнутое красноватое имя — на этого персонажа бафф не наложить.

**Мышь** — в меню работает при любом режиме подсказок (на Steam Deck — правый трекпад); щелчки по меню до игры не доходят.

| Мышь | Действие |
|---|---|
| наведение | подсветка строки, клавиши или подсказки; выделение не меняется |
| ЛКМ по строке | выделить; повторный щелчок по выделенной — как A (первый щелчок по группе в главном меню её только выделяет) |
| ЛКМ сразу | пункты главного меню, кроме групп; «+ Новая группа»; галочка баффа; персонаж в полоске партии; соседние вкладки целей; клавиши экранной клавиатуры; стрелки ‹ › значения в настройках (щелчок по значению — вперёд) |
| ПКМ где угодно | назад, как B; в главном меню — закрыть; при назначении клавиши — отмена |
| колесо | прокрутка состава группы, целей баффов, настроек и справки на три строки, выделение едет со списком |
| ЛКМ по подсказке внизу | то же, что её кнопка или клавиша; у пары LB/RB — по значку нужной стороны; подсказки направлений не нажимаются |

Каждая правка сразу сохраняется в файл настроек.

## Группы

- **Встроенные** (`BuffGroup.Long/Quick/Important`) названы по длительности: «10 мин/ур и дольше», «Минуты (мин/ур)», «Раунды». Удержание кнопки меню кастует Long, двойное нажатие — Important. Их можно переименовать, сменить длительность и скрыть.
- **Свои** — слоты `Custom1…Custom12` в том же enum `BuffGroup` (`BubbleBuffer.cs`), поэтому каст, отчёты и сохранение работают без отдельной ветки кода. Описание групп — `SavedBufferState.Groups` (`SavedGroup`: `Id`, `Name` — `null` значит «по длительности», `Duration`, `Hidden`).
- **Членство** — как в оригинале, `InGroups` у баффа; бафф в группе, если он там указан и у него есть цели. У любого баффа изначально `InGroups = {Long}`, поэтому цель, поставленная баффу вне групп, кладёт его в Long.
- **Галочка** — `DisabledIn` у баффа и в `SavedBuffState`. Кастуется только `BubbleBuff.ActiveIn(group)`: в группе, не выключен, есть цели (`BuffExecutor.Execute`, приоритет слотов в `Recalculate`, подсказка группы).
- **Цели общие** для баффа во всех группах. Добавление баффа без целей ставит их автоматически (`PadGroups.AutoTarget`): все в партии, на кого `CanTarget`; у баффа «только на себя» это ровно его кастеры; песням — один исполнитель. Удаление из последней группы и снятие последней цели сбрасывают цели и выводят бафф из всех групп.
- **Длительность баффа** — `AbilityCombinedEffects.Duration`: максимум `DurationRate` по эффектам (`IBeneficialEffect.cs`, `ExtentionMethods.Duration`); `Permanent` и сутки считаются часами, активируемые — переключаемыми. Без точной длительности — по старому `IsLong`.
- **Подбор по длительности** (`PadGroups.AutoFill`) берёт видимые баффы с кастерами, подходящие по длительности (`PadGroups.Fits`; «10 мин/ур и дольше» = 10 мин плюс часы), и добавляет их выключенными.
- Удаление своей группы чистит её и в сохранённых баффах персонажей, которых сейчас нет в партии.

## Файлы

- **Настройки**, отдельный файл на каждое прохождение: `<папка мода>/UserSettings/bi2tl-<GameId>.json`, GameId берётся из `header.json` сохранения, без дефисов.
  - `Wanted` — UniqueId целей; `InGroups` — группы.
  - `Version` обязательно `1`, иначе миграция сотрёт цели.
  - Клавиши: `OpenBuffMenuKey`, `ShortcutKeys` (`Long`, `Quick`, `Important`), каждая вида `{"Key": "F7", "Ctrl": false, "Shift": false, "Alt": false}`.
  - Править файл только при закрытой игре: мод сохраняет поверх.
- **Лог** (строки с меткой `[PAD]`):
  - WotR через Proton: `steamapps/compatdata/1184370/pfx/drive_c/users/steamuser/AppData/LocalLow/Owlcat Games/Pathfinder Wrath Of The Righteous/Player.log`; на Windows `%USERPROFILE%\AppData\LocalLow\Owlcat Games\Pathfinder Wrath Of The Righteous\Player.log`.
  - Kingmaker на Linux: `~/.config/unity3d/Owlcat Games/Pathfinder Kingmaker/Player.log` (ещё `[PadBuffsKingmaker]`).
- **WotR в режиме геймпада на Steam Deck:** без `startup.json` с `{"ForceControllerMode":"gamepad"}` в папке игры WotR на любое нажатие клавиши предлагает перейти на клавиатуру, в том числе на клавишу меню из Steam Input.

## Kingmaker

То же меню (группы, галочки, автоцели, справка, жесты) собирается для Kingmaker отдельным проектом `BuffIt2TheLimit.Kingmaker/`. Он компилирует общие файлы из `BuffIt2TheLimit/` с символом `KINGMAKER`; PC-экран книги заклинаний из WotR (`BubbleBuffer.cs`, `UIHelpers.cs`, `Main.cs`) в эту сборку не входит.

- **Режим мыши и режим геймпада.** В режиме геймпада меню забирает кнопки через консольный слой ввода игры; в режиме мыши такого слоя нет, кнопки геймпада опрашиваются напрямую, работает и клавиатура (стрелки, Enter, Backspace). Мышь работает в обоих режимах: компонент `PadPointer` на строках и подсказках, `PadWheel` на холсте меню — он же не даёт игре менять масштаб колесом (Kingmaker не масштабирует, пока под курсором есть обработчик прокрутки).
- **Раскладка Steam Input.** Kingmaker хранит раскладку в Steam Cloud; шаги из [Открытие меню](#открытие-меню) работают так же.

**Что работает иначе, чем в WotR**

| Часть | Kingmaker |
|---|---|
| Источники баффов | заклинания из книг, классовые способности, переключаемые, песни. Свитки, зелья, жезлы и предметы не сканируются |
| Каст | `KmExecutionEngine`: проверка цели → `RuleCastSpell` → ячейка тратится сразу после правила (в Kingmaker нет хука «перед срабатыванием»). Ячейка тратится и при провале заклинания, как в игре |
| Нет в игре | Shifter's Fury, ездовые животные, Arcanist (резервуар, Share Transmutation), Magic Deceiver, Azata Zippy Magic, мифические уровни, жезлы удлинения |
| Питомцы | один питомец у юнита (`Descriptor.Pet`), резервной партии нет |
| Журнал боя | результат пишет сам мод (`KmCombatLog`): в консольный журнал в режиме геймпада, в журнал PC-интерфейса в режиме мыши |

**Где различия в коде**

- `KmHost.cs` — точка входа UMM, `GlobalBubbleBuffer`, хранение настроек, подписки на события (загрузка зоны, смена партии, начало боя), заглушка результатов каста.
- `KmExecutionEngine.cs` — каст.
- `KmCompat.cs` — `PetType`, `GetPet`, псевдоним `SimpleBlueprint` → `BlueprintScriptableObject`.
- GUID блупринта в WotR — структура, в Kingmaker — строка; общий код вызывает `blueprint.Gid()` (WotR — `CoreTypes.cs`, Kingmaker — `KmHost.cs`).
- Общие типы (`BuffGroup`, `Bubble`, `AbilityCache`, `RoundLimitHandler` …) вынесены из `BubbleBuffer.cs` в `CoreTypes.cs`.
- Остальное — блоки `#if KINGMAKER` / `#if !KINGMAKER` в общих файлах.

## Что меняет форк

- `PadQuickMenu.cs` — окно: главная, группы, состав, название (экранная клавиатура), цели баффов, справка.
  - Собственный overlay-канвас: PC-канвас игры в режиме геймпада скрыт.
  - Кнопки идут через консольный слой ввода игры (`GamePad.Instance.PushLayer`), поэтому, пока окно открыто, игра на них не реагирует.
  - Направления опрашиваются из `GamePad.Instance.Player` (Rewired) с автоповтором.
  - Подсказки внизу каждой страницы и стрелки вкладок — иконки кнопок самой игры: `GamePadIcons.Instance.GetIcon(RewiredActionType)`, сборка в `MakeHintBar`/`MakeIcon`. Без иконки показывается текст `[A]`. Стрелок ↑↓←→ в шрифте игры нет.
- `PadGroups.cs` — модель групп: свои группы, названия по длительности, галочки, автоцели (сам/партия/песня), подбор по длительности.
- `PadHelp.cs` — разметка справки и иконки кнопок в тексте. Текст — ключ `pad.help.body` (в Kingmaker к нему добавляется `pad.help.km`); разметка: `#` раздел, `##` подраздел, `- ` пункт, `1. ` шаг, `> ` совет, `{A}| текст` строка кнопки, `**жирный**`. Токены `{A} {B} {X} {Y} {LB} {RB} {UP} {DOWN} {LEFT} {RIGHT}` становятся спрайтами TMP-ассета игры (`<sprite name="<префикс><действие>">`, префикс берётся из `ConsoleBindingTemplate`: `Steam_`, `XBox_`, `PS4_`), без ассета — буквами в скобках; `{L5}` — значок кнопки меню, `{G1} {G2} {G3}` — имена встроенных групп. Найденный ассет пишется в лог строкой `[PAD] icons:`. Пример в справке условный (Воин, Жрец, Волшебница, Бард, Плут) и от партии игрока не зависит.
- `PadGestures.cs` — жесты кнопки меню и всплывающее уведомление `PadToast`.
- `CoreTypes.cs` — общие типы, вынесенные из `BubbleBuffer.cs` для сборки под Kingmaker.
- `BuffExecutor.cs`: событие `RoutineFinished` с результатом каста; счётчики `ScheduledRoutines` и `FinishedRoutines` (без второго, когда кастовать нечего, поверх результата на 30 с вставало «накладываю…»); в режиме геймпада клавиша меню открывает новое окно; нажатия F6/F7/F9 пишутся в лог.
- `InstantExecutionEngine.cs` — паузы между пачками кастов в реальном времени (`WaitForSecondsRealtime`), иначе на паузе игры каст «висит».
- `BubbleBuffer.cs`: `Awake` контроллера книги заклинаний больше не падает в режиме геймпада, где книги нет (в оригинале `NullReferenceException` в `TryFixEILayout`).
- `IBeneficialEffect.cs`: `OwnBuffGuids` для оставшегося времени; `Duration` — класс длительности баффа.
- `SaveState.cs`: `Groups` (свои группы) и `DisabledIn` (галочки) в файле настроек.
- `Info.json`: имя «Buff It 2 The Limit (Groups)» (до 1.21.2-pad.3 и kingmaker-v0.2.0 — «(Pad)»; Id и папки `BuffIt2TheLimit` и `PadBuffsKingmaker` прежние, обновление ставится поверх), страница форка, без репозитория обновлений (лента обновлений оригинала заменила бы форк).

## Сборка

### Сторонние программы

| Программа | Зачем | macOS | Linux | Windows |
|---|---|---|---|---|
| .NET SDK 8 или новее | сборка обоих проектов | `curl -sSL https://dot.net/v1/dotnet-install.sh \| bash -s -- --channel 8.0` (в `~/.dotnet`) или `brew install --cask dotnet-sdk` | тот же скрипт или пакет дистрибутива `dotnet-sdk-8.0` | `winget install Microsoft.DotNet.SDK.8` |
| Bash, `ssh`, `md5sum`/`md5` | `deploy.sh`, `release.sh` | встроены | встроены | Git for Windows: `winget install Git.Git`, скрипты запускать из **Git Bash** |
| `zip` или bsdtar | упаковка zip для релиза | встроен (`tar`) | `sudo apt install zip` | `C:\Windows\System32\tar.exe`, используется сам |
| GitHub CLI `gh` | только `release.sh --publish` | `brew install gh` | [cli.github.com](https://cli.github.com/) | `winget install GitHub.cli` |

`deploy.sh` и `release.sh` берут `dotnet` из `PATH`, затем из `~/.dotnet`; переопределяется `DOTNET=/путь/к/dotnet`. После установки `gh` один раз войти: `gh auth login`.

**Пакеты NuGet** `dotnet build` скачивает сам при первой сборке (нужен интернет): `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 (эталонные сборки .NET Framework 4.8 / 4.8.1, поэтому targeting pack Windows не нужен) и `BepInEx.AssemblyPublicizer.MSBuild` 0.4.2 (открывает приватные члены игры при компиляции).

**Сборки игры** (вне git, никогда не публиковать):

| Проект | Папка | Что туда скопировать |
|---|---|---|
| WotR | `GameInstall/Wrath_Data/Managed/` | все DLL из `<WotR>/Wrath_Data/Managed` и `UnityModManager/` с `UnityModManager.dll` и `0Harmony.dll` |
| Kingmaker | `GameInstallKM/Kingmaker_Data/Managed/` | все DLL из `<Kingmaker>/Kingmaker_Data/Managed` и `UnityModManager/` с `UnityModManager.dll` и `0Harmony.dll` |

Например, со Steam Deck:

```bash
mkdir -p GameInstall/Wrath_Data GameInstallKM/Kingmaker_Data
scp -r "deck@steamdeck.local:/home/deck/.local/share/Steam/steamapps/common/Pathfinder Second Adventure/Wrath_Data/Managed" GameInstall/Wrath_Data/
scp -r "deck@steamdeck.local:/home/deck/.local/share/Steam/steamapps/common/Pathfinder Kingmaker/Kingmaker_Data/Managed" GameInstallKM/Kingmaker_Data/
```

`GamePath.props` в корне репозитория говорит проекту WotR, где игра. На Windows с установленной WotR первая сборка пишет его сама по `Player.log` игры; в остальных случаях создать вручную (с абсолютным путём):

```xml
<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
	<PropertyGroup>
		<WrathInstallDir>/абсолютный/путь/к/wotr-pad-buffs/GameInstall</WrathInstallDir>
	</PropertyGroup>
</Project>
```

`GamePath.props` не хранится в git: скопированный с другой машины (вместе с рабочей копией или папками `GameInstall*`) указывает на путь той машины, и сборка падает без сборок игры. После копирования поправьте `WrathInstallDir`.

На Windows один раз выполнить в клоне `git config core.filemode false`: в Windows нет признака исполняемого файла, и без этого git показывает все `.sh` изменёнными. При копировании `.git` с другой машины возвращается старое значение.

Проект Kingmaker берёт `GameInstallKM/`, если в `GamePath.props` не задан `KingmakerInstallDir`. После удачной сборки WotR проект ещё копирует мод в `$(WrathInstallDir)/Mods/` — с `GameInstall/` это безвредно.

### Команды

Собирать Release: в Debug включены отладочные клавиши Shift+I/B/R.

```bash
dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -c Release -p:SolutionDir=$(pwd)/
dotnet build BuffIt2TheLimit.Kingmaker/BuffIt2TheLimit.Kingmaker.csproj -c Release -p:SolutionDir=$(pwd)/
```

Проект Kingmaker собирается под `net48`: Harmony 2.3.6 из UMM для Kingmaker собран под 4.8.

`deploy.sh` собирает Release и ставит его на Steam Deck по SSH (игра должна быть закрыта), затем сверяет контрольные суммы:

```bash
DECK=deck@steamdeck.local ./deploy.sh              # WotR
DECK=deck@steamdeck.local ./deploy.sh --kingmaker  # Kingmaker
DECK=deck@steamdeck.local ./deploy.sh --bind-menu  # WotR, плюс меню = F7, Long = F6, Important = F9 во всех файлах настроек
```

`WOTR_DIR` и `KINGMAKER_DIR` переопределяют папки игр на деке. Новые строки добавлять во все пять `Config/*.json`; без ключа в `en_GB.json` игра падает.

### Выпуск релиза

```bash
./release.sh wotr                  # собрать Release и упаковать dist/BuffIt2TheLimit-Pad-<версия>-pad.<N>.zip
./release.sh kingmaker --publish   # упаковать dist/PadBuffsKingmaker-<версия>.zip, тег kingmaker-v<версия>, публикация
```

Релизы WotR нумеруются `v<версия оригинала>-pad.<N>`, где N — следующий невыпущенный номер; версия Kingmaker берётся из `BuffIt2TheLimit.Kingmaker/Info.json`, перед публикацией её нужно поднять. Для `--publish` нужны [GitHub CLI](https://cli.github.com/), чистое дерево и выложенная `gamepad`; `--notes FILE` заменяет стандартное описание. Папка `dist/` в git не попадает.

## Грабли, найденные по пути

- **L3+R3 занято:** в WotR это окно отчёта об ошибке.
- **Имена задних кнопок Steam Deck** в файле раскладки: L4 = `button_back_left_upper`, L5 = `button_back_left`, R4 = `button_back_right_upper`, R5 = `button_back_right`. Варианта `…_lower` нет, Steam такой блок молча пропускает.
- **Список баффов** в режиме геймпада не строится, пока не открыта книга заклинаний, поэтому меню при открытии вызывает `state.Recalculate(false)`.
- **Оставшееся время** берётся из публичных `Buff.TimeLeft` и `Buff.IsPermanent`.

## Поддержка форка

- `master` повторяет [Gh05d/wrath-epic-buffing](https://github.com/Gh05d/wrath-epic-buffing) и своих коммитов не получает.
- `gamepad` (ветка по умолчанию) — изменения форка поверх него.

Подтягивание новых версий оригинала — слиянием, а не перебазированием, чтобы опубликованная история не переписывалась:

```bash
git remote add upstream https://github.com/Gh05d/wrath-epic-buffing   # один раз
git fetch upstream
git switch master && git merge --ff-only upstream/master && git push origin master
git switch gamepad && git merge master && git push origin gamepad
```

Первый шаг делает и кнопка «Sync fork» на GitHub в ветке `master`. План развития — в [PLAN.md](PLAN.md).
