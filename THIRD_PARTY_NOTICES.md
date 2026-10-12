# Источники, лицензии и внешние зависимости

## Новые материалы проекта

Новые исходники и проектные документы написаны для этого архива. Их лицензия —
MIT, текст в LICENSE. Алгоритмические требования получены из пользовательского
Graph A11y дополнения. Исходники сторонних accessibility mods не копировались
и не vendored в этот проект.

### Адаптированные инструкции Accessibility Mod Template

12 октября 2026 изучен предоставленный архив
`Accessibility-mod-template-Codex-edition-master.zip`, SHA-256
`68e3ee2e4096840ba2c06ecc08301acd43aef7a431c81993cce9c719bbd3d64f`.
Его README указывает исходный
[HappyStarfish/Accessibility-mod-template](https://github.com/HappyStarfish/Accessibility-mod-template)
и благодарит Plueschyoda. Copyright в LICENSE: 2026 Happy Starfish.
Точная ревизия исходного проекта и автор Codex-fork из архива не установлены.

Приложенный LICENSE сохранён без изменения в
[docs/licenses/accessibility-mod-template-CC-BY-4.0.txt](docs/licenses/accessibility-mod-template-CC-BY-4.0.txt).
Условия — [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/legalcode).
Перевод, сокращение и адаптация для Conquest сделаны в этом проекте;
описание изменений и исключений — в [разборе переноса](docs/research/template-import.md).

Адаптированные документы `docs/DEVELOPMENT_GUIDE.md`, `docs/MENU_ACCESSIBILITY.md`,
`docs/LOCALIZATION.md`, `docs/DIAGNOSTICS.md`, `docs/RELEASE_GUIDE.md`,
`docs/GIT_AND_GITHUB.md`, `docs/GAME_API.md`, `docs/PROJECT_STATUS.md` и
`CONTRIBUTING.md` сохраняют CC BY 4.0 и эту атрибуцию. Краткие дополнения из них
в AGENTS/README также сопровождаются ссылкой на источник. MIT собственных
исходников проекта не отменяет условия этих заимствований.
Кодовые templates, scripts и workflow архива в исходники мода не перенесены.

`docs/spec/graph-a11y-spec.original.md` — неизменённый пользовательский материал.
Его права и условия принадлежат исходному правообладателю; MIT проекта не
перелицензирует этот файл. Указанное в нём происхождение RTAccess/WrathAccess
сохранено в оригинале. Для публичного распространения проекта сохранить эту
атрибуцию и уточнить применимые к самому дополнению условия при необходимости.

## Изученные проекты

| Проект | Использовано | Условия исходников |
| --- | --- | --- |
| [Say the Spire](https://github.com/bradjrenshaw/say-the-spire) | UI buffers, бой, отдельные режимы выбора, опыт тестирования | Лицензия всего checkout здесь не устанавливается; код не включён |
| [WrathAccess](https://github.com/bradjrenshaw/wotr-access) | Параллельная навигация, видимость, targeting, стек экранов, диагностика | Репозиторий содержит zlib LICENSE; код не включён |
| [Stardew Access](https://github.com/stardew-access/stardew-access) | Tracker, объектное исследование, доступное движение | Основной проект GPL-3.0; код не включён |
| [Factorio Access](https://github.com/Factorio-Access/FactorioAccess) | Семантический интерфейс и обратимое управление | Не присваиваем неподтверждённую лицензию; код не включён |
| [Hearthstone Access DevTools](https://github.com/HearthstoneAccess/DevTools) | Обновление адаптации и работа с версиями/сигнатурами | Декомпиляция и patch sets не включены |

Подробный разбор с первичными источниками:
[reference-mods.md](docs/research/reference-mods.md).
Ссылка на проект не означает разрешения использовать любую его DLL или
перелицензировать заимствование. Если в будущем переносится код, записать
точный commit, его условия и attribution до включения.

## Зависимости будущего пользовательского пакета

- [BepInEx](https://github.com/BepInEx/BepInEx): внешний загрузчик. Условия
  проверяются для выбранного tag/release; лицензия master и исторической
  документации могут отличаться. DLL загрузчика в архив не входят.
- [NVDA Controller Client](https://github.com/nvaccess/nvda/tree/master/extras/controllerClient):
  использовать официальный matching-architecture build с его LGPL-2.1 terms
  и сопроводительными файлами. В этом архиве нет его бинарников.
- [Tolk](https://github.com/dkager/tolk): необязательный bridge, LGPL-3.0.
  В комплекте самого Tolk могут быть дополнительные компоненты со своими
  условиями. Их не заменяет лицензия Tolk. В этом архиве их нет.
- [Prism](https://github.com/ethindp/prism): рассмотрен как современная
  альтернатива, MPL-2.0. Код и DLL Prism не включены; текущий bridge его не вызывает.
- Windows SAPI предоставляется системой Windows; никакие системные DLL
  не распространяются вместе с проектом.
- .NET SDK/reference assemblies используются для сборки и тестов. В архиве
  нет установщика SDK или копий reference packages.

## Собственность игры

Tainted Grail: Conquest — продукт соответствующих правообладателей.
Этот проект не является официальным выпуском Questline/Awaken Realms.
Не распространяются exe, игровые DLL, assets, локализационные базы,
сохранения, декомпилированные исходники или обход проверки владения игрой.
Игровые ссылки в csproj указывают на локальные файлы и имеют Private=false.

Примеры договорённостей разработчиков других игр с авторами accessibility mods
не являются договорённостью для Conquest. Техническая возможность загрузить
плагин сама по себе не описывает позицию правообладателя.
