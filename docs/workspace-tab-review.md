# Design review: the Workspace tab

I want a critique of this screen's information architecture, the same way you reviewed the Mesh
Network tab. It has grown feature by feature and I want a better layout, things to cut, and a
reorganisation.

Everything below is what is on screen today with the real copy, so you can judge verbosity from
the actual words rather than my summary of them.

---

## What the app is

LocalNEXUS is a Windows desktop app, WPF and .NET 8, with a dark IDE-style shell. It is a
node-graph studio for orchestrating local and cloud LLMs: you wire model nodes on a canvas, type
a request into a chat-style box, and watch the run stream.

The Workspace is the primary tab. The other primary tab is Network, which you have already
reviewed and which is now much simpler; some of that reasoning may transfer, but do not assume it
does.

## The loop this screen exists to support

1. Open a project (a folder holding a codebase).
2. Put nodes on the canvas and wire them, or start from a template.
3. Choose a model on each Model node.
4. Type a request, press Run.
5. Watch the run, read what it wrote, fix what it got wrong.

Steps 2 and 3 are done once and rarely revisited. Step 4 and 5 are done constantly.

## Who uses it

Someone technical enough to run local models. In the common case they already have a graph they
like and are doing step 4 and 5 over and over.

---

## Current layout

```
┌──┬─────────────────┬────────────────────────────────────┬───────────────┐
│  │ EXPLORER        │ tab strip: [untitled ●]            │ INSPECTOR     │
│A │  untitled       │                                    │               │
│C │  run outline     ├────────────────────────────────────┤ selected node │
│T │                 │                                    │ or empty      │
│I │                 │  CANVAS (Nodify)                   │ message       │
│V │                 │  transparent by design, the        │               │
│I │                 │  desktop shows through             │               │
│T │                 │                                    │               │
│Y │                 │  empty state: template picker      │               │
│  │                 │                                    │               │
│B │ + New graph     ├────────────────────────────────────┤               │
│A │ ─────────────   │ conversation / staged files /      │               │
│R │ PROJECT         │ request box + Search the web + Run │               │
│  │  name/path/idx  │                                    │               │
├──┴─────────────────┴────────────────────────────────────┴───────────────┤
│ Problems 0 │ Activity 20 │ Output            [copy] [clear] [collapse]  │
│ transcript rows                                                          │
├──────────────────────────────────────────────────────────────────────────┤
│ ● Idle  ● Mesh  ● Python Ready  ● No project      0 nodes, 0 wires        │
└──────────────────────────────────────────────────────────────────────────┘
```

### Title bar

Custom-drawn so menus live in it. File / Edit / View / Run / Help, centred document title, and a
**Run** button top right, next to the window caption buttons.

Menu contents:
- File: New graph, Save graph, (open project, templates)
- Edit: Add node, Delete selected
- View: Workspace, Network, Problems, Activity, Output
- Run: Run graph, Pause or resume, Stop, Clear the transcript
- Help: Getting started

### Activity bar (far left, icons only)

Workspace, Network, Spec (only when an extension provides it), then at the bottom: Extensions,
Run history, Settings. Tooltips: *"Workspace: the canvas, its run outline and the node
inspector."*

### Left rail, top to bottom

1. **Getting started walkthrough** (dismissable, and now auto-closes once all five steps are
   done). Five steps, each with a title, two to four sentences of body, and sometimes a button.
2. **EXPLORER** — the graph name, then the run outline. The outline rows are the same node view
   models the canvas draws, so a node lights up in both places from one notification. Each row:
   state dot, title, elapsed time. Empty state: *"No nodes yet. Add one from the Edit menu, or by
   right clicking the canvas."*
3. **+ New graph** button.
4. **PROJECT** (pinned bottom) — project name, full path in mono, and an index state dot with
   text. With nothing open: *"No project open"* / *"Open one from the File menu"* / *"Not indexed
   yet."*

### Editor area, top to bottom

1. **Tab strip** — one tab, the document name and a dot when unsaved. There is only ever one
   document; there is no multi-document support.
2. **Canvas** — Nodify editor. Right-click menu: Add node, Delete selected. Double-click empty
   space opens a node picker (a grid of cards, added recently). Transparent on purpose: the
   window has a see-through mode with an opacity slider in Settings, so the desktop showing
   through the canvas is intended, not a bug.
