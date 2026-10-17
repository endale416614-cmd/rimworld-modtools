import re,sys,collections
lines=open(sys.argv[1],encoding='utf-8',errors='replace').read().split('\n')
hdr=re.compile(r'^(\d{4}-\d\d-\d\d \d\d[:.]\d\d[:.]\d\d[.:]\d+)\s+(ERROR|WARN|INFO)\s+(.*)$')
msgs=[]; cur=None
for l in lines:
    m=hdr.match(l)
    if m: cur=[m.group(1),m.group(2),m.group(3),[]]; msgs.append(cur)
    elif cur is not None and l.strip(): cur[3].append(l)
print('== INFO TIMELINE after 19:33:55 (non-mod-chatter)')
for m in msgs:
    if m[1]=='INFO' and m[0][11:]>'19:33:55' and not re.search(r'RimTunes|Isekai|\[GUIPerf\]|RimExodus',m[2]): print(m[0][11:],m[2][:170])
print('\n== Texture2D-from-file exceptions')
c=collections.Counter(); ex=collections.Counter(); samples={}
pathrx=re.compile(r'([A-Za-z]:[\\/][^\s\'"]+?\.(?:png|jpg|jpeg|dds|psd|ktx|tga))',re.I)
foldrx=re.compile(r'294100[\\/](\d+)[\\/]')
for m in msgs:
    if m[2].startswith('[Verse] Exception loading UnityEngine.Texture2D'):
        body=' '.join(x.strip() for x in m[3][:10])
        p=pathrx.search(body+' '+m[2])
        e=re.search(r'(System\.\w+Exception[^|]{0,160})',body)
        key=p.group(1) if p else 'NOPATH'
        f=foldrx.search(key)
        if f: key='workshop/'+f.group(1)
        c[key]+=1; ex[e.group(1)[:160] if e else 'NOEX']+=1
        samples.setdefault(key,(m[2][:300]+' || '+body[:600]))
for k,n in c.most_common(40): print(n,k)
print('--- exception types')
for k,n in ex.most_common(10): print(n,k)
print('--- samples')
for k in list(samples)[:3]: print(samples[k])
print('\n== DivideByZero in OnGUI: stack frames')
cc=collections.Counter()
for m in msgs:
    if 'DivideByZeroException' in m[2]:
        fr=[x.strip() for x in m[3] if x.strip().startswith('at ')][:6]
        cc[' | '.join(fr)[:500]]+=1
for k,n in cc.most_common(6): print(n,k)
print('\n== post-long-event NRE stacks')
cc=collections.Counter()
for m in msgs:
    if m[2].startswith('[Verse] Could not execute post-long-event'):
        fr=[x.strip() for x in m[3] if x.strip().startswith('at ')][:5]
        cc[' | '.join(fr)[:500]]+=1
for k,n in cc.most_common(8): print(n,k)
print('\n== no main tex stacks')
cc=collections.Counter()
for m in msgs:
    if 'no main tex' in m[2]:
        fr=[x.strip() for x in m[3] if x.strip().startswith('at ')][:5]; cc[' | '.join(fr)[:400]]+=1
for k,n in cc.most_common(5): print(n,k)
print('\n== Could not load Texture2D: per path prefix')
cc=collections.Counter()
for m in msgs:
    mm=re.search(r"Could not load Texture2D at '([^']+)'",m[2])
    if mm: cc['/'.join(mm.group(1).split('/')[:2])]+=1
for k,n in cc.most_common(50): print(n,k)
print('\n== Unique WARN')
cc=collections.Counter(re.sub(r'\d+','#',m[2])[:200] for m in msgs if m[1]=='WARN')
for k,n in cc.most_common(80): print(n,k)
print('\n== Other ERROR')
cc=collections.Counter()
for m in msgs:
    if m[1]=='ERROR' and not re.search(r'Texture2D|MatFrom|DivideByZero|RimTunes|post-long-event|no main tex',m[2]):
        cc[re.sub(r'\d+','#',m[2])[:230]]+=1
for k,n in cc.most_common(150): print(n,k)
