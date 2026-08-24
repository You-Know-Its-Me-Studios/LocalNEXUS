# Pre-release audit

Cold read of the repository at commit `80dfefc`, against the question "is this ready to be
public". Findings only. Nothing here has been fixed.

A note on tone, because it shapes how to read the rest: the parts of this codebase that have
been around a while are in better shape than the raw metrics suggest, and I threw away two
findings during this audit that turned out to be measurement errors on my part. The problems
concentrate almost entirely in what is newest, which is the distributed inference feature, and
in the seams between the app and the outside world (network exposure, logs, CI, docs).

---

## If you fix ten things, fix these

| # | Severity | Finding | Where |
|---|----------|---------|-------|
| 1 | Blocker | The distributed peer binds `0.0.0.0` with no authentication of any kind. Anyone who can reach the port can make it load arbitrary paths and repo ids. | `vendor/python/distributed/__main__.py:44` |
| 2 | Blocker | Mesh invite tokens are written in the clear to a log folder that is never pruned, and the bug report template tells users to attach exactly that folder. | mesh logs; `.github/ISSUE_TEMPLATE/bug_report.yml:96` |
| 3 | Major | CI never runs the tests. Five tests fail on a clean clone, and nothing would catch a sixth. | `.github/workflows/build.yml` |
| 4 | Major | The entire safetensors distributed feature is undocumented. Zero markdown files mention it. | `docs/` |
| 5 | Major | The mesh offers safetensors models it cannot serve. No format filter on the offer list. | `src/LocalNEXUS.App/ViewModels/NetworkViewModel.cs:1388` |
| 6 | Major | Logs accumulate without limit. Measured on this machine: 483 files, 204 MB. | `src/LocalNEXUS.App/Services/Persistence/AppPaths.cs:308` |
| 7 | Major | The spurious-disconnect bug is unresolved, and a disconnect fails *every* in-flight request, not just one. | `vendor/python/distributed/coordinator.py:315` |
| 8 | Major | A second assignment to a peer loads the new model before releasing the old one, so it needs double the memory and will OOM. | `vendor/python/distributed/coordinator.py:79` |
| 9 | Major | No single-instance guard, and the mesh ports are fixed. A second launch collides with the first. | `src/LocalNEXUS.App/Services/Persistence/AppConfig.cs:187` |
| 10 | Major | ~2500 lines of new Python have no automated tests at all. There are no Python tests anywhere in the repo. | `tests/` |

---

## 1. First-run and setup

### Major: ~3 GB is downloaded on first launch with no in-app consent

`src/LocalNEXUS.App/App.xaml.cs:414` fires `ProvisionPythonAsync` unconditionally during
`Compose()`. It is not gated on a setting, a prompt, or whether the user has any interest in
safetensors models. On an NVIDIA machine that pulls a CUDA torch, which the README puts at
roughly 3 GB.

The README does disclose this ("That is the only thing downloaded without you asking"), and
that honesty is worth something. But the disclosure lives in a file the user has usually
already stopped reading by the time they double-click the exe. For a public release this is
the kind of thing that produces an angry issue from someone on a metered connection or a
data cap, and they will be right.

Why it matters publicly: it is the first thing the app does, it is invisible, and it is
irreversible from the user's point of view once started.

### Major: five tests fail on a clean clone

Verified by running `dotnet test`: 566 total, 561 pass, 5 fail. All five are
`EndToEndTests`, all with the same message:

```
No GGUF model was found under %LOCALAPPDATA%\LocalNEXUS\models\gguf.
Put one there and run this layer again. Nothing here downloads a model.
```

The message itself is good. The problem is that these *fail* rather than *skip*. A
contributor's first `dotnet test` on a fresh clone goes red, and the honest reading of a red
suite is "this project is broken". `CONTRIBUTING.md` mentions needing a GGUF for
`vendor/llama/` (line 18) but does not warn that the test suite will fail without one.

### Minor: the README's test count is stale

`README.md:72` says "317 tests". The actual count is 566. Not important on its own, but it is
the kind of number a reader spot-checks, and being wrong about it undermines the numbers next
to it (the eval scores) that are much harder to verify.

### Minor: a machine with no NVIDIA GPU

