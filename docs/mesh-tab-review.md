# Design review: the Mesh Network tab

I want a critique of this screen's information architecture. It has grown feature by feature and
now reads as busy. I am looking for a better layout, things to cut, and a reorganisation.

Everything below is what is actually on screen today, with the real copy, so you can judge the
verbosity from the words themselves rather than from my summary of them.

---

## What the feature is for

LocalNEXUS is a Windows desktop app: a node-graph studio for orchestrating local and cloud LLMs.
This tab is its distributed-inference surface.

A **mesh** pools the graphics cards of several machines so they can run one model none of them
could run alone. A model too large for one card is split into **sections** (contiguous ranges of
transformer layers), each held by a different machine. Sections run in series for every token, so
a model is usable only when every section is covered — there is no partial credit.

Three facts shape the whole design:

- It is **optional**. With the mesh node stopped, the app works normally and everything runs
  locally.
- Splitting is **slower**, not faster. It buys capability, not speed.
- The app does not manage the mesh. A bundled engine (Mesh LLM) runs as a child process and owns
  discovery, layer placement and failure recovery. The app starts it, reads what it reports, and
  draws that. Several columns therefore have no data and say `not reported`.

## Who uses it

Someone technical enough to run local models, but not necessarily familiar with distributed
inference. Most sessions will never touch this tab. Of those that do, the common cases are: start
my own mesh and invite a friend; join a mesh I was invited to; browse public meshes and join one.

---

## Current layout

```
┌─ left rail (scrolls) ─┬─ main area ─────────────────────────┬─ inspector ─┐
│ FILTERS               │ header bar                          │ selected    │
│  STATUS               │ notice strip (dismissable)          │ thing       │
│  FORMAT               ├─────────────────────────────────────┤             │
│  PROVIDER             │ TABLE 1  models + found meshes      │             │
│  SHARING              │ ═══ drag handle ═══                 │             │
│ mesh settings         │ TABLE 2  meshes you have joined     │             │
│ MACHINES              │ ═══ drag handle ═══                 │             │
│                       │ TABLE 3  the mesh you host          │             │
└───────────────────────┴─────────────────────────────────────┴─────────────┘
```

### Header bar

State dot · node state text · membership text · `Start/Stop the node` · `Find meshes` · filter box.

Example: `● Routing in an unnamed mesh (private), 1 source(s), 0 model(s) ready — hosting LocalNEXUS - Testing`

### Notice strip

Reports what the last action did. Dismissable. Example:
`Applied. The node restarted, so it is running with these settings now.`

Exists because this tab's messages used to go to the app's Activity feed, which is only rendered
in a different section of the app.

### Table 1 — models you can reach, and meshes you could join

Two row types share it. Sortable headers, horizontal scroll, tilt-wheel support.

| Column | Meaning |
|---|---|
| NAME | Model name, or mesh name for a search result |
| SIZE | Memory it takes; for a mesh, total across its machines |
| COVERAGE | Bar of blocks, one per section; filled = a machine holds it |
| SOURCES | How many machines run it |
| BACKUPS | Spare machines that could take over |
| STATUS | complete / starting / blocked / not joined |
| CONTEXT | Context window |
| CONTENTS | For a mesh, models it runs; for a model, machines running it |
| VERIFIED | When the mesh last checked |

### Table 2 — meshes you have joined

Header: title, membership summary, `Leave them all`. One row per joined mesh; you can be in
several.

| Column | Meaning |
|---|---|
| MESH | Name recorded when joined |
| MESH ID | Decoded from the invite token |
| JOINED | Time |
| CONNECTION | node stopped / starting the node / reaching the mesh / loading models / in it, models ready / failed |

Plus a per-row `Leave`.

Empty state: *"Not in anybody else's mesh. Find meshes above, pick one and join it, and it appears
here."*

### Table 3 — the mesh you host

Header: title, `Yours. Anybody you give the invite to joins this one.`, `Copy the invite`,
`New invite`. Always exactly one row.

| Column | Meaning |
|---|---|
| MESH | Name, with node state dot |
| MESH ID | Exists once the node has created it |
| WHO CAN FIND IT | `this network only` / `public once the node starts` / `publishing, not listed yet` / `anybody, listed publicly` / `could not be published` |
| MEMBERS | Machines in it |
| YOU ARE GIVING | `not offering this machine` / `offering the machine, no models ticked` / `N models` |

### Left rail, top to bottom

**Four filter groups** (STATUS, FORMAT, PROVIDER, SHARING), each with rows and live counts. Two of
them infer their answer rather than being told it, and say so in tooltips:

> "Worked out from the quantization label. The mesh reports a quantization rather than a format…"

**Mesh settings**, each field followed by explanatory text:

- **Mesh name** — *"What this machine's own mesh is called, and what anybody you invite sees.
  Ignored while you are joined to somebody else's."*
- **Join by invite** + `Join it` — *"For a mesh somebody sent you an invite to. Anything you join is
  listed under the table, and you can be in several at once."*
- **Publish this mesh publicly** (toggle) — *"The only thing here that reaches past your own
  network. Off, only machines on this network can find the mesh. On, it is listed publicly so
  anybody can find it, though joining still needs the token."* (in warning colour)
- **Offer this machine** (toggle)
- **Memory to share** (slider + readout) + **Share all of it** (checkbox) — *"NVIDIA GeForce RTX
  4080 Laptop GPU has 12 GB. Sharing 8.5 GB keeps 3.5 GB for everything else. 3 GB is held back for
  your own models, a quarter of the card and never less than 1.5 GB."*
- **Models to share** — *"Tick what this machine offers the mesh. Nothing is shared until one is
  ticked."* then a checkbox list, then *"Nothing here is in force until you press the button at the
  bottom of this panel. A running node is restarted so it picks them up; a stopped one just keeps
  them for next time."*
- **Port**
- **Invite token** (read-only) + `Copy` / `Replace`
- **Save these settings** — label changes to `Apply and restart the node` when the node is running

**MACHINES** — selectable list of peers.

### Inspector (right)

One shared panel. Shows whichever of six things is selected: a model, a layer section, a machine, a
found mesh, a joined mesh, or the hosted mesh. Selecting in any table clears the others. A back
arrow steps up a level; a close button clears entirely.

---

## What I know is wrong

- **The tab is doing too much.** Browse, join, host and configure all at once.
- **Duplication.** The invite token appears in the rail *and* in table 3's header *and* in the
  inspector. Publishing state appears in the rail toggle *and* in a table column. Membership
  appears in the header text *and* in table 2.
- **The rail is very long** and mixes filters (which act on table 1) with settings (which act on
  the node) with a peer list.
- **Prose density.** Nearly every control carries a sentence or two underneath.
- **Three tables is a lot of chrome** — three headers, three sets of column headings, two drag
  handles.

## Constraints

- WPF desktop, not web. Dark IDE-style shell; this is one of two primary sections.
- Columns showing `not reported` cannot be filled — the engine does not report them.
- Settings are engine command-line arguments read once at startup, so changes need a node restart.
  That is why the save button and several hints talk about restarting.
- The house style avoids em dashes, keeps sentence case, and prefers saying "not reported" over
  inventing a value.

## What I want from you

1. A better information architecture. Should this be one screen? Tabs within the tab? Progressive
   disclosure? Should hosting and joining be separated?
2. Specific cuts — which of the explanatory sentences are load-bearing and which are noise.
3. Where duplication should be resolved, and which copy should win.
4. Whether three tables is right, or whether some of this belongs in the inspector, a dialog, or
   somewhere else entirely.
5. A sketch of the layout you would propose.
