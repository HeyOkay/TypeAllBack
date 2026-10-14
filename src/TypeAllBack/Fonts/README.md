# Шрифты подсказки

Распространённые открытые веб-шрифты, чтобы подсказка рисовалась тем же шрифтом, что и поле (Roboto во VK, Open Sans, Inter и т. д.), а не Segoe UI. Установленный в Windows шрифт с тем же именем всегда важнее встроенного.

Все шрифты распространяются по лицензии SIL Open Font License 1.1; тексты лицензий с копирайтами — в `licenses/`. Источник — репозиторий google/fonts (https://github.com/google/fonts).

Начертания: 400, 500, 600, 700 (у PT Sans и PT Serif — 400 и 700). Курсив синтезируется.

## Изменения

- Шрифты без зарезервированных имён (Reserved Font Name), поставляемые как вариативные, превращены в статичные начертания (fontTools instancer) и урезаны до латиницы, кириллицы и общих знаков. Имена сохранены.
- Шрифты с зарезервированными именами, у которых есть исходные статичные файлы, включены **без изменений**: Lato, PT Sans, PT Serif, IBM Plex Mono.
- Шрифты с зарезервированными именами, доступные только вариативными, изменены (статичные начертания, урезанный набор знаков) и поэтому по условиям OFL переименованы:

| Внутреннее имя | Исходный шрифт | Лицензия |
|---|---|---|
| TAB Sans A | IBM Plex Sans (IBM Corp.) | `licenses/TABSansA-OFL.txt` |
| TAB Sans B | Raleway (The Raleway Project Authors) | `licenses/TABSansB-OFL.txt` |
| TAB Sans C | Source Sans 3 (Adobe) | `licenses/TABSansC-OFL.txt` |
| TAB Mono C | Source Code Pro (Adobe) | `licenses/TABMonoC-OFL.txt` |

Программа сопоставляет имя шрифта поля с внутренним именем (PreviewFontResolver.BundledAliases); изменённые версии не выдаются за оригиналы.

## Состав

Roboto, Open Sans, Noto Sans, Inter, Montserrat, PT Sans, Source Sans 3, Raleway, Nunito, Rubik, Fira Sans, IBM Plex Sans, Golos Text, Onest, Manrope, Mulish, Exo 2, Lato, Arimo, Noto Serif, PT Serif, Roboto Mono, JetBrains Mono, Fira Code, Source Code Pro, IBM Plex Mono.