3. **Empty-canvas overlay** — a template picker. *"Nothing on the canvas yet"* / *"Start from one
   of these, or double click anywhere to add a node. Every template runs as it is once a model is
   chosen on each Model node."* Then template cards, then a *"Build on my own"* card: *"Start from
   an empty canvas. Double click anywhere to add a node."* Dismissable.
4. **Bottom block above the request box**, three things stacked, each conditional:
   - **Conversation** — a header row with a *"New conversation"* button: *"Starts a fresh thread.
     Nothing is deleted: the old one keeps every word and stays searchable in the run history."*
   - **The run is waiting on an answer** — with *"Proceed without answering"* (*"Carries on with a
     stated assumption, which the run says out loud."*) and *"Answer in the box below. Anything you
     send now is read as the answer rather than as a new request."*
   - **Breakpoint hold** — Release unchanged / Continue.
   - **Staged files** — a list with per-row Discard and a *"Discard all"*, plus: *"Everything that
     compiled is already written. These were not, and nothing on disk was changed for them. Open
     one to see what was attempted, then either say what to do below and run again, or discard it.
     They are kept with the project, so closing the application does not lose them."*
5. **Request box** — placeholder *"Describe a request, then press Run or Ctrl+Enter."*, a
   **Search the web** checkbox (*"Applies to every Model node in this run."*), and a **Run**
   button.

### Right inspector

340px. Shows the selected node's settings, with a Title field common to all node types above the
type-specific panel. Empty state: *"Select a node to edit its settings."* This is the same
inspector the Network tab uses.

### Bottom panel

Three tabs, switched by visibility rather than docked:
- **Problems** (with count) — compile diagnostics: severity glyph, file, line, id, message. Empty:
  *"Nothing is wrong with the generated code right now."*
- **Activity** (with count) — the run transcript. Each row: time, state dot, title, detail, an
  optional inline body, a streaming progress line, and a collapsible **Output** expander holding
  the full text.
- **Output** — the same events read as a log, with bodies always expanded. Footer: *"The engines
  write their own logs to disk."* + "Open the log folder".

Header buttons: copy the panel, clear the transcript, hide the panel.

### Status bar

`● Idle` (run state) · `● Mesh` (dot only, tooltip carries the text) · `● Python Ready` ·
staged summary · `● No project` + project kind · right-aligned: `0 nodes, 0 wires`.

---

## Things I already suspect

- **The Run button is in three places**: the title bar, the request box, and the Run menu.
- **The tab strip holds one tab, always.** There is no second document and no way to make one.
- **The left rail mixes three things** the way the Network rail used to: a tutorial, the graph
  outline, and project state.
- **Project state appears twice** — the PROJECT block in the rail and the status bar both name it.
- **The Activity and Output tabs are the same events** rendered two ways. Output is Activity with
  every body expanded.
- **The empty canvas shows a template picker, but the left rail simultaneously says "No nodes
  yet. Add one from the Edit menu"** — two different pieces of advice for the same state, and the
  Edit menu is the least likely route of the three.
- **Nothing gates on having a project.** With no project open the canvas, request box and Run
  button are all fully presented, and the run will fail or write nowhere.
- **The staged-files paragraph is 60 words** and sits above the box you type into.

## Constraints

- WPF desktop, not web. Keep it native-feeling.
- The canvas is Nodify (MVVM) and I am not replacing it.
- The window's transparency is deliberate and configurable; do not treat the canvas being
  see-through as a defect.
- The inspector is one shared slot serving both primary tabs, so proposals for it have to work
  for the Network tab too.
- Strict MVVM: no logic in code-behind. Anything you propose has to be expressible as bindings and
  commands.
- House style: no em dashes, sentence case, prefer saying "not reported" over inventing a value,
  terse microcopy.

## What I want from you

1. A better information architecture for this screen. What belongs in the rail, the editor area,
   the bottom panel, and what should not be permanently visible at all.
2. Specific copy cuts. Which of the explanatory paragraphs are load-bearing and which are noise.
3. Where duplication should be resolved and which copy wins.
4. Whether the three-tab bottom panel is right, and whether Activity and Output should be one
   thing.
5. What the screen should look like with no project open, which is the state a first-time user
   meets.
6. A sketch of the layout you would propose.
