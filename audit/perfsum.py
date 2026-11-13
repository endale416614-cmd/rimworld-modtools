"""Summarises a probe scan timing table (LP17_PROBE_OUT.perf.tsv).

usage: py -I perfsum.py RUN.perf.tsv [TOP]

Prints the tick cost, the share taken by all timed mod patches and components, totals per owner, and the entries
that cost 20 microseconds or more per call. Each timed call carries roughly 0.1 us of timer overhead, so an entry
with millions of cheap calls looks heavier than it is.
"""
import csv, sys
from collections import defaultdict


def main():
    rows = list(csv.DictReader(open(sys.argv[1], encoding="utf-8"), delimiter="\t"))
    top = int(sys.argv[2]) if len(sys.argv) > 2 else 28
    tick = next(float(r["ms"]) for r in rows if r["patch"] == "Verse.TickManager.DoSingleTick")
    ticks = next(int(r["calls"]) for r in rows if r["patch"] == "Verse.TickManager.DoSingleTick")
    own = defaultdict(lambda: [0.0, 0, 0])
    for r in rows:
        if r["owner"] != "(top)":
            o = own[r["owner"]]
            o[0] += float(r["ms"])
            o[1] += 1
            o[2] += int(r["calls"])
    total = sum(v[0] for v in own.values())
    calls = sum(v[2] for v in own.values())
    print(f"DoSingleTick {tick:.0f} ms over {ticks} ticks = {tick / ticks:.2f} ms per tick (instrumented)")
    print(f"timed patches and components: {total:.0f} ms = {100 * total / tick:.0f}% of tick time, {calls:,} calls, "
          f"timer overhead about {calls * 0.1 / 1000:.0f} ms")
    print("by owner:")
    for o, v in sorted(own.items(), key=lambda kv: -kv[1][0])[:top]:
        print(f"  {v[0]:8.1f} ms {100 * v[0] / tick:5.1f}%  {v[1]:4d} entries {v[2]:>11,} calls  {o}")
    print("20 us or more per call, 100 calls or more:")
    for r in sorted(rows, key=lambda r: -float(r["usPerCall"])):
        if r["owner"] != "(top)" and float(r["usPerCall"]) >= 20 and int(r["calls"]) >= 100:
            print(f"  {float(r['usPerCall']):9.1f} us x {int(r['calls']):>7,} = {float(r['ms']):7.1f} ms  {r['owner']} | {r['patch'][:100]}")


if __name__ == "__main__":
    main()
