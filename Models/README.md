# Corpus-derived EN/RU character models

The embedded files contain counts of characters and character transitions of orders
1–3, plus context counts. They contain no word list and no user input. They were
generated mechanically by `Tools/train_language_models.py`; do not patch individual
entries to satisfy examples.

## Sources and attribution

Both corpora are pinned to Universal Dependencies release **r2.16**:

- English: https://github.com/UniversalDependencies/UD_English-EWT/tree/r2.16
  English Web Treebank, Stanford University / Universal Dependencies contributors.
  Sources include weblogs, newsgroups, emails, reviews and answers.
- Russian: https://github.com/UniversalDependencies/UD_Russian-GSD/tree/r2.16
  Russian GSD, Google and Universal Dependencies contributors Ryan McDonald,
  Vitaly Nikolaev and Olga Lyashevskaya. Source genre: Wikipedia.

Derived model data is distributed under **CC BY-SA 4.0**, with the same attribution.
See `LICENSE.txt`. Model data is a statistical transformation of corpus text;
the original sentences and linguistic annotations are not embedded in the app.
The application code is separate from the model data.

## Reproduction

Download only the training files to a scratch directory as `en-train.conllu` and
`ru-train.conllu`:

https://raw.githubusercontent.com/UniversalDependencies/UD_English-EWT/r2.16/en_ewt-ud-train.conllu

https://raw.githubusercontent.com/UniversalDependencies/UD_Russian-GSD/r2.16/ru_gsd-ud-train.conllu

Run `python Tools/train_language_models.py CORPUS_DIRECTORY Models`.
`training.json` records hashes, sentence counts, character counts and output hashes.
Only `# text = ` records from the train files are used. Lowercase letter runs in the
language alphabet are counted, with two start-of-word markers. Other characters are
boundaries. Evaluation examples and dev/test records are never added to training.

## Runtime decision

LanguagePlausibility scores all characters in the session segments for the source
layout and their physical conversion for the target layout. It uses recursive
backoff smoothing: unigram pseudocount 0.5, bigram prior strength 2, trigram prior
strength 4. The alternative must have mean log probability >= -3.7 and improve
on the original by >= 1.0. At least three letters of evidence must exist in both
versions; this is a minimum evidence guard, not a maximum word/session length.
All evaluation is local. Models load once; preview scores are cached until input
changes. There is no network access, telemetry, personal training or text history.

## Held-out evaluation

Download matching dev/test files as `en-dev.conllu`, `ru-dev.conllu`,
`en-test.conllu`, `ru-test.conllu` alongside train files. Run:

`dotnet run --project Tests/LayoutPreview.Tests.csproj -- --corpus CORPUS_DIRECTORY test`

The evaluator excludes exact training sentence duplicates, restricts to monolingual
keyboard-representable letters, strips punctuation/numbers, and maps physical keys
to manufacture a wrong-layout version. It evaluates proper text and corrupted text
separately. The test split was evaluated after fixing the decision thresholds.
`evaluation-dev.json` and `evaluation-test.json` contain the full results.

On test sentence prefixes up to 160 characters: EN 1662/1665 errors detected,
RU 512/512; 0 false suggestions on the 2177 proper sentences. On 3–5 character
prefixes, false suggestions reached 0.20%; detection was lower. These measurements
do not establish performance on code, transliteration, slang, mixed-language text
or a live Notepad session. RU training is mainly encyclopedic text, so informal
Russian is a known coverage limitation. Do not tune tables to individual failures.