I traced this by reading rather than by testing on such a machine, so treat it as
unverified. `AcceleratorProbe.Detect()` shells out to `nvidia-smi` and falls back to the CPU
lockfile when no driver answers, with a reason string. That path looks correct and
deliberate. `DistributedRuntimeManager.FitsOnThisMachine` returns `false` when
`DetectMemory()` returns null, which routes a model to the distributed path on a machine with
no GPU at all — see finding 3.4.

**I did not verify** the no-GPU path end to end. I have one machine and it has an NVIDIA card.

---

## 2. Error handling and failure modes

This area is much better than a naive grep suggests, and I want to be explicit about that
because I nearly filed a bad finding here. My first pass counted "175 bare catches" and it was
wrong: the detector was broken. Corrected numbers:

- 290 `catch` clauses in C#
- 61 of them have no executable statement
- **all 61 carry an explanatory comment**, and every one I read is narrowly typed
  (`catch (Exception ex) when (ex is IOException or ObjectDisposedException)`)

The Python side has 11 broad catches, every one annotated with a `# noqa: BLE001` and a
reason. There are zero `TODO`, `FIXME`, `HACK` or `XXX` markers in the entire source tree, and
zero `NotImplementedException`.

So the findings below are specific, not systemic.

### Major: an unbounded `max_tokens` from an untrusted caller

`vendor/python/distributed/api.py:71`:

```python
def _max_tokens(self, body: dict[str, Any]) -> int:
    asked = body.get("max_tokens") or body.get("max_completion_tokens")
    return int(asked) if asked else DEFAULT_MAX_TOKENS
```

No upper bound. `{"max_tokens": 100000000}` is accepted and loops that many times, each
iteration a full network round trip through every stage, holding a KV cache that grows the
whole time. A non-numeric value raises `ValueError` inside the handler and becomes an
unhandled 500 rather than the 400 it should be. There is also no cap on prompt length and no
limit on concurrent requests.

On a loopback-only deployment this is a footgun. Combined with finding 4.1 it is a denial of
service that any machine on the LAN can trigger.

### Minor: the distributed host's refusal reaches the user as a wall of text

`DistributedRuntimeManager.WaitUntilHealthyAsync` (`:339`) surfaces a failed launch as
`"...Recent output:" + instance.GetRecentOutput()`, which is up to 40 lines of Python logging
including tracebacks. That is genuinely the right call for diagnosing a pipeline that would
not come up, and I would not change it to something shorter. But it lands in a WPF error
surface, and a stranger's first encounter with it will read as a crash.

Opinion, not a defect: the *first* line of that output is usually the real answer (the planner
refuses with an exact shortfall in GB). Leading with that and putting the rest behind a
disclosure would be better.

---

## 3. The distributed inference feature

This is the weakest area by a wide margin, and it is also the newest. Everything below is
something I would expect a stranger with two machines to hit.

### Blocker: see 4.1. No authentication, binds to all interfaces.

### Major: the spurious-disconnect bug is still unresolved

Current status, accurately: **not fixed, not diagnosed, diagnostics added.**

`vendor/python/distributed/activation_server.py:508` now logs the exception type, the
`__cause__`, link age, whether the reader reports EOF, the transport state, and a stack trace
when a link is declared lost. That was added specifically so the next occurrence is
diagnosable. The underlying behaviour is unchanged.

What was observed: the host logged `stage 0 lost 127.0.0.1:8802` while the peer was
demonstrably alive, and six subsequent requests all succeeded, with no reconnect logged. That
last part contradicts what the code does, which is why it is not diagnosed. A baseline from a
*genuine* disconnect is now known (`cause: ConnectionResetError`, `transport closing=True,
socket=-1`, and `reader eof: False` even then, so EOF is not a discriminator).

### Major: one disconnect fails every in-flight request

`vendor/python/distributed/coordinator.py:315`, `_token_arrived`: an `ERROR` frame carrying no
`request_id` is put on the queue of *every* waiting request.

The reasoning is written down and is defensible in isolation — a genuinely broken pipeline
cannot finish anything. But it compounds badly with the finding above: a *spurious* loss event
would fail every concurrent request at once. Two people sharing one host would see unrelated
requests die together for no visible reason.

