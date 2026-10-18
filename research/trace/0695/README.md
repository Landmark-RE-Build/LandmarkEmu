# 0.695 work: patch notes, features, client trace, live architecture

The working folder for moving LandmarkEmu to the 0.695 client (branch `client-0.695`) and for checking every feature
of the live game against what the server has built.

| File | What it is | Made by |
|---|---|---|
| [TASKS.md](TASKS.md) | **The working task list.** Every gap found, with its source and status. Kept up to date. | by hand |
| [FEATURES.md](FEATURES.md) | The feature list: every system of the live game, how it worked (with sources), and what the server does today | by hand |
| [patchnotes.tsv](patchnotes.tsv) | Every developer post in the patch notes, one numbered item per bullet / paragraph (forum + wiki update pages) | `re/tools/patchitems.py` |
| [wiki-xref.tsv](wiki-xref.tsv) | Every wiki page: its kind (prop, outfit, creature, goal, system...) and the client item it matches | `re/tools/wikixref.py` |
| [wiki-systems.md](wiki-systems.md) | The wiki's feature / rule pages, grouped by area, ticked off as each is cross-referenced | `re/tools/wikixref.py` |
| [CLIENT-DIFF.md](CLIENT-DIFF.md) | What changed in the client from 0.589 to 0.695: classes, packets, data tables, UI, scripts, text | by hand + tools |
| ../live-server-architecture.md | How the live servers were built, from the client and interviews; the plan to recreate it | by hand |

## Sources

- **Patch notes:** the official forum's "Update Notes and Developer Discussion" threads (design/forum/patches and
  design/forum/all, archived copies), the wiki's "Update Notes" and "Game Updates" pages.
  - `alpha` items (before the 21 March 2016 relaunch) are history: the relaunch replaced most of those systems.
  - `launch` items (21 March to 4 June 2016) are in the 0.589 client: the server should already do them.
  - `post589` items (7 June 2016 to 6 December 2016) need the server, and usually the 0.695 client.
  - The forum has no update thread after 6 December 2016 (checked against the 2017 and 2019 archived thread lists).
    The January 2017 build and the 14 February 2017 update changed only the exe and the English text.
- **The wiki:** the full fandom dump (design/wiki, 2,340 pages; re/tools/wikiscrape.py).
- **The clients:** 0.589 (re/decomp_all, re/Landmark589) and 0.695 (re/Landmark695, re/decomp_all_695).

## Regenerating

```
python re/tools/patchitems.py      # patch notes -> patchnotes.tsv
python re/tools/wikixref.py        # wiki -> wiki-xref.tsv, wiki-systems.md
```

Item ids (P0001...) follow the date order of all sources, so they shift when new threads are added. TASKS.md quotes
the date and thread with each id.
