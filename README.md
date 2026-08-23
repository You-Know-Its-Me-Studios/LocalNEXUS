![LocalNEXUS](assets/brand/localnexus-banner.png)

<h1 align="center">LocalNEXUS</h1>

LocalNEXUS wires language models together on a canvas and points them at your codebase. You type what you want, the graph reads your project, writes the code, compiles it, and writes the files. Models run on your hardware, across several machines, or through a hosted provider if you want them to.

Works on any C# codebase. Unity is detected rather than assumed, and a Unity project adds the
write rules that keep a scene from silently losing its scripts. Built it for Unity C# first. Ever
expanding...

![The workspace during a run](docs/images/workspace-mid-run.png)

## Quick start

Two ways in.

Take `LocalNEXUS-setup.exe` from a release and run it. It installs per user, so no elevation prompt, and it fetches whichever engines you tick from their own release pages. Re-run it later to add one you skipped. It is not signed, so SmartScreen will warn you once.

Or take the zip. Unzip, run `LocalNEXUS.exe`, place the engine binaries yourself as described in `vendor/*/README.md`. Self contained, no .NET install needed.

Five minutes to a generated file, assuming a `.gguf` on disk:

1. `File > Open project`. The status bar names the project and says whether it is a Unity
   project or an ordinary C# one, and reports how many C# files it indexed. Unity is detected,
   never asked for, and a Unity project is the only kind the Unity write rules apply to.
2. `File > Settings`, Models section. Drop a `.gguf` into the `models\gguf` folder it names, or point it at a folder of your own. Format is detected by reading the file, not the name.
3. `File > Start from > One model, one file`, or click it on the empty canvas. That is the Prompt, Model and Output graph already wired. To build it yourself instead, double click the canvas to search for a node, or drag a wire out and let go over empty space to be offered only what could connect.
4. Click the Model node, choose Local, pick your model.
5. Click the Output node, set folder and filename. `Assets/Scripts`, `Spinner.cs`.
6. Type into the box under the canvas and press Ctrl+Enter:

> Write a MonoBehaviour called Spinner that rotates its transform around the Y axis at a configurable speed, with a serialized field for the speed.

Nodes light up in turn, the reply streams into the feed, the last line names the file it wrote. Everything else is more nodes in the middle of that.

## Nodes

| Node           | Does                                                                                   |
| -------------- | -------------------------------------------------------------------------------------- |
| Prompt         | Holds what you typed. Feeds Triage or Model                                            |
| Triage         | Reads the project index, ranks existing files, decides edit or create, orders the work |
| Model          | Calls an LLM. Local, mesh, or hosted                                                   |
| Debate         | Two models argue an approach over several rounds, and send on what they settled        |
| Judge          | Reads a debate, or two models arguing separately, and makes the determination          |
| Loop           | Runs everything wired to it once per item in a list, and can stop between items        |
| Reshape        | Reshapes the text going by. Inject standing text, extract what matters, replace, trim  |
| Compiler check | Compiles against the project's real references, hands failures back for repair         |
| Output         | Writes files, subject to the Unity binding rules                                       |

## Why not just use a chat window

Because a chat window cannot see your project, and nothing checks its answer before that answer becomes a file you have to clean up.

The project gets indexed first, so a plan can say edit this rather than always create this. Creating a type that already exists gets refused, and the file holding the original is named. Code is compiled before anything is written, and failures go back to the model to fix. Some changes compile perfectly and still break Unity, like renaming a MonoBehaviour away from the filename Unity binds it by, and those get refused outright.

Files that pass are written as they pass. One that will not compile is kept with its errors while the rest of the plan carries on, instead of the whole run being thrown away. Every run is recorded, and the files a run wrote can be put back.

## How well does it actually work

There is an eval harness in the repo that runs twenty tasks against a real model, ten times each, and scores what landed on disk. Against `qwen2.5-coder-7b-instruct` at Q4, the last few runs came in around 175 to 179 out of 200.

Fourteen of the twenty tasks pass every single time. Zero duplicate types have reached disk in the last four hundred runs, which is the one failure this whole thing exists to prevent.

One task fails every time, and I am not going to pretend otherwise. Asked to move a class into a different namespace, the 7B returns the file byte-identical in twenty-nine attempts out of thirty. It simply does not do it. The plan is correct, the instruction reaches the coder intact, and the model ignores it. That is a model limitation and no amount of app work fixes it.