### Major: a peer given a second assignment needs double the memory

`vendor/python/distributed/coordinator.py:79`, `_take_assignment`:

```python
stage = PartialStage(plan.model_dir, assignment, quantization=plan.quantization)
await asyncio.to_thread(stage.load)
self._stage = stage
```

The new stage is fully loaded *before* `self._stage` is rebound, so both models are resident
during the second load. There is no explicit release of the old stage, no
`torch.cuda.empty_cache()`. A peer holding 10 GB that is re-planned needs 20 GB and will fail
with a CUDA OOM whose message says nothing about the real cause.

This is on the normal path, not an edge case: any re-plan (a machine joins, a machine leaves,
the user changes model) goes through it.

### Major: a peer holds its stage forever

There is no idle timeout and no release on host disconnect. Once a peer has been given
layers, it holds that VRAM until the process is killed. If the host crashes, the contributing
machine's card stays occupied indefinitely and the only remedy is for the owner to notice and
kill it manually.

### Major: never run across two physical machines

The README says this plainly and to its credit ("Unproven: everything distributed... Never
across two physical machines, which is embarrassing to still be writing"). Everything I
verified was loopback. Specifically **unverified**: NAT and firewall behaviour, real network
latency per decode token, whether the 300-second token timeout is right on a slow link,
behaviour when the two machines disagree about the model directory path, and whether a peer on
a different CUDA version loads the same weights identically.

### What would embarrass you if a stranger tried it

Ranked by likelihood:

1. They run `python -m distributed peer` as documented, which binds `0.0.0.0`, and either a
   security-minded reviewer notices or something on their network pokes it.
2. The model directory path differs between the two machines and the failure message is a
   Python traceback about a missing config, not "that path does not exist on the other
   machine".
3. They expect 4-bit to make a big model fit, because that is what 4-bit means everywhere
   else, and on a MoE model it saves 3% (see 6.2).
4. A re-plan OOMs the peer for no visible reason.

---

## 4. Security and safety of a public release

### Blocker: the distributed peer has no authentication and binds to all interfaces

`vendor/python/distributed/__main__.py:44`:

```python
peer.add_argument("--host", default="0.0.0.0",
                  help="the address to listen on. 0.0.0.0 accepts from the network.")
```

The module docstring at line 3 documents `--host 0.0.0.0` as *the* example invocation, so this
is the shipped guidance, not just a default.

I searched the whole package for any token, secret, handshake, HMAC or signature in the
protocol. There is none. `activation_server.py:258` dispatches a `LOAD` frame from any
connected socket straight to the load path. What an unauthenticated peer on the network can
therefore do:

- **Cause arbitrary loads.** `partial_loader.py:118` and `:134` pass an attacker-supplied
  `model_dir` to `AutoConfig.from_pretrained` / `AutoModelForCausalLM.from_pretrained`. Those
  accept a Hugging Face repo id as well as a local path, so a remote party can make the
  machine download arbitrary repositories from the internet.
- **Exhaust memory and disk.** Repeated `LOAD` frames, each multi-gigabyte. Frames carry
  tensors up to the 1 GB protocol cap. No rate limit, no connection cap, no concurrency cap.
- **Probe the filesystem.** Error messages distinguish "no config" from "not a folder",
  which is a file-existence oracle.

**What it cannot do, verified:** `trust_remote_code` is not set anywhere in the package, so it
defaults to `False` and custom modeling code will not execute. `weights_only` defaults to
`True` in the installed transformers 5.15.1 (`modeling_utils.py:189`), so the pickle
deserialization path is closed. **This is not remote code execution.** It is remote-controlled
resource consumption and outbound network activity, which is bad enough to gate a release on.

The mitigation is not necessarily auth — binding loopback by default and requiring an explicit
opt-in for a LAN address would remove most of it.

### Blocker: mesh invite tokens are logged in the clear, and users are told to send the logs

Real evidence from this machine's logs:

```
16:32:20 INFO  Invite created for mesh LocalNEXUS-e2e (8af8b130...): eyJpZCI6IjE0Zjc0NjQwNDg3ODRlODg0YW...
```

The invite token is the credential that grants join access to a private mesh. It is captured
from the mesh child process's stdout into a plaintext file under
`%LOCALAPPDATA%\LocalNEXUS\logs\`, which is never pruned (see 7.2).

`.github/ISSUE_TEMPLATE/bug_report.yml:96` tells the reporter: *"logs are in
%LOCALAPPDATA%\LocalNEXUS\logs"*. So the documented support workflow directs users to attach,
to a public GitHub issue, a folder containing live mesh join credentials.

The token is emitted by `mesh-llm` itself, which is third-party. The retention and the
"attach your logs" instruction are ours.

`SECURITY.md` is otherwise unusually thorough — it correctly explains that llama-server binds
`127.0.0.1` and runs with permissive CORS and no API key, and that this is only safe because
of the binding. It says nothing about the new Python peer, which is the one thing in the
codebase that does not bind loopback.

### Minor: `SECURITY.md` does not cover the distributed pipeline

Lines 77 to 79 cover llama-server and the mesh node. There is no mention of port 8749, the
activation protocol, or the peer's exposure. For a document whose whole purpose is telling a
researcher what the attack surface is, the newest and least-protected surface being absent is
a gap.

---

## 5. Code quality and maintainability

The mechanical indicators here are good and I want to be clear about that rather than
manufacture findings: zero TODO/FIXME/HACK/XXX, zero `NotImplementedException`, no
commented-out code blocks that I found, and the exception handling discipline described in
section 2.

### Major: `TestServices` has silently diverged from the real composition

`tests/LocalNEXUS.Tests/Support/TestServices.cs:112` builds:

```csharp
var runtimes = new RuntimeResolver(new LlamaServerManager(children), new PythonRuntimeManager(children, python));
```

`src/LocalNEXUS.App/App.xaml.cs:198` builds:

```csharp
var runtimes = new RuntimeResolver(_llamaServers, _distributedRuntime, _pythonRuntime);
```

Two runtimes versus three. The consequence is `ModelRuntimeTests.cs:235`:

```csharp
/// <summary>The real build wires two runtimes, one per format.</summary>
Assert.Equal(2, runtimes.Runtimes.Count);
```

That test is **green and meaningless**. Its name and its doc comment both claim to verify the
real composition; it verifies a hand-maintained duplicate that no longer matches. This is
worse than no test, because it reads as coverage. Resolver ordering is now load-bearing (the
distributed runtime must be asked before the Python one or the feature silently never
activates) and nothing tests it.

### Major: no tests for the distributed package

`find` for `test_*.py` / `*_test.py` returns nothing anywhere in the repo. There is no pytest,
no Python test project, no CI step that would run one. The package is roughly 2500 lines
across 10 modules including a wire protocol, a planner, and a partial-weight loader with
non-obvious invariants (contiguous layer coverage, embed on first stage, head on last,
bit-identical split equivalence).

All of my verification during development was ad-hoc scripts in a temp scratchpad directory.
Those are not in the repository and are gone. Nothing protects any of it from regression.

### Minor: the largest files are doing too much

Against the project's own stated standard in `CLAUDE.md` ("Single responsibility... Small
focused files"):

| Lines | File |
|-------|------|
| 2295 | `src/LocalNEXUS.App/Nodes/ModelNode.cs` |
| 1480 | `src/LocalNEXUS.App/ViewModels/MainViewModel.cs` |
| 1431 | `src/LocalNEXUS.App/ViewModels/NetworkViewModel.cs` |
| 1080 | `src/LocalNEXUS.App/Services/Distributed/MeshManager.cs` |
| 661 | `vendor/python/distributed/activation_server.py` |

`ModelNode.cs` at 2295 lines is the worst offender by a distance. I did not read it in full,
so I am not asserting it *should* be split, only that it is four times the size of anything
around it and is the file a new contributor is most likely to need to touch.

### Nit, and this is opinion: comment density in the Python half

The distributed package carries a much heavier explanatory-prose style than the C# half —
multi-paragraph module docstrings and long "why" comments on individual decisions. I wrote it
that way deliberately and I still think the reasoning is worth keeping, but it is a visible
inconsistency between the two halves of the codebase, and a contributor will not know which
convention to follow. Worth a line in `CONTRIBUTING.md` either way.

---

## 6. Documentation and onboarding

The README is genuinely good: it explains what the thing is, gives a five-minute path to a
result, and is honest about limitations in a way most projects are not (the namespace task
that fails 29 times out of 30 is called out by name). The gaps below are about what is
*missing*, not what is wrong.

### Major: the safetensors distributed feature is documented nowhere

I grepped every markdown file in the repo for `python -m distributed`,
`DistributedInferenceEnabled`, `DistributedPeers`, `distributed host` and `distributed peer`.
**Zero matches.** The feature comprises:

- 10 Python modules and a CLI with two subcommands
- a C# runtime (`DistributedRuntimeManager`, 482 lines)
- two config keys that can only be set by hand-editing a JSON file

None of it is written down. Worse, `docs/distributed.md` exists and is exclusively about the
GGUF mesh, so a reader looking for "how do I distribute a model" finds a document that
confidently answers a different question. Lines 21 and 59 to 60 of that file talk only about
GGUF and layer packages.

The two config keys are the sharp end: `DistributedInferenceEnabled` defaults to false and has
no UI, so the feature is currently **unreachable** by anyone who has not read the source.

### Major: the 4-bit MoE limitation is written down nowhere

Grepped every markdown file for `bitsandbytes`, `4bit`, `4-bit`, `quantiz`. The only hit is an
unrelated line in an internal review document.

The limitation is counterintuitive and expensive to rediscover: quantization in transformers
is an `nn.Linear` module swap, and a mixture-of-experts model stacks its experts into a 3D
`nn.Parameter` that the swap never sees. Measured on Qwen3-Coder-30B, only **3.0%** of the
layer weights are quantizable, so 4-bit takes the model from 56.87 GB to 55.69 GB. Anyone who
turns on 4-bit expecting the usual ~4x will conclude the feature is broken.

### Minor: internal working documents are shipping in `docs/`

`docs/mesh-tab-review.md` and `docs/workspace-tab-review.md` are design-review briefs written
to be pasted into a chat model. `docs/history-rewrite.md` documents an internal git history
rewrite. `docs/eval-night-log.md` is a working log. None are referenced from the README's docs
table, which lists five files. They are not harmful, but a public `docs/` folder is read as
curated, and these are scratch.

Opinion: `CLAUDE.md` at 30 KB in the repo root is a judgement call. It is candid about
internal reasoning and reversed decisions. Plenty of projects ship one; just be aware it is
the second-largest markdown file in the repo and strangers will read it.

---

## 7. State and lifecycle

### Major: no single-instance guard, and the mesh ports are fixed

I searched for a `Mutex` or any single-instance mechanism and found none. Two copies of the
app can run simultaneously. Consequences:

- `AppConfig.cs:187` and `:190` fix the mesh ports at 9337 and 3131. The second instance's
  mesh node cannot bind and will fail, with an error that will not obviously mean "you have
  two copies open".
- Both instances read and write the same `AppConfig` file. The save itself is atomic
  (temp file plus `File.Move`), so the file will not be corrupted, but it is
  last-writer-wins across the *whole document*: instance A changing a theme and instance B
  changing a model path means one of them loses silently.
- Both spawn engine processes and both compete for the same GPU.

Per-model inference ports are fine — `LlamaServerManager.cs:467`,
`PythonRuntimeManager.cs:312` and `DistributedRuntimeManager.cs:471` all ask the OS for a free
loopback port.

### Major: logs accumulate without limit

`AppPaths.cs:308` creates a new timestamped log file per engine launch. I searched for any
pruning, retention policy or cleanup and found none.

Measured on this machine right now: **483 files, 204 MB**. This is a development machine, so a
user's growth will be slower, but the growth is unbounded and nothing ever deletes one. It is
also the folder containing cleartext invite tokens (4.2), so unbounded retention is a security
property here and not just a tidiness one.

### Minor: process ownership on crash is actually handled well

Worth stating because it is the thing most likely to leak and it does not. `ChildProcessGroup`
puts every engine process in a Windows job object so the kernel kills the whole tree when the
app's handle closes, `ChildProcessRegistry` records processes so a later session can reap
what a crash left, and identity is matched on id plus start time plus binary rather than pid
alone. `App.xaml.cs` hooks `DispatcherUnhandledException`, `UnhandledException`,
`ProcessExit` and `UnobservedTaskException`.

The gap: a distributed **peer** is started by the user from a command line and is therefore
outside all of it. Nothing the app owns will ever clean one up. Given peers are currently
manual-only that is arguably correct, but it will stop being correct the moment peer launch
moves into the app.

### Minor: the Python venv is never garbage collected

`%LOCALAPPDATA%\LocalNEXUS\runtime\python\` holds an interpreter, a venv and a download cache
(roughly 3 GB with CUDA torch). Uninstalling the app does not remove it — I did not test the
uninstaller, so treat that as inference from the paths rather than a verified claim. There is
a Reset in the UI, but nothing tells a user that several gigabytes are sitting there.

---

## 8. The gap between what the UI says and what the app does

### Major: the mesh offers safetensors models it cannot serve

`src/LocalNEXUS.App/ViewModels/NetworkViewModel.cs:1377`, `RebuildOfferedModels`:

```csharp
foreach (var model in Catalog.Models)          // line 1388
{
    var row = new OfferedModelViewModel(model, offered.Contains(model.Path), OnOfferChanged);
```

No format filter. `ModelCatalog` deliberately discovers both formats (that is a documented
architecture decision), and Mesh LLM's inference path is GGUF-only. So the contribution panel
presents safetensors models with a tick box, the tick is persisted to
`MeshOfferedModelPaths`, and `MeshManager.cs:498` passes the path to the node as a requested
model, where it will fail.

The user's action is accepted, saved, and cannot work. Nothing in the UI says so.

I confirmed this by reading the code. **I did not run it** to observe the exact failure the
mesh produces.

### Major: two settings exist that the UI cannot reach

`DistributedInferenceEnabled` and `DistributedPeers` (`AppConfig.cs:196` and `:208`) have no
UI. They are documented in their own XML comments as hand-edited, which is a deliberate
staging decision. But from a user's point of view the Network tab describes a distributed
system, and the actual switch for the newest distributed feature is invisible. Someone will
turn on the mesh expecting it to affect safetensors models, and nothing will happen.

This is the correct behaviour after the gate was repointed (turning on the GGUF mesh should
not silently reroute safetensors work). It is still a gap between what the UI implies and what
exists.

### Minor: "not reported" columns

The model table's size and throughput columns read "not reported" because the mesh does not
report them. This is the right call and I am listing it only to say I checked it and it is
honest rather than misleading.

---

## What I did not or could not check

Blind spots, so you know where the report is thin:

- **The UI itself.** I read XAML and view models but did not launch the application. Every
  finding about what a user sees is inferred from bindings and code, not observed. Anything
  in section 8 could be contradicted by a label I did not find.
- **Two physical machines.** Everything distributed was verified on loopback only. NAT,
  firewalls, real latency, and cross-machine path agreement are all unverified.
- **A machine with no NVIDIA GPU.** Traced by reading `AcceleratorProbe`; not executed.
- **The installer and uninstaller.** `release.ps1` and `src/LocalNEXUS.Installer` were not
  read in depth and not run. The claim that the venv survives uninstall is inference.
- **`ModelNode.cs`, `MainViewModel.cs`, `MeshManager.cs` in full.** I sampled these; at 2295,
  1480 and 1080 lines I read structure and specific paths rather than every line. There could
  be real defects in the parts I skipped.
- **The eval harness and its claimed scores.** Not run, not audited. The README's 175 to 179
  out of 200 is taken at face value.
- **Extensions and MCP.** `ExtensionHost`, `JsonRpcConnection` and `McpBridgeServer` were only
  touched by the exception-handling sweep. The MCP server is stdio and a named pipe per
  `SECURITY.md`, which I did not verify.
- **Hosted providers.** The Anthropic, Gemini and OpenAI-compatible clients were not reviewed
  for key handling beyond confirming keys go through DPAPI and into an auth header.
- **Whether the five failing tests are the only environment-dependent ones.** I ran the suite
  once, on a machine that has a Python venv, CUDA, and vendor binaries present. A machine
  missing those may fail differently and more.
- **Dependency licensing and the NOTICE file.** Not audited.
