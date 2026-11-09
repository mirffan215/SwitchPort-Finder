#!/usr/bin/python3
import sys,os
mib,mode,oid=sys.argv[1],sys.argv[2],sys.argv[3].strip('.')
rows=[l.rstrip('\n').split('|',2) for l in open(os.path.join(os.path.dirname(os.path.abspath(__file__)),mib))]
key=lambda o:[int(x) for x in o.strip('.').split('.')]
def out(r): print('.'+r[0]); print(r[1]); print(r[2])
if mode=='-g':
    for r in rows:
        if r[0]==oid: out(r); break
elif mode=='-n':
    for r in rows:
        if key(r[0])>key(oid): out(r); break
