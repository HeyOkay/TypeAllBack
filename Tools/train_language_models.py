"""Train pinned UD character models; no user examples or dev/test text are used.
Usage: python Tools/train_language_models.py CORPUS_DIRECTORY src/TypeAllBack/Models --ru-corpus mixed
Russian choices: gsd (legacy), taiga, mixed. Taiga uses train parts a-e from r2.16.
"""
import argparse, collections, hashlib, json, pathlib, re
parser=argparse.ArgumentParser()
parser.add_argument("inputs",type=pathlib.Path);parser.add_argument("output",type=pathlib.Path)
parser.add_argument("--ru-corpus",choices=["gsd","taiga","mixed"],default="mixed")
args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
metadata={}
for language,alphabet in {"en":"abcdefghijklmnopqrstuvwxyz","ru":"абвгдеёжзийклмнопрстуфхцчшщъыьэюя"}.items():
    files=["en-train.conllu"] if language=="en" else (["ru-train.conllu"] if args.ru_corpus=="gsd" else [f"ru_taiga-ud-train-{p}.conllu" for p in "abcde"] + (["ru-train.conllu"] if args.ru_corpus=="mixed" else []))
    counts=collections.Counter();contexts=collections.Counter();sentences=0;words=set();sources=[]
    for name in files:
        path=args.inputs/name;raw=path.read_bytes()
        sources.append({"file":name,"sha256":hashlib.sha256(raw).hexdigest()})
        for line in raw.decode("utf-8-sig").splitlines():
            if not line.startswith("# text = "):continue
            sentences+=1
            for word in re.findall(f"[{alphabet}]+",line[9:].lower()):
                words.add(word);previous="^^"
                for char in word:
                    counts[char]+=1;counts[previous[-1:]+char]+=1;counts[previous+char]+=1
                    contexts[previous[-1:]]+=1;contexts[previous]+=1;previous=previous[-1:]+char
    model={"Alphabet":alphabet,"Total":sum(counts[c] for c in alphabet),"Counts":dict(sorted(counts.items())),"Contexts":dict(sorted(contexts.items()))}
    encoded=json.dumps(model,ensure_ascii=False,separators=(",",":")).encode("utf-8")
    (args.output/f"{language}.json").write_bytes(encoded)
    metadata[language]={"release":"r2.16","corpus":"EWT" if language=="en" else args.ru_corpus,"inputs":sources,"sentences":sentences,"characters":model["Total"],"wordforms":len(words),"ngrams":len(counts),"model_bytes":len(encoded),"model_sha256":hashlib.sha256(encoded).hexdigest()}
print(json.dumps(metadata,indent=2))
(args.output/"training.json").write_text(json.dumps(metadata,indent=2)+"\n",encoding="utf-8")
