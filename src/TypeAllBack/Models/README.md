# Corpus-derived EN/RU character models

The embedded files contain counts of characters and character transitions of orders
1–3, plus context counts. They contain no word list and no user input. They were
generated mechanically by `Tools/train_language_models.py`; do not patch individual
entries to satisfy examples.

## Sources and attribution

All corpora are pinned to Universal Dependencies release **r2.16**:

- English: https://github.com/UniversalDependencies/UD_English-EWT/tree/r2.16
  English Web Treebank, Stanford University / Universal Dependencies contributors.
  Sources include weblogs, newsgroups, emails, reviews and answers.
- Russian: https://github.com/UniversalDependencies/UD_Russian-GSD/tree/r2.16
  Russian GSD, Google and Universal Dependencies contributors Ryan McDonald,
  Vitaly Nikolaev and Olga Lyashevskaya. Source genre: Wikipedia.
- Russian additionally: https://github.com/UniversalDependencies/UD_Russian-Taiga/tree/r2.16
  UD Russian-Taiga, HSE School of Linguistics / Universal Dependencies contributors
  Olga Lyashevskaya, Olga Rudina, Natalia Vlasova and Anna Zhuravleva.
  Genres include electronic communication, social media, blogs, fiction and news.

Derived model data is distributed under **CC BY-SA 4.0**, with the same attribution.
See `LICENSE.txt`. Model data is a statistical transformation of corpus text;
the original sentences and linguistic annotations are not embedded in the app.
The application code is separate from the model data.

## Reproduction

Download only the training files to a scratch directory as `en-train.conllu` and
`ru-train.conllu`:

https://raw.githubusercontent.com/UniversalDependencies/UD_English-EWT/r2.16/en_ewt-ud-train.conllu

https://raw.githubusercontent.com/UniversalDependencies/UD_Russian-GSD/r2.16/ru_gsd-ud-train.conllu

Download the five `ru_taiga-ud-train-a.conllu` through `-e.conllu` files from
https://github.com/UniversalDependencies/UD_Russian-Taiga/tree/r2.16 into the same directory.

Run `python Tools/train_language_models.py CORPUS_DIRECTORY src/TypeAllBack/Models --ru-corpus mixed`.
Use `--ru-corpus gsd` to reproduce the legacy Russian model, or `taiga` for comparison.
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

Legacy GSD-only results (before the update below), on test sentence prefixes up to 160 characters: EN 1662/1665 errors detected,
RU 512/512; 0 false suggestions on the 2177 proper sentences. On 3–5 character
prefixes, false suggestions reached 0.20%; detection was lower. These measurements
do not establish performance on code, transliteration, slang, mixed-language text
or a live Notepad session. RU training is mainly encyclopedic text, so informal
Russian is a known coverage limitation. Do not tune tables to individual failures.

## Russian model update (2026-10-10)

The selected model combines Taiga train parts a-e and GSD train: 123,340 sentence
records, 7,878,411 alphabetic characters, 151,796 distinct forms. These are corpus
statistics, not a runtime dictionary. RU JSON grows from 109,557 to 165,810 bytes;
English model bytes and runtime scoring/thresholds are unchanged.

`evaluation-model-comparison.json` compares legacy GSD, Taiga, and mixed models.
Selection used dev balance; test was not used to tune thresholds. Normalized
sentence overlaps with either Russian training source and duplicate evaluation
sentences are excluded for all candidates, giving the same evaluated samples.
Only monolingual keyboard-representable texts with at least 12 letters enter
the corpus evaluation; punctuation is stripped and lengths 3/5/10/20/160 tested.
This does not measure real-time editor behavior, code, URLs, or mixed languages.

On Taiga test five-character prefixes, detection improves 900/996 -> 927/996;
false suggestions remain 2/996. On GSD five-character prefixes, detection declines
474/506 -> 466/506, false suggestions remain 1/506. EWT five-character detection
1557/1645 -> 1558/1645, false suggestions remain 1/1645. Longer test sentence
prefixes have no false suggestions across all three corpora (3147 samples),
and mixed detects 3144/3147 wrong-layout samples. Short ambiguous text remains
limited; on dev three-character EWT one false suggestion occurs versus zero
for legacy. Do not claim that the update universally improves every prefix.

To reproduce comparisons, provide pinned Taiga files, original EWT/GSD files,
an output directory, and a legacy RU JSON produced with `--ru-corpus gsd`:
`python Tools/compare_language_models.py TAIGA_DIRECTORY EWT_GSD_DIRECTORY OUTPUT LEGACY_RU_JSON`.
User phrases are regression fixtures only, never inserted into training data.
