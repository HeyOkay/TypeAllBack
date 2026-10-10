import collections, hashlib, json, math, re
from pathlib import Path
import sys
# Args: Taiga directory, original EWT/GSD directory, output directory, legacy RU model.
data,old,root=map(Path,sys.argv[1:4]);root.mkdir(parents=True,exist_ok=True)
repo=Path(__file__).resolve().parent.parent
def texts(path):
 with path.open(encoding='utf-8-sig') as f:
  return [line[9:].strip() for line in f if line.startswith('# text = ')]
def normal(text): return ' '.join(re.findall('[a-zа-яё]+',text.lower()))
gsd=texts(old/'ru-train.conllu');taiga=[]
inputs=[]
for part in 'abcde':
 p=data/f'ru_taiga-ud-train-{part}.conllu';taiga+=texts(p)
 inputs.append({'file':p.name,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
def train(sentences):
 counts=collections.Counter();contexts=collections.Counter();words=set(); alphabet='абвгдеёжзийклмнопрстуфхцчшщъыьэюя'
 for text in sentences:
  for word in re.findall(f'[{alphabet}]+',text.lower()):
   words.add(word);ctx='^^'
   for c in word:
    counts[c]+=1;counts[ctx[-1:]+c]+=1;counts[ctx+c]+=1;contexts[ctx[-1:]]+=1;contexts[ctx]+=1;ctx=ctx[-1:]+c
 model={'Alphabet':alphabet,'Total':sum(counts[c] for c in alphabet),'Counts':dict(sorted(counts.items())),'Contexts':dict(sorted(contexts.items()))}
 encoded=json.dumps(model,ensure_ascii=False,separators=(',',':')).encode()
 return model,encoded,{'sentences':len(sentences),'characters':model['Total'],'wordforms':len(words),'ngrams':len(counts),'model_bytes':len(encoded),'model_sha256':hashlib.sha256(encoded).hexdigest()}
models={'gsd':json.loads(Path(sys.argv[4]).read_text(encoding='utf-8-sig'))};stats={}
for name,sentences in [('taiga',taiga),('mixed',taiga+gsd)]:
 model,encoded,info=train(sentences);models[name]=model;stats[name]=info
 out=root/name;out.mkdir(exist_ok=True);(out/'ru.json').write_bytes(encoded)
 print(name,info,flush=True)
english=json.loads((repo/'src/TypeAllBack/Models/en.json').read_text(encoding='utf-8-sig'))
def score(text,m):
 ctx='^^';total=0;n=0;cnt=m['Counts'];con=m['Contexts'];alpha=m['Alphabet'];den=m['Total']+.5*len(alpha)
 for c in text.lower():
  if not c.isalpha():ctx='^^';continue
  n+=1
  if c not in alpha:total+=math.log(1e-7);ctx='^^';continue
  uni=(cnt.get(c,0)+.5)/den;prev=ctx[1:];bi=(cnt.get(prev+c,0)+2*uni)/(con.get(prev,0)+2)
  total+=math.log((cnt.get(ctx+c,0)+4*bi)/(con.get(ctx,0)+4));ctx=prev+c
 return (total/n if n else -math.inf),n
letters='qwertyuiopasdfghjklzxcvbnm;\',.[]`'
russian='йцукенгшщзфывапролдячсмитьжэбюхъё'
maps={'en':dict(zip(letters,russian)),'ru':dict(zip(russian,letters))}
trainkeys={'ru':{normal(t) for t in taiga+gsd},'en':{normal(t) for t in texts(old/'en-train.conllu')}}
reports={};excluded={};fixtures={}
for name,ru in models.items():
 reports[name]=[]
 for split in ['dev','test']:
  for corpus,lang,path in [('ewt','en',old/f'en-{split}.conllu'),('gsd','ru',old/f'ru-{split}.conllu'),('taiga','ru',data/f'ru_taiga-ud-{split}.conllu')]:
   samples=[];seen=set();duplicates=0
   for text in texts(path):
    clean=' '.join(''.join(c if c.isalpha() else ' ' for c in text.lower()).split())
    if normal(clean) in trainkeys[lang] or clean in seen:duplicates+=1;continue
    seen.add(clean)
    if any(c.isalpha() and c not in maps[lang] for c in clean) or sum(c.isalpha() for c in clean)<12:continue
    samples.append(clean)
   excluded[f'{corpus}-{split}']=duplicates
   for length in [3,5,10,20,160]:
    n=fp=det=0
    for clean in samples:
     if len(clean)<length and length!=160:continue
     proper=clean[:length];wrong=''.join(maps[lang].get(c,c) for c in proper)
     original=english if lang=='en' else ru;opposite=ru if lang=='en' else english
     p,pn=score(proper,original);w,wn=score(wrong,opposite);n+=1
     fp+=pn>=3 and wn>=3 and w>=-3.7 and w-p>=1
     det+=pn>=3 and wn>=3 and p>=-3.7 and p-w>=1
    reports[name].append({'split':split,'corpus':corpus,'length':length,'samples':n,'false':fp,'detected':det,'detectPercent':round(100*det/n,2) if n else 0,'falsePercent':round(100*fp/n,2) if n else 0})
 fixtures[name]=[]
 for phrase in ['как ты думаешь','думаешь','что делаешь','ты знаешь','привет мир','спасибо','hello world','this is a normal message']:
  lang='ru' if re.search('[а-яё]',phrase) else 'en';proper=ru if lang=='ru' else english;opp=english if lang=='ru' else ru
  wrong=''.join(maps[lang].get(c,c) for c in phrase);p,pn=score(phrase,proper);w,wn=score(wrong,opp)
  fixtures[name].append({'phrase':phrase,'score':round(p,4),'margin':round(p-w,4),'detected':pn>=3 and wn>=3 and p>=-3.7 and p-w>=1,'falseOnProper':pn>=3 and wn>=3 and w>=-3.7 and w-p>=1})
 print(name,'evaluation completed',flush=True)
report={'release':'r2.16','inputs':inputs,'training':stats,'excludedDuplicates':excluded,'evaluation':reports,'fixtures':fixtures}
(root/'comparison.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
for name in models:
 print(name,json.dumps([r for r in reports[name] if r['length'] in [5,160]],ensure_ascii=False),flush=True)
 print(name,'fixtures',json.dumps(fixtures[name],ensure_ascii=False),flush=True)
