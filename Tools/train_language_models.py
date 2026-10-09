"""Build deterministic character Markov counts from UD CoNLL-U train files.
Usage: python Tools/train_language_models.py CORPUS_DIRECTORY Models
Inputs: en-train.conllu, ru-train.conllu. No user input is collected.
"""
import collections
import hashlib
import json
import pathlib
import re
import sys

alphabets = {"en": "abcdefghijklmnopqrstuvwxyz", "ru": "абвгдеёжзийклмнопрстуфхцчшщъыьэюя"}
inputs, output = map(pathlib.Path, sys.argv[1:])
output.mkdir(parents=True, exist_ok=True)
metadata = {}
for language, alphabet in alphabets.items():
    raw = (inputs / f"{language}-train.conllu").read_bytes()
    counts = collections.Counter()
    contexts = collections.Counter()
    sentences = 0
    for line in raw.decode("utf-8").splitlines():
        if not line.startswith("# text = "):
            continue
        sentences += 1
        for word in re.findall(f"[{alphabet}]+", line[9:].lower()):
            previous = "^^"
            for char in word:
                counts[char] += 1
                counts[previous[-1:] + char] += 1
                counts[previous + char] += 1
                contexts[previous[-1:]] += 1
                contexts[previous] += 1
                previous = previous[-1:] + char
    model = {"Alphabet": alphabet, "Total": sum(counts[c] for c in alphabet),
             "Counts": dict(sorted(counts.items())), "Contexts": dict(sorted(contexts.items()))}
    encoded = json.dumps(model, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    (output / f"{language}.json").write_bytes(encoded)
    metadata[language] = {"input_sha256": hashlib.sha256(raw).hexdigest(),
                          "sentences": sentences, "characters": model["Total"],
                          "ngrams": len(counts), "model_bytes": len(encoded),
                          "model_sha256": hashlib.sha256(encoded).hexdigest()}
print(json.dumps(metadata, indent=2))
(output / "training.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
