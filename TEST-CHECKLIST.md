# Первый тест — Notepad

Перед тестом: Windows 11, установлены EN и RU раскладки. Запусти проект через F5 в VS Code, затем открой обычный Блокнот.

## P0 — проверить сначала

1. EN активна. Набери `Ghb` -> рядом с caret должно быть `При`.
2. Продолжи до `Ghbdtn` -> preview должен стать `Привет`.
3. Нажми Right Arrow -> `Ghbdtn` должно замениться на `Привет`, preview исчезнуть, активная раскладка Windows должна стать RU.
4. Сразу физически набери `vbh` -> Блокнот должен получить `мир`.
5. Повтори тест и вместо Right Arrow кликни по preview.

## Нативное редактирование

Проверь, что без активного preview Блокнот ведёт себя как обычно: Ctrl+A, Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+Z, стрелки, Home/End, Delete, выделение мышью.

## Известные ограничения этого vertical slice

- Сейчас отслеживается одно текущее слово; persistent correction целого предложения будет следующим этапом.
- Позиция overlay пока берётся через Win32 caret. Если в твоей версии нового Notepad preview вообще не появляется, это ожидаемый диагностический результат: следующим шагом добавим UI Automation/TSF fallback.
- Защита password/secure/IME пока не реализована; прототип предназначен только для контролируемого теста в Блокноте.

## Visual regression — v0.5
- First preview appearance fades/scales from ~0.96 to 1 in ~150 ms.
- Subsequent characters do not replay the entrance animation.
- Preview frame width grows/shrinks smoothly as the converted word changes.
- Right Arrow / click Accept moves the framed suggestion left toward the typed word.
- Frame + suggestion are fully dissolved at about 50% of the ~170 ms Accept movement.
- Final accepted text appears around that midpoint, without a frame.
- Preview text and border use the same neutral gray hue family.


## Manual layout sections (2026-10-10)

- Type `ghbdtn` in EN, manually switch to RU, then back to EN without typing.
  Old hint clears, session ID stays the same, typing ` ghbdtn` creates a new section.
  Right or mouse accepts only the latter: `ghbdtn привет`.
- Repeat manual switches using Win+Space and the configured Alt+Shift/Ctrl+Shift.
  No new session ID solely due to switching, prior text stays unchanged.
- Delete the current section with Backspace and one character across its boundary.
  Old hint must not reappear; subsequent input belongs to an empty new section.
- Check both conversion directions and prefix `OLD ` in Notepad/ChatGPT/Discord/Chrome.
- Automatic layout switching during accept must not emit manual section boundaries.