The rest of the movement between runs is sampling. No seed is set, so a task can swing three or four points either way and mean nothing.

Numbers are in `docs/`, and the harness runs from the command line if you want your own.

## Status

Pre-1.0. Interfaces still move. 317 tests, and the eval above.

Solid: the graph engine, llama.cpp inference, the project index, the Unity rules, per file writes and staging, run history and undo, the interface.

Works, less exercised: safetensors through `transformers serve`, hosted providers, elicitation, Debate and Judge.

Unproven: everything distributed. It has only ever run on one machine talking to itself over loopback. Never across two physical machines, which is embarrassing to still be writing.

## Requirements

Windows 11. No Linux or macOS build.

A GPU is strongly recommended. Developed against an RTX 4080 Laptop with 12 GB. llama.cpp ships Vulkan builds for AMD and Intel, and a CPU build that works, slowly. The installer picks the right one by reading your driver version, and lets you override it.

The app is about 180 MB. The engines vary: llama.cpp is 33 MB for Vulkan and 513 MB for CUDA, because CUDA needs its runtime as a second download. Mesh LLM is 51 MB, uv is 19 MB, both optional. Then whatever your models weigh, which is usually more than all of it put together.

First launch builds a Python environment in the background so safetensors models can be served. Roughly 3 GB on NVIDIA because it pulls a CUDA torch, a few hundred megabytes otherwise. Nothing waits on it and GGUF never touches it. That is the only thing downloaded without you asking.

A 7B class coding model is the realistic floor. Below that you mostly watch the compile check catch things, which the eval confirmed the hard way.

## Distributed inference

![The network tab](docs/images/network.png)

A model too big for one machine can run across several. LocalNEXUS starts a [Mesh LLM](https://github.com/Mesh-LLM/mesh-llm) node, which handles discovery, splits the model into layer stages, and places them wherever there is room.

The Network tab lists what the mesh can serve, how many machines cover each stage, and whether anything is standing by if one drops. You decide what to offer: a switch for the machine, a tick per model, and a memory limit that defaults to leaving a quarter of your card free. Private and LAN only unless you publish it.

Read the status section again before relying on any of this.

## Building from source

.NET 8 SDK, Windows.

```powershell
git clone https://github.com/You-Know-Its-Me-Studios/LocalNEXUS.git
cd LocalNEXUS
dotnet build
.\publish.ps1     # the runnable exe, into dist\
.\release.ps1     # the installer and the zip, into dist\release\
```

The engine binaries are not in the repository. Fetch them into `vendor/` first, or the app builds and runs but cannot do anything. Each folder has a README naming the release to download. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Roadmap

Breakpoints on wires, so a run can be stopped between nodes and a value inspected. Deleting files from the Output node, which writes and edits but cannot remove. Semantic search over run history, which is keyword matching today. And running this across two physical machines, which is the one that has been on the list longest.

The longer goal is a network where people pool compute to run models none of them could run alone. That shapes decisions now. Sources are interchangeable rather than mine and theirs, identity is a persistent public key so reputation can attach later, and coverage is computed properly even with two machines where it always passes.

Trust scoring and contribution economics are deliberately not designed. They need real use first, and guessing at them now would just be guessing.

## Docs

| Doc                                              | Use it for                                         |
| ------------------------------------------------ | -------------------------------------------------- |
| [docs/models.md](docs/models.md)                 | Local and hosted models, the Python runtime        |
| [docs/unity-projects.md](docs/unity-projects.md) | The index, the Unity rules, the compile check      |
| [docs/distributed.md](docs/distributed.md)       | Mesh setup, coverage, contributing compute         |
| [docs/interface.md](docs/interface.md)           | The window, themes, settings                       |
| [docs/architecture.md](docs/architecture.md)     | How it fits together                               |
| [CONTRIBUTING.md](CONTRIBUTING.md)               | Building, conventions, getting the engine binaries |

## Contributing

Issues and pull requests welcome. [CONTRIBUTING.md](CONTRIBUTING.md) covers building from a clean clone and which parts to leave alone. [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) applies. Security goes through [SECURITY.md](SECURITY.md), not a public issue.

## Licence

Apache-2.0. Copyright 2026 You Know Its Me Studios. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

Engine binaries you place in `vendor/` and any model weights carry their own licences.
