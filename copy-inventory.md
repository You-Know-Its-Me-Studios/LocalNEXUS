# Copy inventory

Every string this application shows a person, where it lives, and when it appears.

**1416 strings**, gathered from 155 files. Extracted mechanically and
then classified, so the counts are exact and the "when it shows" column is a reading of the
surrounding code rather than a guess: for XAML it comes from the element, its style, its colour and
what governs its visibility, and for C# from the surface the string is handed to.

Context changes how a line reads, which is why the third column is here. The same sentence is
reassuring under a button and alarming in red.

## What is not here

Log lines, and messages that only ever reach a log file. Anything a converter or a serializer says
to itself. Format strings with no words in them. Icon glyphs, which are pictures written as character codes. Strings assembled at run time out of values are
listed as they appear in the source, so a placeholder like `&#123;Title&#125;` is the value that
gets substituted in.

A note on the counts: a sentence written across several lines of C# is one string here, not four,
because a quarter of a sentence tells nobody how it sounds.

## Contents

- [Window shell](#window-shell) (75)
- [Network tab](#network-tab) (48)
- [Settings](#settings) (155)
- [Node inspector panels](#node-inspector-panels) (202)
- [Run history window](#run-history-window) (7)
- [Extensions window](#extensions-window) (31)
- [Views, shared templates](#views-shared-templates) (188)
- [Nodes, at run time](#nodes-at-run-time) (214)
- [View models](#view-models) (150)
- [Services](#services) (311)
- [Application](#application) (35)


## Window shell


### `src/LocalNEXUS.App/Views/Shell/ActivityBarView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Settings | line 31, `Button` `ToolTip` | Tooltip, on hover |
| Run history: every run this project has had, and putting one back. | line 46, `Button` `ToolTip` | Tooltip, on hover |
| Extensions: what this project adds to the application. | line 60, `Button` `ToolTip` | Tooltip, on hover |
| Workspace: the canvas, its run outline and the node inspector. | line 66, `Grid` `ToolTip` | Tooltip, on hover |
| Spec: changes, their artifacts and what state each one is in. | line 87, `Grid` `ToolTip` | Tooltip, on hover |
| Network: what the mesh can serve, and what this machine contributes. | line 96, `Grid` `ToolTip` | Tooltip, on hover |

### `src/LocalNEXUS.App/Views/Shell/BottomPanelView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Problems | line 33, `TextBlock` `Text` | Static label |
| Activity | line 49, `TextBlock` `Text` | Static label |
| Open every entry | line 63, `Button` `ToolTip` | Tooltip, on hover |
| Close every entry | line 70, `Button` `ToolTip` | Tooltip, on hover |
| Copy everything in this panel | line 77, `Button` `ToolTip` | Tooltip, on hover |
| Clear the transcript | line 83, `Button` `ToolTip` | Tooltip, on hover |
| Hide the panel | line 89, `Button` `ToolTip` | Tooltip, on hover |
| No problems with the generated code. | line 156, `TextBlock` `Text` | Static label |

### `src/LocalNEXUS.App/Views/Shell/EditorAreaView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| New conversation | line 88, `Button` `Content` | Button label |
| Previous conversations are kept in run history. | line 89, `Button` `ToolTip` | Tooltip, on hover |
| Conversation | line 94, `TextBlock` `Text` | Hint under a control or section |
| Proceed without answering | line 146, `Button` `Content` | Button label |
| Skips ahead with a stated assumption. | line 147, `Button` `ToolTip` | Tooltip, on hover |
| The run is waiting on an answer | line 153, `TextBlock` `Text` | Warning text, shown when something needs attention |
| Your next message is read as the answer, not a new request. | line 159, `TextBlock` `Text` | Hint under a control or section |
| Release unchanged | line 193, `Button` `Content` | Button label |
| Carry on with the value exactly as it arrived. | line 194, `Button` `ToolTip` | Tooltip, on hover |
| Continue | line 198, `Button` `Content` | Button label |
| Carry on with whatever the box now holds. | line 199, `Button` `ToolTip` | Tooltip, on hover |
| Discard all | line 245, `Button` `Content` | Button label |
| Forgets every attempt below. No file on disk is touched. | line 246, `Button` `ToolTip` | Tooltip, on hover |
| These files did not compile. Nothing on disk was changed. Open one to see what was attempted, or discard it. | line 259, `TextBlock` `Text` | Hint under a control or section |
| Discard | line 275, `Button` `Content` | Button label |
| Forgets this one attempt. The file on disk is not touched. | line 276, `Button` `ToolTip` | Tooltip, on hover |
| What was attempted | line 297, `Expander` `Header` | Section header, on a collapsible section |
| Describe a request, then press Run or Ctrl+Enter. | line 368, `TextBlock` `Text` | Static label |
| Search the web | line 384, `CheckBox` `Content` | Checkbox label |
| Applies to every Model node in this run. Snippets only; no page is fetched. | line 386, `CheckBox` `ToolTip` | Tooltip, on hover |
| Sends what is in the box. Ctrl+Enter does the same. | line 398, `Button` `ToolTip` | Tooltip, on hover |
| Add node | line 432, `MenuItem` `Header` | Menu item |
| Delete selected | line 445, `MenuItem` `Header` | Menu item |
| Nothing on the canvas yet | line 468, `TextBlock` `Text` | Static label |
| Pick a template or double click to start from scratch. | line 477, `TextBlock` `Text` | Hint under a control or section |
| Puts these away and leaves the canvas empty. Double click anywhere to add a node. | line 507, `Button` `ToolTip` | Tooltip, on hover |
| Build on my own | line 511, `TextBlock` `Text` | Static label |
| Nothing matches. Every node type is listed here, so there is no node by that name. | line 572, `TextBlock` `Text` | Empty state, shown when there is nothing to list |

### `src/LocalNEXUS.App/Views/Shell/InspectorView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Select nothing | line 79, `Button` `ToolTip` | Tooltip, on hover |
| Title | line 92, `TextBlock` `Text` | Field label, above an input |

### `src/LocalNEXUS.App/Views/Shell/NetworkSideBarView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| CONNECTED TO | line 33, `TextBlock` `Text` | Static label |
| Nothing. Start the node to host your own mesh, or join one. | line 63, `TextBlock` `Text` | Hint under a control or section |
| MACHINES | line 69, `TextBlock` `Text` | Static label |

### `src/LocalNEXUS.App/Views/Shell/SpecView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Refresh | line 40, `Button` `Content` | Button label |
| Changes | line 43, `TextBlock` `Text` | Static label |
| Archived | line 76, `TextBlock` `Text` | Hint under a control or section |
| Advance | line 101, `Button` `Content` | Button label |
| Ask OpenSpec to write the next artifact that is ready. | line 102, `Button` `ToolTip` | Tooltip, on hover |
| Send tasks to Workspace | line 106, `Button` `Content` | Button label |
| Put this change's task list in the request box, whole. Nothing runs until you press Run there. | line 107, `Button` `ToolTip` | Tooltip, on hover |
| Choose a change on the left, then an artifact, to read it here. | line 163, `TextBlock` `Text` | Hint under a control or section |

### `src/LocalNEXUS.App/Views/Shell/StatusBarView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| The state of the current or most recent run. | line 34, `StackPanel` `ToolTip` | Tooltip, on hover |
| Mesh | line 55, `TextBlock` `Text` | Static label |
| Python | line 65, `TextBlock` `Text` | Static label |
| Files an earlier run could not finish. Say what to do about them in the box below. | line 79, `StackPanel` `ToolTip` | Tooltip, on hover |

### `src/LocalNEXUS.App/Views/Shell/WorkspaceSideBarView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| RUN OUTLINE | line 25, `TextBlock` `Text` | Static label |
| Unsaved changes | line 51, `TextBlock` `ToolTip` | Tooltip, on hover |
| faulted | line 97, `TextBlock` `Text` | Error text, shown when something has gone wrong |
| skipped | line 104, `TextBlock` `Text` | Value shown in monospace |

### `src/LocalNEXUS.App/Views/Shell/WorkspaceWelcomeView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| LocalNEXUS | line 28, `TextBlock` `Text` | Static label |
| A graph works inside a project: it reads what is there, checks what it writes against it, and writes into it. Pick one to start. | line 35, `TextBlock` `Text` | Hint under a control or section |
| Open a project | line 50, `TextBlock` `Text` | Static label |
| Choose a folder that already holds a codebase. | line 54, `TextBlock` `Text` | Hint under a control or section |
| Start a project in a folder | line 63, `TextBlock` `Text` | Static label |
| Choose an empty folder, or make a new one in the picker. You will be asked what goes where next. | line 67, `TextBlock` `Text` | Hint under a control or section |
| I am only contributing this machine | line 80, `TextBlock` `Text` | Static label |
| Lend this machine's GPU to the mesh without opening anything. Goes straight to Network. | line 84, `TextBlock` `Text` | Hint under a control or section |
| Recent | line 91, `TextBlock` `Text` | Static label |
| Remove | line 107, `Button` `Content` | Button label |
| Takes it off this list. Nothing on disk is touched. | line 108, `Button` `ToolTip` | Tooltip, on hover |
| Getting started | line 153, `TextBlock` `Text` | Static label |

## Network tab


### `src/LocalNEXUS.App/Views/NetworkView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Host settings | line 111, `Button` `Content` | Button label |
| Mesh name, publishing, what this machine offers, and the invite. | line 112, `Button` `ToolTip` | Tooltip, on hover |
| Join with an invite | line 148, `Button` `Content` | Button label |
| Asks the public directory which meshes are out there. Nothing about this machine is published by asking. | line 156, `Button` `ToolTip` | Tooltip, on hover |
| Search models | line 171, `TextBox` `Placeholder` | Placeholder, in an empty text box |
| Turn on | line 193, `Button` `Content` | Button label |
| Node is off. Everything runs locally. | line 200, `TextBlock` `Text` | Static label |
| A mesh pools GPUs across machines to run a model none could run alone. It is optional, and it is slower, not faster. | line 206, `TextBlock` `Text` | Hint under a control or section |
| Host a mesh | line 211, `Button` `Content` | Button label |
| Join with an invite | line 217, `Button` `Content` | Button label |
| Browse public meshes | line 223, `Button` `Content` | Button label |
| MODEL | line 257, `Button` `Content` | Button label |
| COVERAGE | line 259, `Button` `Content` | Button label |
| The model split into parts. A filled block means a machine is running that part. | line 260, `Button` `ToolTip` | Tooltip, on hover |
| STATUS | line 262, `Button` `Content` | Button label |
| MACHINES | line 264, `Button` `Content` | Button label |
| The mesh has not reported how this model is assembled yet. | line 318, `Border` `ToolTip` | Tooltip, on hover |
| Meshes | line 373, `TextBlock` `Text` | Static label |
| Close | line 446, `Button` `ToolTip` | Tooltip, on hover |
| Host settings | line 450, `TextBlock` `Text` | Static label |
| Changes apply on restart. | line 467, `TextBlock` `Text` | Hint under a control or section |
| Mesh name | line 480, `TextBlock` `Text` | Field label, above an input |
| A name for the mesh you host | line 482, `TextBox` `Placeholder` | Placeholder, in an empty text box |
| Ignored while you are joined to someone else's. | line 488, `TextBlock` `Text` | Hint under a control or section |
| Publish this mesh publicly | line 493, `CheckBox` `Content` | Checkbox label |
| Off: only this network can find it. On: listed publicly, but joining still needs the invite. | line 499, `TextBlock` `Text` | Warning text, shown when something needs attention |
| Offer this machine | line 503, `CheckBox` `Content` | Checkbox label |
| Memory | line 506, `TextBlock` `Text` | Field label, above an input |
| Share all of it | line 539, `CheckBox` `Content` | Checkbox label |
| Models | line 544, `TextBlock` `Text` | Field label, above an input |
| Nothing is shared until you tick one. | line 548, `TextBlock` `Text` | Hint under a control or section |
| No models found on this machine. Add a folder in Settings, Models. | line 590, `TextBlock` `Text` | Hint under a control or section |
| Port | line 593, `TextBlock` `Text` | Field label, above an input |
| Invite | line 596, `TextBlock` `Text` | Field label, above an input |
| Start the node to get one. | line 606, `TextBlock` `Text` | Hint under a control or section |
| Copy | line 612, `Button` `Content` | Button label |
| Rotates this node's key, which makes every invite handed out so far stop working. | line 616, `Button` `ToolTip` | Tooltip, on hover |
| Join another mesh | line 621, `TextBlock` `Text` | Field label, above an input |
| Paste invite token | line 624, `TextBox` `Placeholder` | Placeholder, in an empty text box |
| Join it | line 628, `Button` `Content` | Button label |
| Distributed inference | line 656, `TextBlock` `Text` | Static label |
| For a safetensors model too large for this machine. Its layers are split across the machines listed below, each running the distributed peer from a command line. | line 664, `TextBlock` `Text` | Hint under a control or section |
| Split large safetensors models across machines | line 666, `CheckBox` `Content` | Checkbox label |
| Shared secret | line 669, `TextBlock` `Text` | Field label, above an input |
| The same value on every machine. A peer refuses to listen on an address the network can reach without one. Leave blank only when every machine is this one. | line 673, `TextBlock` `Text` | Hint under a control or section |
| Machines | line 677, `TextBlock` `Text` | Field label, above an input |
| Remove | line 686, `Button` `Content` | Button label |
| Takes it off the list. Nothing on that machine is stopped. | line 687, `Button` `ToolTip` | Tooltip, on hover |

## Settings


### `src/LocalNEXUS.App/Views/SettingsView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| SETTINGS | line 34, `TextBlock` `Text` | Static label |
| Appearance | line 59, `TextBlock` `Text` | Section header |
| Pick a theme. It applies right away. | line 61, `TextBlock` `Text` | Hint under a control or section |
| See through the window | line 92, `TextBlock` `Text` | Section header |
| See through | line 114, `TextBlock` `Text` | Hint under a control or section |
| Solid | line 115, `TextBlock` `Text` | Hint under a control or section |
| The slider stops short of fully clear. Past that point, whatever is behind the window makes the text unreadable. | line 121, `TextBlock` `Text` | Hint under a control or section |
| a panel stays solid | line 156, `TextBlock` `Text` | Static label |
| How states look | line 171, `TextBlock` `Text` | Section header |
| pending | line 182, `TextBlock` `Text` | Static label |
| working | line 186, `TextBlock` `Text` | Static label |
| healthy | line 190, `TextBlock` `Text` | Confirmation text, shown when something worked |
| thin | line 194, `TextBlock` `Text` | Warning text, shown when something needs attention |
| failed | line 198, `TextBlock` `Text` | Error text, shown when something has gone wrong |
| Assets/Scripts/PlayerMovement.cs | line 205, `TextBlock` `Text` | Value shown in monospace |
| the node accents | line 225, `TextBlock` `Text` | Hint under a control or section |
| Models | line 233, `TextBlock` `Text` | Section header |
| Add a whole folder to search, or add one model on its own. GGUF and safetensors both show up in the same list. | line 235, `TextBlock` `Text` | Hint under a control or section |
| Add model file | line 245, `Button` `Content` | Button label |
| One GGUF or safetensors file. | line 246, `Button` `ToolTip` | Tooltip, on hover |
| Add model folder | line 251, `Button` `Content` | Button label |
| One safetensors model: a folder holding config.json beside its weights. | line 252, `Button` `ToolTip` | Tooltip, on hover |
| Search a folder | line 257, `Button` `Content` | Button label |
| Search this folder and everything under it for models. | line 258, `Button` `ToolTip` | Tooltip, on hover |
| Rescan | line 263, `Button` `Content` | Button label |
| Edit model-paths.txt | line 267, `Button` `Content` | Button label |
| Find a model to download | line 276, `Expander` `Header` | Section header, on a collapsible section |
| Searches Hugging Face for GGUF models. No account and no sign in. A repository that requires one is shown as gated, with a link. | line 281, `TextBlock` `Text` | Hint under a control or section |
| Search | line 288, `Button` `Content` | Button label |
| qwen2.5-coder, llama-3.1, mistral | line 293, `TextBox` `Placeholder` | Placeholder, in an empty text box |
| Open the page | line 311, `Button` `Content` | Button label |
| Files | line 323, `Button` `Content` | Button label |
| One part of a model split across several files. Downloading this alone does not give you a model. | line 401, `TextBlock` `Text` | Warning text, shown when something needs attention |
| Discard | line 409, `Button` `Content` | Button label |
| Stops and deletes what has been downloaded so far. | line 410, `Button` `ToolTip` | Tooltip, on hover |
| Stop | line 417, `Button` `Content` | Button label |
| Stops, and keeps what arrived so it can carry on later. | line 418, `Button` `ToolTip` | Tooltip, on hover |
| Search history by meaning | line 445, `Expander` `Header` | Section header, on a collapsible section |
| Keyword search finds only the words that were written: a search for the thing that spawns enemies finds nothing when what was written was add a wave spawner. Comparing meanings closes that gap. It needs a small model on this machine, and nothing leaves the machine. | line 452, `TextBlock` `Text` | Hint under a control or section |
| Choose a model | line 468, `Button` `Content` | Button label |
| Turns it on. Any GGUF embedding model on this machine. | line 469, `Button` `ToolTip` | Tooltip, on hover |
| Index the history | line 474, `Button` `Content` | Button label |
| One pass over the runs already recorded. New runs are indexed as they finish. | line 475, `Button` `ToolTip` | Tooltip, on hover |
| Stop | line 482, `Button` `Content` | Button label |
| Turn off | line 488, `Button` `Content` | Button label |
| Back to keyword search. The vectors are kept. | line 489, `Button` `ToolTip` | Tooltip, on hover |
| Delete the index | line 494, `Button` `Content` | Button label |
| Throws away every vector. Indexing again rebuilds them. | line 495, `Button` `ToolTip` | Tooltip, on hover |
| Where models come from | line 514, `TextBlock` `Text` | Field label, above an input |
| Remove | line 525, `Button` `Content` | Button label |
| API keys | line 578, `TextBlock` `Text` | Section header |
| Every key this application holds, encrypted for this Windows account. Nothing here is bundled and nothing is sent anywhere but the provider it belongs to. | line 581, `TextBlock` `Text` | Hint under a control or section |
| Models | line 583, `TextBlock` `Text` | Section header |
| Use the accounts you already pay for. Keys are encrypted for this Windows account and never written into a saved graph, so a graph can be shared without taking your key with it. | line 586, `TextBlock` `Text` | Hint under a control or section |
| Save | line 615, `Button` `Content` | Button label |
| Clear | line 619, `Button` `Content` | Button label |
| Get a key | line 625, `Button` `Content` | Button label |
| Remove | line 631, `Button` `Content` | Button label |
| Add another endpoint | line 645, `TextBlock` `Text` | Section header |
| Anything that speaks the OpenAI API works here, listed above or not. | line 648, `TextBlock` `Text` | Hint under a control or section |
| Search providers | line 668, `TextBlock` `Text` | Section header |
| Five dollars of credit a month, then charged per thousand requests | line 687, `TextBlock` `Text` | Hint under a control or section |
| Brave | line 690, `TextBlock` `Text` | Static label |
| Save | line 710, `Button` `Content` | Button label |
| Clear | line 714, `Button` `Content` | Button label |
| Get a key | line 719, `Button` `Content` | Button label |
| The key is yours and is stored encrypted for this Windows account, beside the model keys. Nothing is bundled, because a bundled key would bill every installation's searches to one account. | line 736, `TextBlock` `Text` | Hint under a control or section |
| Reading images | line 744, `TextBlock` `Text` | Section header |
| Paste or drop an image on the request box and this reads it into text, which joins your request. The image never enters the graph, so the coding model does not have to be able to see. | line 747, `TextBlock` `Text` | Hint under a control or section |
| A vision model on this machine | line 754, `TextBlock` `Text` | Field label, above an input |
| A vision model from your model folders. This application starts it when you paste an image. | line 766, `ComboBox` `ToolTip` | Tooltip, on hover |
| Started the first time you paste an image. A vision model ships as two files, the weights and an mmproj projector. Keep both in the same folder and the projector is found on its own; without it, the model loads and then refuses every image. | line 783, `TextBlock` `Text` | Hint under a control or section |
| Use an address instead | line 788, `Button` `Content` | Button label |
| Or a hosted one | line 791, `TextBlock` `Text` | Field label, above an input |
| For a hosted model, or for a server you are already running yourself. Anything that speaks the OpenAI API and can see. | line 795, `TextBlock` `Text` | Hint under a control or section |
| Address, such as https://api.openai.com/v1 | line 799, `TextBox` `Placeholder` | Placeholder, in an empty text box |
| The base url of an OpenAI compatible server that can see. | line 801, `TextBox` `ToolTip` | Tooltip, on hover |
| Model id at that address, such as gpt-4o-mini | line 804, `TextBox` `Placeholder` | Placeholder, in an empty text box |
| Which model at that address reads images. | line 806, `TextBox` `ToolTip` | Tooltip, on hover |
| Save key | line 811, `Button` `Content` | Button label |
| API key for that address, if it needs one | line 816, `PasswordBox` `Placeholder` | Placeholder, in an empty text box |
| Warn before an expensive run | line 823, `TextBlock` `Text` | Section header |
| Ask above | line 826, `TextBlock` `Text` | Field label, above an input |
| Dollars. A run is measured against a ceiling: the whole input plus the most each node is allowed to write. The real cost is usually lower, and can be higher. Set it to zero to never be asked. Local models cost nothing and never trigger it. | line 833, `TextBlock` `Text` | Hint under a control or section |
| Project | line 838, `TextBlock` `Text` | Section header |
| Where generated code goes | line 848, `TextBlock` `Text` | Field label, above an input |
| Relative to the project root. This is what a newly added Output node starts from. It does not move anything in a graph you have already saved, because that value belongs to the node and was saved with it; open the node and change it there. | line 856, `TextBlock` `Text` | Hint under a control or section |
| Project kind | line 858, `TextBlock` `Text` | Field label, above an input |
| Answer MCP tool calls for this project | line 867, `TextBlock` `Text` | Field label, above an input |
| Allow tool calls while this project is open | line 870, `CheckBox` `Content` | Checkbox label |
| Both this and the switch in Runtime have to be on. That one decides whether this installation answers at all; this one decides whether it answers about this project. | line 874, `TextBlock` `Text` | Hint under a control or section |
| Share these settings | line 876, `TextBlock` `Text` | Field label, above an input |
| Commit the folder and the project kind with the repository | line 879, `CheckBox` `Content` | Checkbox label |
| No project is open, so there is nothing to set here yet. Open one from the File menu, or from the window that appears at startup. | line 889, `TextBlock` `Text` | Hint under a control or section |
| Open project | line 896, `TextBlock` `Text` | Field label, above an input |
| Change it from File, Open project. | line 898, `TextBlock` `Text` | Hint under a control or section |
| What is known about it | line 900, `TextBlock` `Text` | Field label, above an input |
| Read the project again | line 916, `Button` `Content` | Button label |
| Only needed if something outside the editor changed a lot of files at once. | line 920, `TextBlock` `Text` | Hint under a control or section |
| Runtime | line 925, `TextBlock` `Text` | Section header |
| Python environment | line 927, `TextBlock` `Text` | Section header |
| Repair | line 960, `Button` `Content` | Button label |
| Set up again | line 961, `Button` `Content` | Button label |
| Open its folder | line 962, `Button` `Content` | Button label |
| Only needed to run safetensors models. GGUF models work without it. | line 966, `TextBlock` `Text` | Hint under a control or section |
| Mesh node | line 977, `TextBlock` `Text` | Section header |
| All of it is in the Network tab: what the mesh is called, joining somebody else's, what this machine shares, publishing, and starting and stopping the node. | line 981, `TextBlock` `Text` | Hint under a control or section |
| Answer MCP tool calls | line 991, `TextBlock` `Text` | Section header |
| Let other tools open a project, open a graph and run it | line 994, `CheckBox` `Content` | Checkbox label |
| A local pipe, never a network port. A caller can run a graph, which writes files into the open project through the same rules a person's run goes through. It cannot write a file any other way and cannot read a stored key. | line 1002, `TextBlock` `Text` | Hint under a control or section |
| This switch and the one in Project both have to be on. This one decides whether this installation answers at all; that one decides whether it answers about the project you have open. | line 1006, `TextBlock` `Text` | Hint under a control or section |
| Where this install keeps its own files | line 1010, `TextBlock` `Text` | Section header |
| Engine logs, the model catalogue, the Python runtime and this application's configuration. Nothing a project owns is in here. | line 1013, `TextBlock` `Text` | Hint under a control or section |
| Open this install's data folder | line 1018, `Button` `Content` | Button label |
| Extensions | line 1035, `TextBlock` `Text` | Section header |
| Extensions add tools a model can call and node types the graph can run. A bad one cannot break the application, and they belong to the open project because what they talk to does. | line 1038, `TextBlock` `Text` | Hint under a control or section |
| Manage extensions | line 1050, `TextBlock` `Text` | Static label |
| History | line 1064, `TextBlock` `Text` | Section header |
| Every run is written to a database inside the open project, as it happens. Nothing is summarised and nothing is held in memory: the record stays whole and is searched from the history window. | line 1067, `TextBlock` `Text` | Hint under a control or section |
| How much to keep | line 1079, `TextBlock` `Text` | Section header |
| Two things are recorded for every run. The transcript, which is what happened in words, and a snapshot of each file before it was changed, which is the only thing that makes a run undoable. Transcripts are small and are kept forever; snapshots are whole copies of your files, so they are the ones with limits. | line 1082, `TextBlock` `Text` | Hint under a control or section |
| A run past either limit loses its snapshots and keeps its transcript. You can still read everything it did; you can no longer undo it. | line 1086, `TextBlock` `Text` | Hint under a control or section |
| Runs that keep their snapshots | line 1088, `TextBlock` `Text` | Field label, above an input |
| The most recent this many runs stay undoable. Older ones give up their snapshots however new they are. | line 1095, `TextBlock` `Text` | Hint under a control or section |
| Days to keep a snapshot | line 1097, `TextBlock` `Text` | Field label, above an input |
| A snapshot older than this goes, even if the run is one of the most recent. Whichever limit is reached first wins. | line 1104, `TextBlock` `Text` | Hint under a control or section |
| Both are applied on their own at the end of every run, so under ordinary use there is nothing here to press. | line 1109, `TextBlock` `Text` | Hint under a control or section |
| What it is using | line 1111, `TextBlock` `Text` | Section header |
| Recount | line 1120, `Button` `Content` | Button label |
| Measure the database again. It does not change anything. | line 1121, `Button` `ToolTip` | Tooltip, on hover |
| Delete old snapshots | line 1126, `Button` `Content` | Button label |
| Applies the two limits above right now, instead of waiting for the next run to do it. | line 1127, `Button` `ToolTip` | Tooltip, on hover |
| Delete all snapshots | line 1132, `Button` `Content` | Button label |
| Every snapshot, whatever the limits say. Transcripts are kept. | line 1133, `Button` `ToolTip` | Tooltip, on hover |
| Delete everything | line 1138, `Button` `Content` | Button label |
| Every transcript and every snapshot for this project. | line 1139, `Button` `ToolTip` | Tooltip, on hover |
| What undo reaches | line 1145, `TextBlock` `Text` | Section header |
| Undo puts back only the files this application wrote or edited during a run. Anything a build or an editor regenerated, an extension changed, or you edited by hand is invisible to it, and putting a file back also discards whatever was done to it since. This is run undo, not version control. | line 1149, `TextBlock` `Text` | Warning text, shown when something needs attention |
| Behaviour | line 1153, `TextBlock` `Text` | Section header |
| Starting values for nodes you add from here on. Every one of them can be changed on the node itself, and a graph you have already saved keeps whatever was set on it, so nothing here reaches backwards. | line 1156, `TextBlock` `Text` | Hint under a control or section |
| Compiler check | line 1158, `TextBlock` `Text` | Section header |
| The Compiler check node compiles what a model wrote before anything is written to disk. When it does not build, the errors go back to the model that produced the code and it tries again. | line 1161, `TextBlock` `Text` | Hint under a control or section |
| Repair attempts after the first failure | line 1163, `TextBlock` `Text` | Field label, above an input |
| How many more times to hand the errors back before giving up. Zero checks the code and reports what is wrong without trying to fix it. Each attempt is another request to the model, so this is the setting that decides how long a bad first answer takes to resolve. | line 1167, `TextBlock` `Text` | Hint under a control or section |
| Plan context budget | line 1169, `TextBlock` `Text` | Section header |
| How much of your project the Triage node is allowed to put in front of a model when it works out what to change. A model can only read so much at once, so this is a deliberate allowance split three ways rather than an attempt to send everything. | line 1172, `TextBlock` `Text` | Hint under a control or section |
| All three are in characters, roughly four characters to a token. Raising them costs money on a hosted model and time on a local one. Anything that does not fit is dropped in rank order and the run says what was dropped, so a budget that is too small is visible rather than silent. | line 1176, `TextBlock` `Text` | Hint under a control or section |
| Project map | line 1188, `TextBlock` `Text` | Field label, above an input |
| One line per type in the whole project, so the model knows what already exists and does not write a second copy of it. | line 1193, `TextBlock` `Text` | Hint under a control or section |
| Candidate detail | line 1197, `TextBlock` `Text` | Field label, above an input |
| The actual contents of the files the request looks like it is about. The largest of the three, because this is what a change is written against. | line 1202, `TextBlock` `Text` | Hint under a control or section |
| Signatures this run | line 1206, `TextBlock` `Text` | Field label, above an input |
| What earlier files in this same run declared, so the fifth file can call into the first by its real name instead of guessing. | line 1211, `TextBlock` `Text` | Hint under a control or section |
| Candidate files offered before any is read | line 1215, `TextBlock` `Text` | Field label, above an input |
| How many files get shortlisted by ranking before any of them is opened. Only what survives the shortlist spends any of the candidate detail budget above. Files you never mentioned but which the work depends on still make the list. | line 1219, `TextBlock` `Text` | Hint under a control or section |

## Node inspector panels


### `src/LocalNEXUS.App/Views/Settings/AgentNodeSettingsView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Wire a Model node into the Model pin and type a request. It works out the steps itself: reading and writing files, compiling, searching the project, and using whatever tools that Model node has selected. Writes go through the same project rules the Output node uses. | line 64, `TextBlock` `Text` | Hint under a control or section |
| Limit | line 66, `TextBlock` `Text` | Section header |
| Turns | line 68, `TextBlock` `Text` | Field label, above an input |
| One turn is one model call and the tools it asked for | line 70, `TextBox` `ToolTip` | Tooltip, on hover |
| The budget for the whole task, not per file. If it runs out, anything already written stays written and the run reports where it stopped. | line 73, `TextBlock` `Text` | Hint under a control or section |
| Prompt | line 87, `TextBlock` `Text` | Section header |
| System prompt | line 89, `TextBlock` `Text` | Field label, above an input |
| How this agent is told to work. What each tool does is described on the tool, so this only covers how to go about the job. | line 96, `TextBlock` `Text` | Hint under a control or section |

### `src/LocalNEXUS.App/Views/Settings/CompilerCheckNodeSettingsView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| What it can check against | line 17, `TextBlock` `Text` | Section header |
| The set is incomplete, so a type this project defines will read as a type that does not exist. Errors that a missing reference could explain are marked, and a check whose every error is one of those is reported as inconclusive rather than failed. Nothing is repaired on the strength of them. | line 42, `TextBlock` `Text` | Warning text, shown when something needs attention |
| Repair | line 46, `TextBlock` `Text` | Section header |
| Retry limit | line 48, `TextBlock` `Text` | Field label, above an input |
| How many times to ask the model to fix its own code. Each attempt is another model call. | line 51, `TextBlock` `Text` | Hint under a control or section |
| When it still does not compile | line 53, `TextBlock` `Text` | Section header |
| Leave it for later and carry on | line 56, `RadioButton` `Content` | Button label |
| Fault the run | line 59, `RadioButton` `Content` | Button label |
| Continue with a warning | line 62, `RadioButton` `Content` | Button label |
| Leaving it for later stages the file with its errors, writes the ones that did compile, and runs the rest of the plan. Fault stops the run and writes nothing further. Continue sends the broken code on to be written anyway. | line 69, `TextBlock` `Text` | Hint under a control or section |
| Last check | line 71, `TextBlock` `Text` | Section header |
| Catches code that will not build, including a member the model invented on a type that does not have it. Put it between the model and the Output node; nothing is written until it passes. | line 112, `TextBlock` `Text` | Hint under a control or section |

### `src/LocalNEXUS.App/Views/Settings/DebateNodeSettingsView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Two models argue about how to approach the subject on the Text pin, over several rounds, each reading what the other actually said. What comes out is a brief, not code: wire a Model node after it to write the implementation. | line 13, `TextBlock` `Text` | Hint under a control or section |
| Model A | line 16, `TextBlock` `Text` | Section header |
| What it does | line 18, `TextBlock` `Text` | Field label, above an input |
| Debate | line 20, `RadioButton` `Content` | Button label |
| Argue the position it believes is right, and change its mind when the other is better. | line 22, `RadioButton` `ToolTip` | Tooltip, on hover |
| Defend | line 24, `RadioButton` `Content` | Button label |
| Make the case for the proposal and answer what is said against it. | line 27, `RadioButton` `ToolTip` | Tooltip, on hover |
| Criticize | line 29, `RadioButton` `Content` | Button label |
| Find where the proposal breaks. | line 32, `RadioButton` `ToolTip` | Tooltip, on hover |
| What it argues from | line 36, `TextBlock` `Text` | Field label, above an input |
| The codebase | line 38, `RadioButton` `Content` | Button label |
| The patterns already in the open project, read from the index. | line 40, `RadioButton` `ToolTip` | Tooltip, on hover |
| Its own reasoning | line 42, `RadioButton` `Content` | Button label |
| What the model knows, without being shown the project. | line 45, `RadioButton` `ToolTip` | Tooltip, on hover |
| Model B | line 50, `TextBlock` `Text` | Section header |
| What it does | line 52, `TextBlock` `Text` | Field label, above an input |
| Debate | line 54, `RadioButton` `Content` | Button label |
| Defend | line 57, `RadioButton` `Content` | Button label |
| Criticize | line 61, `RadioButton` `Content` | Button label |
| What it argues from | line 67, `TextBlock` `Text` | Field label, above an input |
| The codebase | line 69, `RadioButton` `Content` | Button label |
| Its own reasoning | line 72, `RadioButton` `Content` | Button label |
| One arguing from the project and one from what is generally right is the pairing worth having. Both may debate, but two defenders never disagree and two critics never propose anything, so those two pairings are refused before the run starts. | line 81, `TextBlock` `Text` | Hint under a control or section |
| When it has settled | line 84, `TextBlock` `Text` | Section header |
| Agreement needed | line 86, `TextBlock` `Text` | Field label, above an input |
| Two numbers are reported each round. Each model says how far it has come, and that drifts optimistic because models tend to agree. The other is measured from what the two positions actually name and propose. Only the measured number decides. A wide gap between the two means the models are being agreeable rather than agreeing. | line 110, `TextBlock` `Text` | Hint under a control or section |
| The measurement is arithmetic, not another model. It counts the types, members and files both sides named, and the verbs of what they propose doing. Naming the same type and proposing opposite things counts as disagreement, which shared words alone would score as agreement. Every round writes its working to the Activity panel: what both named, what only one named, and what contradicted. | line 115, `TextBlock` `Text` | Hint under a control or section |
| Stop after | line 117, `TextBlock` `Text` | Field label, above an input |
| Minutes and seconds, for example 05:00. However the debate is going, it stops there. It also stops after six rounds whatever the clock says, because two models that have not come together by the sixth are not going to. | line 123, `TextBlock` `Text` | Hint under a control or section |
| Which model writes it up and judges | line 125, `TextBlock` `Text` | Field label, above an input |
| Model A | line 127, `RadioButton` `Content` | Button label |
| Model B | line 130, `RadioButton` `Content` | Button label |
| One of the two writes the final brief, and decides when they do not agree and a judge is set to. It reads both positions, so either will do; pick whichever you trust more to write clearly. | line 138, `TextBlock` `Text` | Hint under a control or section |
| When they do not agree | line 141, `TextBlock` `Text` | Section header |
| Let a judge decide and carry on | line 144, `RadioButton` `Content` | Button label |
| Stop and ask me | line 148, `RadioButton` `Content` | Button label |
| Asking is right when somebody is watching. A run that stops for a question is worth nothing to somebody who walked away, which is why letting a judge decide is the default. Asking and then getting no answer falls back to the judge anyway. | line 155, `TextBlock` `Text` | Hint under a control or section |
| How the judge resolves it | line 157, `TextBlock` `Text` | Field label, above an input |
| Combine both | line 159, `RadioButton` `Content` | Button label |
| Choose a side | line 163, `RadioButton` `Content` | Button label |
| Decide independently | line 167, `RadioButton` `Content` | Button label |
| Combining is the default here, because the rounds have already surfaced both positions and something usable has to come out. A Judge node wired on the canvas defaults the other way, since wiring one deliberately is asking for a determination rather than an average. | line 174, `TextBlock` `Text` | Hint under a control or section |
| Last debate | line 177, `TextBlock` `Text` | Section header |
| Every round is written to the Activity panel as it happens, and into the run record with it, so the whole argument is readable afterwards in the history window and not only the verdict. | line 186, `TextBlock` `Text` | Hint under a control or section |

### `src/LocalNEXUS.App/Views/Settings/JudgeNodeSettingsView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Reads what arrives and makes the determination. Wire a debate into the Text pin for a third opinion however well its models agreed, or two model nodes into Text and Second for a plain second opinion with none of the rounds. | line 13, `TextBlock` `Text` | Hint under a control or section |
| How it decides | line 15, `TextBlock` `Text` | Section header |
| Decide independently | line 18, `RadioButton` `Content` | Button label |
| Read both, then write its own, informed by both and bound to neither. | line 21, `RadioButton` `ToolTip` | Tooltip, on hover |
| Choose a side | line 23, `RadioButton` `Content` | Button label |
| Pick the better position and write it up. | line 26, `RadioButton` `ToolTip` | Tooltip, on hover |
| Combine both | line 28, `RadioButton` `Content` | Button label |
| Merge the two into one position. | line 30, `RadioButton` `ToolTip` | Tooltip, on hover |
| Deciding independently is the default. Wiring a judge deliberately is asking for a determination rather than an arbitration: choosing a side throws away half the reasoning that was just paid for, and combining tends to produce a position neither model would defend. The fallback judge inside a debate defaults to combining, for exactly the opposite reason. | line 37, `TextBlock` `Text` | Hint under a control or section |
| What it said | line 39, `TextBlock` `Text` | Section header |

### `src/LocalNEXUS.App/Views/Settings/LoopNodeSettingsView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Runs everything wired to the Item pin once for each entry in the list arriving on Text. Anything that is not a list counts as one item. | line 18, `TextBlock` `Text` | Hint under a control or section |
| Put a breakpoint on the wire out of the Item pin to stop before every item and look at what is about to be processed. That is the one thing this node does that wiring a list straight into another node does not. | line 23, `TextBlock` `Text` | Hint under a control or section |

### `src/LocalNEXUS.App/Views/Settings/ModelNodeSettingsView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Provider | line 11, `TextBlock` `Text` | Section header |
| Local | line 13, `RadioButton` `Content` | Button label |
| Network | line 16, `RadioButton` `Content` | Button label |
| Self hosted | line 19, `RadioButton` `Content` | Button label |
| OpenRouter | line 22, `RadioButton` `Content` | Button label |
| Cloud | line 25, `RadioButton` `Content` | Button label |
| A hosted provider you have an account with. | line 27, `RadioButton` `ToolTip` | Tooltip, on hover |
| Provider | line 33, `TextBlock` `Text` | Field label, above an input |
| This provider has no key yet. Add one in Settings under API keys. The graph is fine; it just cannot run until there is a key. | line 45, `TextBlock` `Text` | Warning text, shown when something needs attention |
| Model id | line 48, `TextBlock` `Text` | Field label, above an input |
| Free text, because a provider serves many models. The rate shown above is for the one that provider is best known for, so a different model may cost more or less. | line 53, `TextBlock` `Text` | Hint under a control or section |
| Local model | line 58, `TextBlock` `Text` | Field label, above an input |
| Add folder | line 103, `Button` `Content` | Button label |
| Rescan | line 107, `Button` `Content` | Button label |
| Open models folder | line 112, `Button` `Content` | Button label |
| Edit model folders | line 116, `Button` `Content` | Button label |
| Opens model-paths.txt, one folder per line, scanned for both formats | line 117, `Button` `ToolTip` | Tooltip, on hover |
| Browse for a file | line 177, `Button` `Content` | Button label |
| Run a GGUF from anywhere on disk for this node only, without adding its folder to the catalogue | line 178, `Button` `ToolTip` | Tooltip, on hover |
| Browse for a folder | line 182, `Button` `Content` | Button label |
| A safetensors model is a folder holding config.json beside its weight files, so it is picked as a folder | line 183, `Button` `ToolTip` | Tooltip, on hover |
| Use the catalogue | line 186, `Button` `Content` | Button label |
| Drop this node's own model and go back to the selection above | line 187, `Button` `ToolTip` | Tooltip, on hover |
| Context size | line 201, `TextBlock` `Text` | Field label, above an input |
| GPU layers | line 206, `TextBlock` `Text` | Field label, above an input |
| 999 puts every layer on the GPU. | line 211, `TextBlock` `Text` | Hint under a control or section |
| Refresh | line 229, `Button` `Content` | Button label |
| Reads what the running server has again | line 230, `Button` `ToolTip` | Tooltip, on hover |
| These take effect when the model loads. The next run stops the running server and starts it again with these values. | line 248, `TextBlock` `Text` | Hint under a control or section |
| Runs on this machine alone. To use several machines, switch to the network provider. | line 253, `TextBlock` `Text` | Hint under a control or section |
| Python runtime | line 260, `TextBlock` `Text` | Section header |
| Repair | line 315, `Button` `Content` | Button label |
| Builds whatever is missing, reusing everything already downloaded | line 316, `Button` `ToolTip` | Tooltip, on hover |
| Set up again | line 320, `Button` `Content` | Button label |
| Deletes the environment and builds it from the cached downloads | line 321, `Button` `ToolTip` | Tooltip, on hover |
| Open runtime folder | line 324, `Button` `Content` | Button label |
| Sets itself up in the background. GGUF models work while it does. | line 331, `TextBlock` `Text` | Hint under a control or section |
| Network model | line 336, `TextBlock` `Text` | Field label, above an input |
| Models the network can serve right now. A model that is starting or blocked cannot be picked; the Network tab shows what it is waiting on. | line 363, `TextBlock` `Text` | Hint under a control or section |
| Model id | line 368, `TextBlock` `Text` | Field label, above an input |
| The model name your server expects. Set its address under Endpoint. | line 372, `TextBlock` `Text` | Hint under a control or section |
| API key (optional) | line 374, `TextBlock` `Text` | Field label, above an input |
| Only if your server needs one. Stored in plain text in the graph file. | line 378, `TextBlock` `Text` | Hint under a control or section |
| OpenRouter model slug | line 383, `TextBlock` `Text` | Field label, above an input |
| For example anthropic/claude-sonnet-4 or meta-llama/llama-3.3-70b-instruct | line 387, `TextBlock` `Text` | Hint under a control or section |
| API key | line 389, `TextBlock` `Text` | Field label, above an input |
| Stored in plain text in the graph file, so take it out before sharing one. | line 393, `TextBlock` `Text` | Hint under a control or section |
| Prompt | line 396, `TextBlock` `Text` | Section header |
| System prompt | line 398, `TextBlock` `Text` | Field label, above an input |
| Set from the project kind when the node was added. Changing it here affects this node only. | line 411, `TextBlock` `Text` | Hint under a control or section |
| Take a markdown code fence off the reply | line 417, `CheckBox` `Content` | Checkbox label |
| Only when the whole reply is one fenced block, so an explanation with code in it is left alone. Turn it off for a model that is meant to produce prose, a plan, or documentation that keeps its code blocks. | line 423, `TextBlock` `Text` | Hint under a control or section |
| Edits | line 425, `TextBlock` `Text` | Section header |
| Automatic | line 428, `RadioButton` `Content` | Button label |
| Always the whole file | line 431, `RadioButton` `Content` | Button label |
| Always a diff | line 434, `RadioButton` `Content` | Button label |
| How this node asks for a change to a file. Automatic suits most models. | line 440, `TextBlock` `Text` | Hint under a control or section |
| Sampling | line 442, `TextBlock` `Text` | Section header |
| Temperature | line 452, `TextBlock` `Text` | Field label, above an input |
| Max tokens | line 457, `TextBlock` `Text` | Field label, above an input |
| Endpoint | line 462, `TextBlock` `Text` | Section header |
| Base URL | line 464, `TextBlock` `Text` | Field label, above an input |
| Leave blank to let LocalNEXUS run the model. Set it to use a server that is already running. | line 468, `TextBlock` `Text` | Hint under a control or section |
| Tools | line 475, `TextBlock` `Text` | Section header |
| Refresh | line 481, `Button` `Content` | Button label |
| Reads what this project has installed again | line 482, `Button` `ToolTip` | Tooltip, on hover |
| Check support | line 496, `Button` `Content` | Button label |
| Gives the model one small tool and sees whether it actually calls it. The model has to be loaded. | line 497, `Button` `ToolTip` | Tooltip, on hover |
| List tools | line 528, `Button` `Content` | Button label |
| Choose tools | line 550, `Expander` `Header` | Section header, on a collapsible section |
| Tool calls per run | line 569, `TextBlock` `Text` | Field label, above an input |
| How many times this node may call a tool before it has to answer without one | line 571, `TextBox` `ToolTip` | Tooltip, on hover |
| Extensions are per project, so this is what this project has installed. Every call is written to the feed with its arguments and result, and a tool that fails comes back to the model as an error rather than stopping the run. | line 576, `TextBlock` `Text` | Hint under a control or section |

### `src/LocalNEXUS.App/Views/Settings/OutputNodeSettingsView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Destination | line 11, `TextBlock` `Text` | Section header |
| Target subfolder (relative to the project) | line 13, `TextBlock` `Text` | Field label, above an input |
| File name | line 16, `TextBlock` `Text` | Field label, above an input |
| Resolved path | line 19, `TextBlock` `Text` | Field label, above an input |
| Ask in the feed before writing | line 28, `CheckBox` `Content` | Checkbox label |
| Created if it is missing, overwritten if it is not. Paths outside the project are refused. | line 32, `TextBlock` `Text` | Hint under a control or section |

### `src/LocalNEXUS.App/Views/Settings/PromptNodeSettingsView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| What it sends | line 11, `TextBlock` `Text` | Section header |
| No settings. It sends on whatever you type in the chat box. | line 13, `TextBlock` `Text` | Hint under a control or section |

### `src/LocalNEXUS.App/Views/Settings/ReshapeNodeSettingsView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| What it does | line 18, `TextBlock` `Text` | Section header |
| All of these are mechanical. Nothing here calls a model and nothing passing through leaves the machine. A model can still write the rule, through a prompt and a model wired into the Rule pin. | line 21, `TextBlock` `Text` | Hint under a control or section |
| Inject | line 24, `RadioButton` `Content` | Button label |
| Put standing text before or after whatever passes through. | line 26, `RadioButton` `ToolTip` | Tooltip, on hover |
| Extract | line 28, `RadioButton` `Content` | Button label |
| Keep the part that matches and drop the rest. | line 31, `RadioButton` `ToolTip` | Tooltip, on hover |
| Replace | line 33, `RadioButton` `Content` | Button label |
| Find and replace, by pattern. | line 36, `RadioButton` `ToolTip` | Tooltip, on hover |
| Trim | line 38, `RadioButton` `Content` | Button label |
| Cut to a length, so what leaves fits a context budget. | line 41, `RadioButton` `ToolTip` | Tooltip, on hover |
| Script | line 43, `RadioButton` `Content` | Button label |
| A C# expression, for anything the presets do not cover. | line 46, `RadioButton` `ToolTip` | Tooltip, on hover |
| Before | line 52, `TextBlock` `Text` | Field label, above an input |
| After | line 59, `TextBlock` `Text` | Field label, above an input |
| A house rule on the way into a coder, without editing its system prompt, and without editing five of them when the rule changes. Leave both empty and the text passes through untouched. | line 68, `TextBlock` `Text` | Hint under a control or section |
| Keep what matches | line 73, `TextBlock` `Text` | Field label, above an input |
| Model output is always more than was asked for. This keeps the first bracketed group when the pattern has one, and the whole match when it does not. A pattern that finds nothing passes the text through rather than handing an empty file to whatever is next. | line 82, `TextBlock` `Text` | Hint under a control or section |
| Find | line 87, `TextBlock` `Text` | Field label, above an input |
| Replace with | line 94, `TextBlock` `Text` | Field label, above an input |
| A pattern that matches nothing passes the text through. One that will not compile fails the node, because a node that exists to change what goes through it should not report success having changed nothing. | line 100, `TextBlock` `Text` | Hint under a control or section |
| Longest allowed, in characters | line 105, `TextBlock` `Text` | Field label, above an input |
| Cut from | line 109, `TextBlock` `Text` | Field label, above an input |
| The end | line 111, `RadioButton` `Content` | Button label |
| Keep the beginning. | line 113, `RadioButton` `ToolTip` | Tooltip, on hover |
| The start | line 115, `RadioButton` `Content` | Button label |
| Keep the end. | line 118, `RadioButton` `ToolTip` | Tooltip, on hover |
| Nothing is added to say it was cut. This feeds a context budget, and a marker would be one more thing counted against the budget it exists to respect. | line 124, `TextBlock` `Text` | Hint under a control or section |
| Expression | line 129, `TextBlock` `Text` | Field label, above an input |
| One C# expression, with the incoming text available as input. This is the only mode that can be unavailable: the script compiler needs the runtime assemblies as files and a single file build keeps them inside itself. | line 138, `TextBlock` `Text` | Hint under a control or section |

### `src/LocalNEXUS.App/Views/Settings/TextOutputNodeSettingsView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Whatever reached this node. Nothing is written to disk and nothing leaves the application, so none of the project write rules apply to it. | line 20, `TextBlock` `Text` | Hint under a control or section |
| Copy | line 26, `Button` `Content` | Button label |
| Puts the whole answer on the clipboard | line 27, `Button` `ToolTip` | Tooltip, on hover |
| Nothing has run yet. | line 33, `TextBlock` `Text` | Hint under a control or section |

### `src/LocalNEXUS.App/Views/Settings/TriageNodeSettingsView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| What it looks at | line 11, `TextBlock` `Text` | Section header |
| Candidate files | line 13, `TextBlock` `Text` | Field label, above an input |
| How many files to shortlist before reading any of them. Files you never mention but which the work depends on still make the list. | line 16, `TextBlock` `Text` | Hint under a control or section |
| Context budget | line 18, `TextBlock` `Text` | Section header |
| Project map | line 28, `TextBlock` `Text` | Field label, above an input |
| Candidate detail | line 33, `TextBlock` `Text` | Field label, above an input |
| Signatures from this run | line 38, `TextBlock` `Text` | Field label, above an input |
| In characters, roughly four to a token. Anything that does not fit is dropped, and the run says what. | line 47, `TextBlock` `Text` | Hint under a control or section |
| Last plan | line 49, `TextBlock` `Text` | Section header |
| What was decided about files that already exist | line 61, `TextBlock` `Text` | Static label |
| Files to write, in the order they will be written | line 74, `TextBlock` `Text` | Static label |
| Uses the Model node wired after it to do the thinking. Wire it as Prompt, Triage, Model, Compiler check, Output. | line 95, `TextBlock` `Text` | Hint under a control or section |

## Run history window


### `src/LocalNEXUS.App/Views/HistoryWindow.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Run history | line 9, `Window` `Title` | Window title |
| Search | line 43, `Button` `Content` | Button label |
| Find | line 49, `TextBlock` `Text` | Static label |
| Anything said during a run: an error, a file name, a request. | line 59, `TextBlock` `Text` | Static label |
| Pick a run to read what it did. | line 147, `TextBlock` `Text` | Static label |
| Undo the files | line 165, `Button` `Content` | Button label |
| Reuse the request | line 171, `Button` `Content` | Button label |

## Extensions window


### `src/LocalNEXUS.App/Views/ExtensionsWindow.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Extensions | line 9, `Window` `Title` | Window title |
| SOURCES | line 70, `TextBlock` `Text` | Static label |
| Presets | line 72, `RadioButton` `Content` | Button label |
| Installed | line 78, `RadioButton` `Content` | Button label |
| Add an extension | line 109, `MenuItem` `ToolTip` | Tooltip, on hover |
| From npm | line 111, `MenuItem` `Header` | Menu item |
| a package name | line 112, `MenuItem` `Text` | Static label |
| From git | line 114, `MenuItem` `Header` | Menu item |
| a repository url | line 115, `MenuItem` `Text` | Static label |
| From disk | line 117, `MenuItem` `Header` | Menu item |
| a folder with a manifest | line 118, `MenuItem` `Text` | Static label |
| By command | line 120, `MenuItem` `Header` | Menu item |
| anything else | line 121, `MenuItem` `Text` | Static label |
| Filter by name or description | line 130, `TextBox` `ToolTip` | Tooltip, on hover |
| Nothing matches that. | line 200, `TextBlock` `Text` | Hint under a control or section |
| Select an extension to see what it is and what it adds. | line 210, `TextBlock` `Text` | Hint under a control or section |
| Adds | line 235, `TextBlock` `Text` | Section header |
| Needs | line 238, `TextBlock` `Text` | Section header |
| Runs | line 250, `TextBlock` `Text` | Section header |
| More | line 253, `TextBlock` `Text` | Section header |
| Install | line 256, `Button` `Content` | Button label |
| State | line 282, `TextBlock` `Text` | Section header |
| Adds | line 289, `TextBlock` `Text` | Section header |
| Needs | line 336, `TextBlock` `Text` | Section header |
| Added from | line 348, `TextBlock` `Text` | Section header |
| Runs | line 351, `TextBlock` `Text` | Section header |
| Test connect | line 355, `Button` `Content` | Button label |
| Disable | line 360, `Button` `Content` | Button label |
| Enable | line 366, `Button` `Content` | Button label |
| View logs | line 372, `Button` `Content` | Button label |
| Remove | line 377, `Button` `Content` | Button label |

## Views, shared templates


### `src/LocalNEXUS.App/Views/ActivityFeedView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Output | line 117, `Expander` `Header` | Section header, on a collapsible section |
| Confirm | line 142, `Button` `Content` | Button label |
| Cancel | line 146, `Button` `Content` | Button label |
| Wire up a graph, type a request, then press Run. | line 169, `TextBlock` `Text` | Static label |
| Open the engine logs | line 177, `Button` `Content` | Button label |
| The engines write their own logs to disk, separately from this transcript. | line 178, `Button` `ToolTip` | Tooltip, on hover |

### `src/LocalNEXUS.App/Views/AddExtensionWindow.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Height | line 10, `Window` `Content` | Control label |
| Name | line 29, `TextBlock` `Text` | Section header |
| Arguments | line 32, `TextBlock` `Text` | Section header |
| Separated by spaces. | line 34, `TextBlock` `Text` | Hint under a control or section |
| Working directory | line 36, `TextBlock` `Text` | Section header |
| Leave blank to use the extension folder. | line 38, `TextBlock` `Text` | Hint under a control or section |
| Environment | line 40, `TextBlock` `Text` | Section header |
| One per line, as NAME=value. | line 45, `TextBlock` `Text` | Hint under a control or section |
| What it speaks | line 47, `TextBlock` `Text` | Section header |
| Tools a model can call | line 49, `CheckBox` `Content` | Checkbox label |
| Node types for the graph | line 50, `CheckBox` `Content` | Checkbox label |
| A spec tab | line 51, `CheckBox` `Content` | Checkbox label |
| Pick at least one, or there is no way to talk to it. | line 56, `TextBlock` `Text` | Hint under a control or section |
| Cancel | line 60, `Button` `Content` | Button label |

### `src/LocalNEXUS.App/Views/InspectorTemplates.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Model id | line 32, `TextBlock` `Text` | Field label, above an input |
| Size | line 56, `TextBlock` `Text` | Field label, above an input |
| not reported | line 57, `TextBlock` `Text` | Value shown in monospace |
| Context | line 61, `TextBlock` `Text` | Field label, above an input |
| Parameters | line 66, `TextBlock` `Text` | Field label, above an input |
| Throughput | line 71, `TextBlock` `Text` | Field label, above an input |
| not reported | line 72, `TextBlock` `Text` | Value shown in monospace |
| Backups | line 76, `TextBlock` `Text` | Field label, above an input |
| Last verified | line 81, `TextBlock` `Text` | Field label, above an input |
| Coverage | line 86, `TextBlock` `Text` | Section header |
| LAYERS | line 96, `TextBlock` `Text` | Value shown in monospace |
| COVERED BY | line 97, `TextBlock` `Text` | Value shown in monospace |
| BACKUPS | line 98, `TextBlock` `Text` | Value shown in monospace |
| Backups are other machines that can take over if the one holding a section drops out. The mesh does not report throughput, and size is shown only for meshes listed in the directory. Everything else is shown exactly as the mesh reports it. | line 164, `TextBlock` `Text` | Hint under a control or section |
| Read from the mesh. | line 172, `TextBlock` `Text` | Hint under a control or section |
| Reported by the mesh. Nothing about another machine can be changed from here. | line 182, `TextBlock` `Text` | Hint under a control or section |
| Peer key | line 184, `TextBlock` `Text` | Field label, above an input |
| This machine's permanent ID on the network. | line 191, `TextBlock` `Text` | Hint under a control or section |
| What it brings | line 193, `TextBlock` `Text` | Field label, above an input |
| Path to it | line 196, `TextBlock` `Text` | Field label, above an input |
| Last seen | line 199, `TextBlock` `Text` | Field label, above an input |
| Engine version | line 202, `TextBlock` `Text` | Field label, above an input |
| Trust | line 206, `TextBlock` `Text` | Field label, above an input |
| Everything in a private mesh was invited in, so all of it is trusted. | line 209, `TextBlock` `Text` | Hint under a control or section |
| What this machine offers | line 215, `TextBlock` `Text` | Section header |
| Offer this machine's compute | line 219, `CheckBox` `Content` | Checkbox label |
| Memory cap, GB | line 222, `TextBlock` `Text` | Field label, above an input |
| Apply and restart the node | line 228, `Button` `Content` | Button label |
| Raise this machine's cap | line 248, `TextBlock` `Text` | Section header |
| Raise the cap to let this machine take these layers. | line 250, `TextBlock` `Text` | Hint under a control or section |
| Memory cap, GB | line 252, `TextBlock` `Text` | Field label, above an input |
| Apply and restart the node | line 258, `Button` `Content` | Button label |
| Invite another machine | line 265, `TextBlock` `Text` | Section header |
| Any machine with room can fill the gap. | line 267, `TextBlock` `Text` | Hint under a control or section |
| Copy invite token | line 272, `Button` `Content` | Button label |
| The mesh does not report how much memory these layers need. It does report that no machine holds them. | line 278, `TextBlock` `Text` | Hint under a control or section |
| Covered by | line 283, `TextBlock` `Text` | Field label, above an input |
| Backups | line 286, `TextBlock` `Text` | Field label, above an input |
| Other machines with room to take these layers over. If the machine holding them goes away, the mesh moves the work to one of these without the model going down. | line 292, `TextBlock` `Text` | Hint under a control or section |
| This mesh is hosted by someone else. The details come from whoever runs it and cannot be verified from this machine. To use it, join it first. | line 304, `TextBlock` `Text` | Hint under a control or section |
| Size | line 306, `TextBlock` `Text` | Field label, above an input |
| Serving now | line 309, `TextBlock` `Text` | Field label, above an input |
| Nothing loaded. | line 318, `TextBlock` `Text` | Hint under a control or section |
| Looking for | line 322, `TextBlock` `Text` | Field label, above an input |
| Models it wants and does not have. If your machine holds one of these, joining fills a real gap rather than adding another spare. | line 335, `TextBlock` `Text` | Hint under a control or section |
| This mesh is not serving anything yet. It is waiting for a machine that has one of the models above. | line 346, `TextBlock` `Text` | Hint under a control or section |
| Join this mesh | line 351, `TextBlock` `Text` | Section header |
| Gets an invite from the directory and restarts your node into this mesh. Your own mesh stops while you are in theirs, and Leave the mesh puts it back. | line 354, `TextBlock` `Text` | Hint under a control or section |
| This mesh has not named itself, so the directory can only be asked for the best mesh serving the same model. That may not be this exact one. | line 360, `TextBlock` `Text` | Warning text, shown when something needs attention |
| Join | line 366, `Button` `Content` | Button label |
| Where it has got to | line 383, `TextBlock` `Text` | Field label, above an input |
| Mesh id | line 392, `TextBlock` `Text` | Field label, above an input |
| Joined | line 397, `TextBlock` `Text` | Field label, above an input |
| Its invite | line 405, `TextBlock` `Text` | Field label, above an input |
| Pass this on and somebody else can join the same mesh. | line 415, `TextBlock` `Text` | Hint under a control or section |
| Leave this mesh | line 420, `Button` `Content` | Button label |
| Mesh id | line 436, `TextBlock` `Text` | Field label, above an input |
| Who can find it | line 441, `TextBlock` `Text` | Field label, above an input |
| Publishing is in the panel on the left, and it is read when the node starts, so ticking it takes effect when the node next comes up. Off, only machines on this network can find the mesh; on, it is listed publicly, though joining still needs the invite. | line 446, `TextBlock` `Text` | Hint under a control or section |
| Members | line 448, `TextBlock` `Text` | Field label, above an input |
| You are giving | line 451, `TextBlock` `Text` | Field label, above an input |
| The invite | line 458, `TextBlock` `Text` | Field label, above an input |
| Start the node and it creates one. | line 470, `TextBlock` `Text` | Hint under a control or section |
| Copy it | line 477, `Button` `Content` | Button label |
| New invite | line 482, `Button` `Content` | Button label |
| Rotates this node's key, which makes every invite handed out so far stop working. | line 483, `Button` `ToolTip` | Tooltip, on hover |

### `src/LocalNEXUS.App/Views/MainWindow.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| _File | line 124, `MenuItem` `Header` | Menu item |
| _New graph | line 125, `MenuItem` `Header` | Menu item |
| Ctrl+N | line 125, `MenuItem` `Text` | Static label |
| _Open graph... | line 126, `MenuItem` `Header` | Menu item |
| Ctrl+O | line 126, `MenuItem` `Text` | Static label |
| _Save graph | line 127, `MenuItem` `Header` | Menu item |
| Ctrl+S | line 127, `MenuItem` `Text` | Static label |
| Save graph _as... | line 128, `MenuItem` `Header` | Menu item |
| Ctrl+Shift+S | line 128, `MenuItem` `Text` | Static label |
| Start _from | line 136, `MenuItem` `Header` | Menu item |
| Save as _template... | line 151, `MenuItem` `Header` | Menu item |
| Open _project... | line 153, `MenuItem` `Header` | Menu item |
| Se_ttings | line 155, `MenuItem` `Header` | Menu item |
| Ctrl+, | line 155, `MenuItem` `Text` | Static label |
| E_xit | line 157, `MenuItem` `Header` | Menu item |
| _Edit | line 160, `MenuItem` `Header` | Menu item |
| _Add node | line 161, `MenuItem` `Header` | Menu item |
| _Delete selected | line 174, `MenuItem` `Header` | Menu item |
| _View | line 177, `MenuItem` `Header` | Menu item |
| _Workspace | line 178, `MenuItem` `Header` | Menu item |
| _Network | line 179, `MenuItem` `Header` | Menu item |
| Toggle _side bar | line 181, `MenuItem` `Header` | Menu item |
| Ctrl+B | line 181, `MenuItem` `Text` | Static label |
| Toggle _inspector | line 182, `MenuItem` `Header` | Menu item |
| Toggle _panel | line 183, `MenuItem` `Header` | Menu item |
| Ctrl+J | line 183, `MenuItem` `Text` | Static label |
| _Problems | line 185, `MenuItem` `Header` | Menu item |
| _Activity | line 186, `MenuItem` `Header` | Menu item |
| _Run | line 189, `MenuItem` `Header` | Menu item |
| _Run graph | line 190, `MenuItem` `Header` | Menu item |
| Ctrl+Enter | line 190, `MenuItem` `Text` | Static label |
| _Pause or resume | line 191, `MenuItem` `Header` | Menu item |
| _Stop | line 192, `MenuItem` `Header` | Menu item |
| _Clear the transcript | line 194, `MenuItem` `Header` | Menu item |
| _Help | line 197, `MenuItem` `Header` | Menu item |
| _Getting started | line 198, `MenuItem` `Header` | Menu item |
| Run faulted | line 221, `TextBlock` `Text` | Error text, shown when something has gone wrong |
| Stop | line 233, `Button` `Content` | Button label |
| Minimise | line 251, `Button` `ToolTip` | Tooltip, on hover |
| Maximise | line 257, `Button` `ToolTip` | Tooltip, on hover |
| Restore | line 264, `Button` `ToolTip` | Tooltip, on hover |
| Close | line 271, `Button` `ToolTip` | Tooltip, on hover |
| Close settings | line 427, `Button` `ToolTip` | Tooltip, on hover |
| Settings | line 431, `TextBlock` `Text` | Static label |

### `src/LocalNEXUS.App/Views/NodeTemplates.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Breakpoint | line 196, `MenuItem` `Header` | Menu item |
| Stop the run here and show what is passing along this wire. | line 202, `MenuItem` `ToolTip` | Tooltip, on hover |

### `src/LocalNEXUS.App/Views/ProjectSetupView.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Asked once. Everything here can be changed later in Settings under Project. | line 41, `TextBlock` `Text` | Hint under a control or section |
| Skip | line 48, `Button` `Content` | Button label |
| Take the defaults. Nothing is blocked, and this is not asked again. | line 49, `Button` `ToolTip` | Tooltip, on hover |
| Save | line 53, `Button` `Content` | Button label |
| Where generated code goes | line 60, `TextBlock` `Text` | Field label, above an input |
| Relative to the project root. The list is folders this project already has; type anything else for one that does not exist yet. | line 68, `TextBlock` `Text` | Hint under a control or section |
| Project kind | line 70, `TextBlock` `Text` | Field label, above an input |
| Default model for this project | line 79, `TextBlock` `Text` | Field label, above an input |
| Stays on this machine and is never committed, because it is a path that exists here and may not anywhere else. | line 87, `TextBlock` `Text` | Hint under a control or section |
| Answer MCP tool calls for this project | line 89, `TextBlock` `Text` | Field label, above an input |
| Allow other tools to open and run this project | line 92, `CheckBox` `Content` | Checkbox label |
| Off unless the installation itself is also answering, which is a separate switch in Settings and is off by default. | line 96, `TextBlock` `Text` | Hint under a control or section |
| Share these settings | line 98, `TextBlock` `Text` | Field label, above an input |
| Commit the folder and the project kind with the repository | line 101, `CheckBox` `Content` | Checkbox label |

### `src/LocalNEXUS.App/Views/Theme.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| nothing to choose | line 430, `TextBlock` `Text` | Static label |

### `src/LocalNEXUS.Installer/Views/SetupWindow.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| LocalNEXUS Setup | line 10, `Window` `Title` | Window title |
| LocalNEXUS Setup | line 76, `TextBlock` `Text` | Static label |
| Minimise | line 83, `Button` `ToolTip` | Tooltip, on hover |
| Close | line 84, `Button` `ToolTip` | Tooltip, on hover |
| LocalNEXUS 1.6.0 | line 164, `TextBlock` `Text` | Static label |
| Cancel | line 233, `Button` `Content` | Button label |
| Back | line 238, `Button` `Content` | Button label |
| Retry | line 243, `Button` `Content` | Button label |
| This may take a few minutes. | line 257, `TextBlock` `Text` | Static label |

### `src/LocalNEXUS.Installer/Views/StepTemplates.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Welcome to the LocalNEXUS 1.6.0 Setup Wizard | line 28, `TextBlock` `Text` | Static label |
| Close other applications before continuing. | line 37, `TextBlock` `Text` | Static label |
| Runs on your hardware | line 41, `TextBlock` `Text` | Static label |
| Nothing sent to a cloud | line 44, `TextBlock` `Text` | Static label |
| License Agreement | line 62, `TextBlock` `Text` | Static label |
| Read the license agreement before continuing. | line 63, `TextBlock` `Text` | Static label |
| I accept the agreement | line 103, `TextBlock` `Text` | Static label |
| I do not accept | line 115, `TextBlock` `Text` | Static label |
| Select Components | line 134, `TextBlock` `Text` | Static label |
| Choose which parts of LocalNEXUS to install. | line 135, `TextBlock` `Text` | Static label |
| Everything | line 139, `Button` `Content` | Button label |
| Local models only | line 140, `Button` `Content` | Button label |
| required | line 192, `TextBlock` `Text` | Static label |
| Create a desktop shortcut | line 233, `TextBlock` `Text` | Static label |
| Choose a llama.cpp Build | line 248, `TextBlock` `Text` | Static label |
| Ready to Install | line 315, `TextBlock` `Text` | Static label |
| Setup is now ready to install LocalNEXUS on this computer. | line 316, `TextBlock` `Text` | Static label |
| DESTINATION | line 321, `TextBlock` `Text` | Static label |
| ENGINES TO FETCH | line 327, `TextBlock` `Text` | Static label |
| Nothing optional is ticked. LocalNEXUS will install and start, but it will not be able to run a model, because no engine was selected on the previous step. Add one later by running this installer again. | line 334, `TextBlock` `Text` | Empty state, shown when there is nothing to list |
| Installing | line 361, `TextBlock` `Text` | Static label |
| Please wait while the engines are downloaded and set up. | line 362, `TextBlock` `Text` | Static label |
| Completing the LocalNEXUS Setup Wizard | line 428, `TextBlock` `Text` | Static label |
| Setup has finished installing LocalNEXUS on this computer. | line 429, `TextBlock` `Text` | Static label |
| Launch LocalNEXUS | line 453, `TextBlock` `Text` | Static label |

### `src/LocalNEXUS.Installer/Views/UninstallWindow.xaml`

| Text | Where | When it shows |
| --- | --- | --- |
| Remove LocalNEXUS | line 5, `Window` `Title` | Window title |
| Remove LocalNEXUS | line 32, `TextBlock` `Text` | Static label |
| This removes the application and the engine binaries it installed. | line 33, `TextBlock` `Text` | Static label |
| Also remove my settings, saved graphs and models catalogue | line 53, `TextBlock` `Text` | Static label |
| Leave this unticked and your work stays where it is, so reinstalling picks up where you left off. Ticking it also removes the Python runtime, which is a multi gigabyte download to rebuild. Model files themselves are never touched wherever they live. | line 60, `TextBlock` `Text` | Static label |
| Cancel | line 76, `Button` `Content` | Button label |
| Remove | line 77, `Button` `Content` | Button label |

## Nodes, at run time


### `src/LocalNEXUS.App/Nodes/AgentNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;Title&#125; was not run, because of what it might have cost. | line 302, `WarnIfExpensiveAsync` | Run fault or refusal, shown when something is stopped or declined |
| No model is wired in. The Agent borrows the model on its Model pin, and that model has to be able to call tools. | line 336, `IsToolWarningSevere` | Bound to the interface and read whenever the panel is drawn |
| &#123;Title&#125; was not given anything to do. Wire something into its text input, or type a request. | line 389, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; has no model. Wire a Model node into its Model pin; the agent borrows that node's model, its selected extensions and its search key rather than carrying its own. | line 395, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; cannot run: &#123;reason&#125; | line 401, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; started with &#123;tools.Count&#125; tool(s) | line 416, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| turn &#123;turn&#125; of &#123;MaxTurns&#125; | line 426, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| finished in &#123;turn&#125; turn(s), &#123;calls&#125; tool call(s) | line 434, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125; finished without using a tool It answered in one turn and called nothing, so nothing was read, written or run. If the answer claims work was done, it was not done here. That is | line 442, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| usually a model that cannot emit tool calls: check tool support on the Model node. It is also the right answer when there was genuinely nothing to do.&#123;Environment.NewLine&#125;&#123;Environment.NewLine&#125;&#123;summary&#125; | line 445, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; finished | line 451, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;turn&#125; turn(s), &#123;calls&#125; tool call(s). | line 451, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| stopped at the limit of &#123;MaxTurns&#125; turns | line 474, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125; reached its limit It took &#123;MaxTurns&#125; turns and &#123;calls&#125; tool call(s) without finishing. Raise the limit on the node, or narrow the request. Anything it wrote is written and anything refused is waiting. | line 477, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: &#123;call.Name&#125; | line 517, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;toolbox.Written.Count&#125; file(s) written | line 562, `Emit` | Status line, updated as the thing it describes changes |

### `src/LocalNEXUS.App/Nodes/CompilerCheckNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Repaired, then compiled Did not compile Could not tell, references incomplete | line 133, `OutcomeText` | Bound to the interface and read whenever the panel is drawn |
| Could not be checked Not run yet | line 136, `OutcomeText` | Bound to the interface and read whenever the panel is drawn |
| &#123;Title&#125;: &#123;items.Count&#125; item(s) to check | line 183, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;index + 1&#125; of &#123;items.Count&#125; | line 193, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125; received nothing to check. Connect a node to its Code pin. | line 209, `CheckOnceAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;fileName&#125; compiled in &#123;result.Elapsed.TotalMilliseconds:0&#125; ms | line 236, `CheckOnceAsync` | Status line, updated as the thing it describes changes |
| &#123;fileName&#125; compiled after &#123;repaired.Attempts&#125; repair attempt(s) | line 250, `CheckOnceAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125; received an empty plan to check. | line 273, `CheckPlanAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125;: &#123;checkedFile.File.RelativePath&#125; is not being compiled into the rest It does not compile, so anything after it is checked without it. A later file that genuinely needed it will say so, rather than inheriting errors from this one. | line 315, `CheckPlanAsync` | Activity feed entry, written while a run is going on |
| &#123;compiled&#125; of &#123;settled.Count&#125; file(s) compiled, &#123;failed&#125; left for later &#123;settled.Count&#125; file(s) compiled after &#123;repairs&#125; repair attempt(s) | line 335, `CheckPlanAsync` | Status line, updated as the thing it describes changes |
| &#123;settled.Count&#125; file(s) compiled | line 338, `CheckPlanAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;: &#123;label&#125; was not checked | line 367, `CheckOneAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: &#123;label&#125; cannot be repaired | line 411, `CheckOneAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: &#123;label&#125;, repair attempt &#123;attempt&#125; of &#123;RetryLimit&#125; Asking &#123;upstream!.Title&#125; to fix &#123;request.ErrorCount&#125; error(s) | line 433, `CheckOneAsync` | Activity feed entry, written while a run is going on |
| &#123;label&#125;: repair attempt &#123;attempt&#125; of &#123;RetryLimit&#125; | line 437, `CheckOneAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;: repair attempt &#123;attempt&#125; produced nothing | line 444, `CheckOneAsync` | Activity feed entry, written while a run is going on |
| The previous content stands. | line 444, `CheckOneAsync` | Activity feed entry, written while a run is going on |
| &#123;result.TrustedErrors.Count&#125; error(s) remain in &#123;file.RelativePath&#125; | line 483, `CheckOneAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;: &#123;file.RelativePath&#125; does not compile and &#123;(attempts == 0 ? Nothing has been written.&#123;Environment.NewLine&#125;&#123;listing&#125; | line 488, `CheckOneAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125;: &#123;file.RelativePath&#125; is left for later &#123;result.TrustedErrors.Count&#125; error(s) remain after &#123;(attempts == 0 ? | line 496, `CheckOneAsync` | Activity feed entry, written while a run is going on |
| The rest of the plan carries on and this file is staged rather than written. | line 499, `CheckOneAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: nothing upstream can repair this The code arrived from &#123;what&#125;, which cannot be asked for another attempt. Wire a model node into this node to enable repair. | line 597, `TryRepairAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: &#123;upstream.Title&#125; cannot repair this | line 607, `TryRepairAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: repair attempt &#123;attempt&#125; of &#123;RetryLimit&#125; Asking &#123;upstream.Title&#125; to fix &#123;request.ErrorCount&#125; error(s) in &#123;fileName&#125; | line 624, `TryRepairAsync` | Activity feed entry, written while a run is going on |
| Repair attempt &#123;attempt&#125; of &#123;RetryLimit&#125; | line 628, `TryRepairAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;: repair attempt &#123;attempt&#125; produced nothing &#123;upstream.Title&#125; returned an empty reply, so the previous code stands. | line 635, `TryRepairAsync` | Activity feed entry, written while a run is going on |
| &#123;label&#125; compiles &#123;result.Summary&#125;. &#123;result.ReferenceSummary&#125; | line 677, `ReportAttempt` | Activity feed entry, written while a run is going on |
| &#123;label&#125; does not compile | line 692, `ReportAttempt` | Activity feed entry, written while a run is going on |
| &#123;label&#125;: could not tell, references incomplete | line 714, `ReportInconclusive` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;: &#123;label&#125; could not be judged &#123;result.Errors.Count&#125; error(s), and every one of them names something this check had no reference for. &#123;result.ReferenceSummary&#125; Nothing was repaired, because there is no reason to believe the code is wrong. | line 717, `ReportInconclusive` | Activity feed entry, written while a run is going on |
| &#123;Environment.NewLine&#125;&#123;result.FormatDiagnostics(DiagnosticsShown)&#125; | line 720, `ReportInconclusive` | Activity feed entry, written while a run is going on |
| Could not be checked | line 731, `Unavailable` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;: nothing was checked | line 733, `Unavailable` | Activity feed entry, written while a run is going on |
| &#123;errors&#125; error(s) remain in &#123;fileName&#125; | line 757, `Fail` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;: &#123;fileName&#125; does not compile and &#123;attempted&#125;. &#123;errors&#125; error(s) remain:&#123;Environment.NewLine&#125;&#123;listing&#125; | line 762, `Fail` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125;: continuing with code that does not compile | line 767, `Fail` | Run fault or refusal, shown when something is stopped or declined |
| &#123;errors&#125; error(s) remain in &#123;fileName&#125; and &#123;attempted&#125;. This node is set to continue anyway. | line 768, `Fail` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Nodes/DebateNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;Title&#125; has nothing to argue about. Wire a request, a plan or a spec into its Text pin. | line 154, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| Model A Model B | line 157, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; could not read \"&#123;TimeBudget&#125;\" as a time. Enter it as minutes and seconds, for example 05:00. | line 165, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125;: opening &#123;((NodeBase)first).Title&#125; as &#123;FirstRole&#125; from &#123;Describe(FirstSource)&#125;, &#123;((NodeBase)second).Title&#125; as &#123;SecondRole&#125; from &#123;Describe(SecondSource)&#125;. | line 180, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| Settles at &#123;ConvergenceThreshold&#125; percent, at most &#123;MaximumRounds&#125; rounds, at most &#123;Format(budget)&#125;. | line 183, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| Round &#123;round&#125; of at most &#123;MaximumRounds&#125; | line 220, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;: &#123;LastOutcome&#125; | line 288, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: a judge is deciding The two positions are at &#123;scored.Explanation&#125; and &#123;why&#125;. &#123;((NodeBase)arbiter).Title&#125; will &#123;Describe(FallbackJudgeMode)&#125;. | line 322, `ResolveAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: nobody answered, so a judge decided &#123;((NodeBase)arbiter).Title&#125; will &#123;Describe(FallbackJudgeMode)&#125;. | line 346, `ResolveAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: settled by hand | line 354, `ResolveAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; has both models set to defend, and two defenders never disagree. Set one to criticize, or set both to debate. &#123;Title&#125; has both models set to criticize, and two critics never propose anything. Set | line 387, `EnforcePairing` | Run fault or refusal, shown when something is stopped or declined |
| one to defend, or set both to debate. | line 390, `EnforcePairing` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125;: no project to argue from A model is set to argue from the codebase and none is open, so it argues from what it knows. | line 412, `BuildProjectContextAsync` | Activity feed entry, written while a run is going on |
| Debate needs a model on &#123;which&#125;. Wire a Model node's Model output into it. | line 472, `Require` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125;: round &#123;round&#125;, &#123;model.Title&#125; &#123;position&#125;&#123;said&#125; | line 490, `Record` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: after round &#123;round&#125;, &#123;(scored.IsMeasured ? $ | line 510, `ReportConvergence` | Activity feed entry, written while a run is going on |
| not measurable The measured number is what decides, and the threshold is &#123;ConvergenceThreshold&#125; percent. | line 510, `ReportConvergence` | Activity feed entry, written while a run is going on |
| There was too little in common to judge: &#123;scored.Reason&#125;. Nothing settles on an unmeasured round, and it is not being read as disagreement. &#123;Environment.NewLine&#125;&#123;Environment.NewLine&#125;&#123;scored.Breakdown()&#125; | line 513, `ReportConvergence` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Nodes/ExtensionNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;Title&#125; needs the extension '&#123;ExtensionId&#125;', which is not registered against this project. | line 95, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; needs the extension '&#123;extension.Manifest.Name&#125;', which is switched off. | line 100, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| starting the extension | line 103, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| &#123;extension.Manifest.Name&#125; ran '&#123;TypeKey&#125;' but returned nothing for the output pin '&#123;pin.Name&#125;'. | line 140, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; ran in &#123;extension.Manifest.Name&#125; &#123;inputs.Count&#125; input(s), &#123;outputs.Count&#125; output(s) | line 148, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;outputs.Count&#125; output(s) | line 152, `ExecuteAsync` | Status line, updated as the thing it describes changes |

### `src/LocalNEXUS.App/Nodes/JudgeNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;Title&#125; has nothing to judge. Wire a debate, or a model, into its Text pin. | line 84, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; has no model to judge with. Wire a Model node's Model output into its Model input. | line 90, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; cannot judge: &#123;whyNot&#125; | line 95, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125;: &#123;judgeNode.Title&#125; is &#123;Describe(Mode)&#125; One position, so this is a read on whether what arrived stands up. | line 106, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| Two positions, so this is a determination between them. | line 109, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Describe(Mode)&#125;, &#123;verdict.Length&#125; characters | line 127, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;: &#123;Describe(Mode)&#125; | line 129, `ExecuteAsync` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Nodes/LoopNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| nothing has run yet &#123;Total&#125; item(s) to work through | line 90, `ProgressText` | Bound to the interface and read whenever the panel is drawn |
| item &#123;Position&#125; of &#123;Total&#125;, &#123;Remaining&#125; to go | line 93, `ProgressText` | Bound to the interface and read whenever the panel is drawn |
| &#123;items.Count&#125; item(s), but nothing is wired to the Item pin | line 119, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125; has nothing to run Wire the Item pin into whatever should happen for each item. | line 122, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: &#123;items.Count&#125; item(s) over &#123;chain.Count&#125; node(s) | line 130, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: item &#123;Position&#125; of &#123;Total&#125; | line 149, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| nothing to work through &#123;items.Count&#125; item(s) done | line 157, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| Read as configuration by another node. Nothing ran here. | line 188, `RunChainAsync` | Status line, updated as the thing it describes changes |
| &#123;node.Title&#125; failed on item &#123;Position&#125; of &#123;Total&#125;: &#123;ex.Message&#125; | line 216, `RunChainAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Nodes/ModelNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Not loaded | line 340, `LoadStateText` | Bound to the interface and read whenever the panel is drawn |
| No tools. Nothing is offered to the model and nothing is spent on schemas. | line 649, `ToolTokenEstimate` | Bound to the interface and read whenever the panel is drawn |
| This model calls tools. | line 752, `ToolSupportText` | Bound to the interface and read whenever the panel is drawn |
| This model does not call tools, so anything selected here is context spent for nothing. | line 756, `ToolSupportText` | Bound to the interface and read whenever the panel is drawn |
| This node runs the model below, not the catalogue selection above. This node points at a model that is no longer there. | line 907, `ModelSourceText` | Bound to the interface and read whenever the panel is drawn |
| No model selected. Choose one above, or browse for one anywhere on disk. This node runs the catalogue selection above. | line 910, `ModelSourceText` | Bound to the interface and read whenever the panel is drawn |
| &#123;Title&#125;: &#123;items.Count&#125; item(s) to work through | line 953, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;index + 1&#125; of &#123;items.Count&#125; | line 963, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125; received no input. Connect something to its Text pin. | line 978, `AnswerOnceAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125;  (&#123;ModelDisplayName&#125;) | line 981, `AnswerOnceAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: writing &#123;tasks.Count&#125; file(s) | line 1022, `WriteFilesAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;  (&#123;task.Order&#125; of &#123;tasks.Count&#125;: &#123;task.RelativePath&#125;, &#123;(wholeFile ? | line 1056, `WriteFilesAsync` | Activity feed entry, written while a run is going on |
| &#123;task.Order&#125; of &#123;tasks.Count&#125;: &#123;task.FileName&#125; | line 1060, `WriteFilesAsync` | Status line, updated as the thing it describes changes |
| &#123;produced.Count&#125; file(s) written | line 1111, `WriteFilesAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;  (&#123;task.RelativePath&#125; would not apply, attempt &#123;attempt&#125; of &#123;EditRetryLimit&#125;) | line 1164, `ApplyWithRetriesAsync` | Activity feed entry, written while a run is going on |
| retrying &#123;task.FileName&#125; (&#123;attempt&#125; of &#123;EditRetryLimit&#125;) | line 1168, `ApplyWithRetriesAsync` | Status line, updated as the thing it describes changes |
| &#123;task.RelativePath&#125; changed by name, not by text | line 1214, `ApplyOnceAsync` | Activity feed entry, written while a run is going on |
| &#123;task.RelativePath&#125; was not expressed as named changes | line 1227, `ApplyOnceAsync` | Activity feed entry, written while a run is going on |
| &#123;task.RelativePath&#125; was not changed | line 1280, `StageUnreadableFile` | Activity feed entry, written while a run is going on |
| &#123;task.RelativePath&#125; was not written The coder kept asking to replace lines that are not in the file, so it was kept rather than written and the run carried on.&#123;Environment.NewLine&#125;&#123;failure&#125; | line 1308, `StageUnappliedEdit` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;  (planning, &#123;ModelDisplayName&#125;) | line 1396, `AnswerAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: listing its tools | line 1450, `ConfiguredToolsAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; could not be asked for its tools | line 1464, `ConfiguredToolsAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;  (&#123;ModelDisplayName&#125;) | line 1480, `ContinueAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; could not reach &#123;name&#125; | line 1562, `GatherToolsAsync` | Activity feed entry, written while a run is going on |
| &#123;ctx.Node.Title&#125; has &#123;tools.Count&#125; tool(s) it cannot use | line 1599, `WithSupportCheckAsync` | Activity feed entry, written while a run is going on |
| &#123;ctx.Node.Title&#125; has &#123;tools.Count&#125; tool(s) | line 1603, `WithSupportCheckAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; wrote a tool call out as text It asked for &#123;named.Name&#125; in the body of its reply instead of calling it, so nothing ran and the reply you get is the request rather than the result. The model chose the right | line 1642, `WarnIfItWroteTheCallOut` | Activity feed entry, written while a run is going on |
| tool and cannot emit it through the protocol. Check tool support on this node, and use a model tuned for tool use or a hosted one. Nothing here parses a call out of text and runs it, because a misread one would run the wrong thing.&#123;Environment.NewLine&#125;&#123;Environment.NewLine&#125;&#123;reply&#125; | line 1645, `WarnIfItWroteTheCallOut` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; searched for &#123;query&#125; Nothing came back. | line 1695, `WarnIfItWroteTheCallOut` | Activity feed entry, written while a run is going on |
| &#123;r.Title&#125;  &#123;r.Url&#125; | line 1698, `WarnIfItWroteTheCallOut` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; could not search for &#123;query&#125; | line 1704, `WarnIfItWroteTheCallOut` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; received an empty reply from &#123;ModelDisplayName&#125;. | line 1878, `StreamOnceAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; stopped calling tools It reached the limit of &#123;MaxToolCalls&#125; calls in one run. Raise the limit on the node if the work genuinely needs more, or look at whether it is repeating itself. | line 1936, `StreamTextAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; called &#123;call.Name&#125; in &#123;extensionName&#125; | line 1976, `StreamTextAsync` | Activity feed entry, written while a run is going on |
| tool &#123;callsMade&#125; of &#123;MaxToolCalls&#125;: &#123;call.Name&#125; | line 1980, `StreamTextAsync` | Status line, updated as the thing it describes changes |
| Run cost | line 2010, `StreamTextAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;  (repair &#123;request.Attempt&#125; of &#123;request.AttemptLimit&#125;, &#123;ModelDisplayName&#125;) | line 2072, `ReviseAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; was not run, because of what it might have cost. | line 2171, `WarnIfExpensiveAsync` | Run fault or refusal, shown when something is stopped or declined |
| No provider chosen. &#123;provider.DisplayName&#125; needs a key. Add one in Settings under API keys. | line 2201, `ProviderStatus` | Bound to the interface and read whenever the panel is drawn |
| &#123;provider.DisplayName&#125;, &#123;provider.RateSummary&#125;. | line 2204, `ProviderStatus` | Bound to the interface and read whenever the panel is drawn |
| &#123;Title&#125; has no provider chosen. Pick one in the node's settings. | line 2233, `ResolveCloud` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; has no model id set for &#123;provider.DisplayName&#125;. | line 2242, `ResolveCloud` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; uses &#123;provider.DisplayName&#125;, which has no key yet. Add one in Settings under API keys. Keys are stored encrypted and never saved into a graph. | line 2250, `ResolveCloud` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; has no base URL set for its self hosted server. | line 2280, `ResolveEndpointAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; has no model id set for its self hosted server. | line 2285, `ResolveEndpointAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; points at a model that is no longer there: &#123;ModelFilePath&#125;. Browse for it again, or clear it to go back to the catalogue selection. | line 2294, `ResolveEndpointAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; has no local model selected. Drop a model into the models folder, add a folder, or browse for one from the settings panel. | line 2302, `ResolveEndpointAsync` | Run fault or refusal, shown when something is stopped or declined |
| Restarting &#123;ModelDisplayName&#125; It is running with a context of &#123;current.ContextSize&#125; and &#123;current.GpuLayers&#125; GPU layers, and this node asks for &#123;ContextSize&#125; and &#123;GpuLayers&#125;. Those are fixed when the model | line 2325, `ResolveEndpointAsync` | Activity feed entry, written while a run is going on |
| loads, so it is being stopped and started again. This takes as long as loading it did. | line 2328, `ResolveEndpointAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; has no network model selected. Pick one in the Network tab or the node settings. | line 2353, `ResolveNetwork` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; cannot use &#123;networkModel.DisplayLabel&#125;: this install's mesh node is not running. Start it from the Network tab. | line 2358, `ResolveNetwork` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; cannot use &#123;networkModel.DisplayLabel&#125;. &#123;detail&#125; &#123;Title&#125; cannot use &#123;networkModel.DisplayLabel&#125; yet. &#123;detail&#125; | line 2371, `ResolveNetwork` | Run fault or refusal, shown when something is stopped or declined |
| Coverage plan | line 2378, `ResolveNetwork` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: &#123;plan.Summary&#125; | line 2378, `ResolveNetwork` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Nodes/NodeFactory.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Unknown node type '&#123;typeKey&#125;'. | line 103, `CreateContributed` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Nodes/OutputNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;Title&#125;: &#123;items.Count&#125; item(s) to write They carry no file names of their own, so each is written to &#123;RelativePathPreview&#125; in turn and the last one is what remains. | line 105, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;index + 1&#125; of &#123;items.Count&#125; | line 118, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125; received nothing to write. Connect a node to its Code pin. | line 133, `WriteOnceAsync` | Run fault or refusal, shown when something is stopped or declined |
| Writing &#123;displayPath&#125; was declined. | line 151, `WriteOnceAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;displayPath&#125;  (&#123;bytes&#125; bytes) | line 168, `WriteOnceAsync` | Status line, updated as the thing it describes changes |
| Wrote &#123;displayPath&#125; &#123;bytes&#125; bytes | line 169, `WriteOnceAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; received an empty plan, so there was nothing to write. | line 200, `WritePlanAsync` | Run fault or refusal, shown when something is stopped or declined |
| Writing &#123;files.Count&#125; file(s) was declined. | line 218, `WritePlanAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;file.RelativePath&#125; is waiting It does not compile yet, so it was kept rather than written.&#123;Environment.NewLine&#125;&#123;file.CheckDetail&#125; | line 251, `WritePlanAsync` | Activity feed entry, written while a run is going on |
| &#123;file.RelativePath&#125; was refused | line 306, `WritePlanAsync` | Activity feed entry, written while a run is going on |
| &#123;file.RelativePath&#125; could not be written | line 328, `WritePlanAsync` | Activity feed entry, written while a run is going on |
| &#123;(file.Operation == FileOperation.Create ? | line 346, `WritePlanAsync` | Activity feed entry, written while a run is going on |
| &#123;file.RelativePath&#125; needs attaching | line 358, `WritePlanAsync` | Activity feed entry, written while a run is going on |
| &#123;written&#125; file(s), &#123;bytes&#125; bytes &#123;written&#125; file(s) written, &#123;staged&#125; waiting | line 363, `WritePlanAsync` | Status line, updated as the thing it describes changes |
| &#123;staged&#125; file(s) waiting to be resolved &#123;written&#125; file(s) are on disk. Say what to do about the rest in the box below; they are kept with the project, so closing the application does not lose them. | line 369, `WritePlanAsync` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Nodes/PromptNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Empty request &#123;request.Length&#125; characters | line 33, `ExecuteAsync` | Status line, updated as the thing it describes changes |

### `src/LocalNEXUS.App/Nodes/ReshapeNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;index + 1&#125; of &#123;items.Count&#125; | line 241, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| &#123;rule.Kind&#125;&#123;(wired ? | line 258, `ReshapeOnceAsync` | Status line, updated as the thing it describes changes |
| : string.Empty)&#125;: &#123;input.Length&#125; to &#123;output.Length&#125; characters | line 258, `ReshapeOnceAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125; cannot produce a new attempt: nothing that can revise is wired into it. | line 337, `ReviseAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; has nothing to extract with. Give it a pattern, or wire a rule into it. | line 429, `ApplyExtract` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; could not read its pattern: &#123;ex.Message&#125;&#123;Environment.NewLine&#125;&#123;rule.Primary&#125; | line 448, `ApplyExtract` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; gave up on its pattern after &#123;PatternTimeout.TotalSeconds:0&#125; seconds. It matches this input too slowly to use:&#123;Environment.NewLine&#125;&#123;rule.Primary&#125; | line 453, `ApplyExtract` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; was given \"&#123;rule.Primary&#125;\" as a length to trim to, which is not a number of characters. | line 471, `ApplyTrim` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; has no pattern to apply. Type one, or wire a rule into it. | line 496, `ApplyPattern` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; could not read its pattern: &#123;ex.Message&#125;&#123;Environment.NewLine&#125;&#123;rule.Primary&#125; | line 506, `ApplyPattern` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; gave up on its pattern after &#123;PatternTimeout.TotalSeconds:0&#125; seconds. It matches this input too slowly to use:&#123;Environment.NewLine&#125;&#123;rule.Primary&#125; | line 511, `ApplyPattern` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; script failed at run time: &#123;ex.Message&#125; | line 539, `RunScriptAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; cannot compile a script in this build: the script compiler needs the runtime assemblies as files, and a single file executable keeps them inside itself. Use Find and replace instead, or run from a build that is not published as a single file. | line 555, `GetOrCompileRunner` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; script did not compile: &#123;diagnostics&#125; | line 572, `GetOrCompileRunner` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Nodes/ReshapeRule.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| The rule pin is wired but nothing arrived on it. Disconnect it to use the rule on the node, or fix whatever should be producing one. | line 77, `Parse` | Run fault or refusal, shown when something is stopped or declined |
| The rule says its kind is \"&#123;kindText&#125;\", which is not one this node knows. Use inject, extract, replace, trim or script. | line 138, `ReadKind` | Run fault or refusal, shown when something is stopped or declined |
| The rule says it is a &#123;kind.ToString().ToLowerInvariant()&#125; rule but carries nothing to apply. Inject needs a template, extract and replace need a pattern, trim needs a length, and script needs an expression. | line 183, `TryParseJson` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Nodes/TextOutputNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;index + 1&#125; of &#123;arriving.Count&#125; | line 152, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| nothing arrived | line 166, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;: &#123;lines&#125; line(s) | line 172, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; received nothing | line 172, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; has a model wired in but nothing to ask it. Wire something into its text input, or type a request. | line 196, `AskAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; cannot ask that model: &#123;reason&#125; | line 202, `AskAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Nodes/ToolSelection.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| starting it and asking | line 125, `HasTools` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Nodes/TriageNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;Title&#125; received no request to plan. | line 119, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; needs an open project to know what already exists. Open one from the File menu. | line 127, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125;: context budget | line 131, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: project index | line 137, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: &#123;candidates.Count&#125; candidate file(s) Nothing in the project looked related, so this plans from scratch. | line 142, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;c.File.RelativePath&#125;  (&#123;c.Reason&#125;) | line 145, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; has no model to plan with. Wire a Model node's Model output into its Model input. | line 163, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; cannot plan: &#123;whyNot&#125; | line 168, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; found something that is not a node to plan with. | line 172, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125;: planning with &#123;plannerNode.Title&#125; The model that writes the files is the one that plans them. | line 175, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: nothing in the request names anything in this project Asking rather than guessing which of them was meant. Planning from a request that names nothing means choosing files on the model's behalf, which is how working | line 197, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| code gets rewritten. | line 200, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125; produced no files to write. &#123;why&#125;&#123;Environment.NewLine&#125;&#123;Environment.NewLine&#125; The planner replied:&#123;Environment.NewLine&#125;&#123;reply&#125; | line 242, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Title&#125; needs &#123;(questions.Count == 1 ? | line 278, `ResolveAsync` | Activity feed entry, written while a run is going on |
| some answers | line 278, `ResolveAsync` | Activity feed entry, written while a run is going on |
| Waiting on an answer | line 293, `ResolveAsync` | Status line, updated as the thing it describes changes |
| Waiting on &#123;questions.Count&#125; answers | line 293, `ResolveAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;: planning with the answers | line 306, `ResolveAsync` | Activity feed entry, written while a run is going on |
| Proceeding on an assumption | line 313, `ResolveAsync` | Status line, updated as the thing it describes changes |
| &#123;Title&#125;: nobody answered, so it assumed | line 314, `ResolveAsync` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: &#123;row.RelativePath&#125; does not exist yet It was planned as an edit and will be created instead. | line 378, `BuildPlan` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: decisions about what already exists | line 491, `Report` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: refused as a duplicate | line 496, `Report` | Activity feed entry, written while a run is going on |
| &#123;Title&#125;: &#123;plan.Summary&#125; | line 501, `Report` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Nodes/UnavailableNode.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| '&#123;TypeKey&#125;' is contributed by an extension that is not installed for this project. Install it from Settings, Extensions, then open this graph again. The node and its wires have been kept. | line 63, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |

## View models


### `src/LocalNEXUS.App/ViewModels/ActivityFeedViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| No vision model | line 113, `AttachImageAsync` | Activity feed entry, written while a run is going on |
| No vision model | line 119, `AttachImageAsync` | Activity feed entry, written while a run is going on |
| Reading an image | line 126, `AttachImageAsync` | Activity feed entry, written while a run is going on |
| Read an image in &#123;reading.Elapsed.TotalSeconds:0.0&#125; s | line 137, `AttachImageAsync` | Activity feed entry, written while a run is going on |
| The image was not read | line 148, `AttachImageAsync` | Activity feed entry, written while a run is going on |
| Run could not start | line 407, `RunAsync` | Activity feed entry, written while a run is going on |
| Run cost &#123;RunCost.Format(_cost.Total)&#125; across &#123;_cost.Calls&#125; call(s). | line 417, `RunAsync` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/ViewModels/AddExtensionViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Add from npm Add from git Add by command | line 67, `Title` | Bound to the interface and read whenever the panel is drawn |
| Package name Repository url | line 86, `ValueLabel` | Bound to the interface and read whenever the panel is drawn |
| for example anklebreaker-unity-mcp@latest the executable to run, such as node or a full path | line 94, `ValueHint` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/ViewModels/AppSettingsViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Nothing configured. | line 212, `VisionStatus` | Bound to the interface and read whenever the panel is drawn |
| A key is stored. The request box has a search checkbox on it, and a model may call search during a run when that is ticked. No key, so search is not offered anywhere. Brave gives five dollars of credit a month, | line 323, `SearchStatus` | Bound to the interface and read whenever the panel is drawn |
| then charges per thousand requests. Get a key at &#123;Services.Search.WebSearchService.KeyUrl&#125; | line 326, `SearchStatus` | Bound to the interface and read whenever the panel is drawn |
| The page could not be opened | line 354, `OpenSearchKeyUrl` | Modal dialog, shown over whatever is on screen |
| &#123;SearchKeyUrl&#125;: &#123;ex.Message&#125; | line 354, `OpenSearchKeyUrl` | Modal dialog, shown over whatever is on screen |
| The Unity write rules are in force: a file name has to match its MonoBehaviour, and a type, namespace or serialized field cannot quietly change name. The Unity write rules do not apply, so a rename that would break a scene is not refused. | line 425, `ProjectKindNote` | Bound to the interface and read whenever the panel is drawn |
| &#123;Services.Files.ProjectSettings.SharedFileName&#125; holds the folder and the project kind, for everybody working on this project. Your model choice and the tool call switch stay in &#123;Services.Files.ProjectSettings.LocalFileName&#125;, which is never committed. | line 445, `ProjectSharingNote` | Bound to the interface and read whenever the panel is drawn |
| Everything is in &#123;Services.Files.ProjectSettings.LocalFileName&#125;, which is added to .gitignore if this project has one. | line 448, `ProjectSharingNote` | Bound to the interface and read whenever the panel is drawn |
| Answering on the local pipe &#123;Services.Mcp.McpBridge.PipeName&#125;. Point an MCP client at LocalNEXUS.Mcp.exe beside the application. Not answering. Other tools cannot drive this installation. | line 510, `McpServerStatus` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/ViewModels/CloudProvidersViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| key saved | line 39, `StatusText` | Bound to the interface and read whenever the panel is drawn |
| no key yet | line 39, `StatusText` | Bound to the interface and read whenever the panel is drawn |
| No warning. Runs start whatever they might cost. A run that could cost more than &#123;RunCost.Format(CostWarningThreshold)&#125; asks first. | line 113, `ThresholdSummary` | Bound to the interface and read whenever the panel is drawn |
| &#123;row.Provider.DisplayName&#125; key saved | line 131, `SaveKey` | Activity feed entry, written while a run is going on |
| Encrypted for this Windows account. It is never written into a graph. | line 131, `SaveKey` | Activity feed entry, written while a run is going on |
| Could not open the browser | line 163, `GetKey` | Modal dialog, shown over whatever is on screen |
| No address given | line 179, `AddCustom` | Modal dialog, shown over whatever is on screen |
| A custom endpoint needs the base url of its API. | line 179, `AddCustom` | Modal dialog, shown over whatever is on screen |
| Already added | line 187, `AddCustom` | Modal dialog, shown over whatever is on screen |
| There is already an endpoint called &#123;provider.DisplayName&#125;. | line 187, `AddCustom` | Modal dialog, shown over whatever is on screen |

### `src/LocalNEXUS.App/ViewModels/ExtensionsViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| No extensions for this project yet. Install one from Presets, or add your own. Open a project first. Extensions belong to a project, because what they talk to does. | line 191, `EmptyMessage` | Bound to the interface and read whenever the panel is drawn |
| No log yet This extension has not been started in this session, so it has not written anything. | line 437, `ViewLogs` | Modal dialog, shown over whatever is on screen |
| No project open Extensions are registered against a project, so open one first. | line 449, `AddAsync` | Modal dialog, shown over whatever is on screen |
| &#123;extension.Manifest.Name&#125; added | line 476, `AddAsync` | Activity feed entry, written while a run is going on |
| Extension not added | line 480, `AddAsync` | Modal dialog, shown over whatever is on screen |
| &#123;extension.Manifest.Name&#125; needs something first | line 501, `ResolveAsync` | Modal dialog, shown over whatever is on screen |
| &#123;m.Prerequisite.Name&#125;&#123;Environment.NewLine&#125;&#123;m.Prerequisite.Reason&#125;&#123;Environment.NewLine&#125;&#123;m.Detail&#125; | line 504, `ResolveAsync` | Modal dialog, shown over whatever is on screen |
| &#123;extension.Manifest.Name&#125; was not added Its prerequisites were declined, so nothing was installed and nothing was registered. | line 523, `ResolveAsync` | Activity feed entry, written while a run is going on |
| &#123;result.Prerequisite.Name&#125; was not installed | line 543, `ResolveAsync` | Modal dialog, shown over whatever is on screen |

### `src/LocalNEXUS.App/ViewModels/GraphDocumentViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| not saved to disk yet | line 74, `PathText` | Bound to the interface and read whenever the panel is drawn |
| &#123;nodes&#125; &#123;(nodes == 1 ? | line 84, `PathText` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/ViewModels/HistoryViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Undo puts back only the files this application wrote or edited during that run. Anything Unity regenerated, an extension changed, or you edited by hand is invisible to it, and putting a file back also discards whatever was done to it since. This is run undo, not version control. | line 109, `UndoScopeText` | Bound to the interface and read whenever the panel is drawn |
| No runs recorded for this project yet. &#123;rows.Count&#125; most recent run(s). | line 138, `RefreshAsync` | Status line, updated as the thing it describes changes |
| Nothing in this project's history matches \"&#123;SearchText&#125;\". &#123;rows.Count&#125; run(s) &#123;how&#125; \"&#123;SearchText&#125;\". | line 170, `RefreshAsync` | Status line, updated as the thing it describes changes |
| Undid a run from &#123;run.StartedAt:HH:mm:ss&#125; | line 271, `UndoAsync` | Activity feed entry, written while a run is going on |
| Undid part of a run from &#123;run.StartedAt:HH:mm:ss&#125; | line 275, `UndoAsync` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/ViewModels/MainViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Connection refused | line 228, `_panelHeight` | Activity feed entry, written while a run is going on |
| &#123;TitleText&#125; - LocalNEXUS | line 496, `WindowTitle` | Bound to the interface and read whenever the panel is drawn |
| Node not added | line 646, `AddNode` | Modal dialog, shown over whatever is on screen |
| Node not added | line 669, `PlaceSearchedNode` | Modal dialog, shown over whatever is on screen |
| Node added without a wire &#123;node.Title&#125; was added, but no pin on it would take the connection from &#123;from.Owner.Title&#125;.&#123;from.Name&#125;. | line 694, `PlaceSearchedNode` | Activity feed entry, written while a run is going on |
| Started from &#123;template.Name&#125; Choose a model on each Model node, then type a request. | line 731, `ApplyTemplate` | Activity feed entry, written while a run is going on |
| Part of the template did not open | line 736, `ApplyTemplate` | Activity feed entry, written while a run is going on |
| Template not opened | line 741, `ApplyTemplate` | Modal dialog, shown over whatever is on screen |
| Saved as a template &#123;Path.GetFileName(chosen)&#125; is now on the File menu under Start from. | line 774, `SaveAsTemplate` | Activity feed entry, written while a run is going on |
| Written to &#123;chosen&#125;. It is outside the templates folder, so it will not appear on the File menu. | line 777, `SaveAsTemplate` | Activity feed entry, written while a run is going on |
| Template not saved | line 783, `SaveAsTemplate` | Modal dialog, shown over whatever is on screen |
| Last graph not found &#123;hint&#125; is gone and nothing in this project carries its identifier, so the canvas was left empty. | line 871, `RestoreProjectGraph` | Activity feed entry, written while a run is going on |
| Nothing in this project carries the identifier it recorded, so the canvas was left empty. | line 874, `RestoreProjectGraph` | Activity feed entry, written while a run is going on |
| Nothing to copy | line 919, `CopyPanel` | Activity feed entry, written while a run is going on |
| That panel is empty. | line 919, `CopyPanel` | Activity feed entry, written while a run is going on |
| &#123;text.ReplaceLineEndings( | line 924, `CopyPanel` | Activity feed entry, written while a run is going on |
| ).Split('\n').Length&#125; line(s) from the &#123;PanelTab&#125; panel. | line 924, `CopyPanel` | Activity feed entry, written while a run is going on |
| Breakpoint set | line 1013, `ToggleBreakpoint` | Activity feed entry, written while a run is going on |
| Breakpoint cleared &#123;connection.Source.Owner.Title&#125;.&#123;connection.Source.Name&#125; to &#123;connection.Target.Owner.Title&#125;.&#123;connection.Target.Name&#125; | line 1013, `ToggleBreakpoint` | Activity feed entry, written while a run is going on |
| . The run will stop here and show what is passing. | line 1016, `ToggleBreakpoint` | Activity feed entry, written while a run is going on |
| Project not opened | line 1054, `OpenProject` | Modal dialog, shown over whatever is on screen |
| &#123;Project.KindText&#125; opened &#123;Project.ProjectPath&#125;. The Unity write rules are in force: a file name has to match | line 1075, `OpenProjectFolder` | Activity feed entry, written while a run is going on |
| its MonoBehaviour, and a type, namespace or serialized field cannot quietly change name. &#123;Project.ProjectPath&#125;. An ordinary C# project, so the Unity write rules do not apply. | line 1078, `OpenProjectFolder` | Activity feed entry, written while a run is going on |
| New graph | line 1098, `NewGraph` | Activity feed entry, written while a run is going on |
| The canvas was cleared. | line 1098, `NewGraph` | Activity feed entry, written while a run is going on |
| Graph saved | line 1156, `WriteGraph` | Activity feed entry, written while a run is going on |
| Graph not saved | line 1160, `WriteGraph` | Modal dialog, shown over whatever is on screen |
| Graph folder not created | line 1214, `GraphFolder` | Activity feed entry, written while a run is going on |
| &#123;folder&#125; could not be created: &#123;ex.Message&#125; | line 1214, `GraphFolder` | Activity feed entry, written while a run is going on |
| Graph loaded | line 1251, `LoadGraphFrom` | Activity feed entry, written while a run is going on |
| &#123;Graph.Nodes.Count&#125; nodes, &#123;Graph.Connections.Count&#125; connections from &#123;path&#125; | line 1251, `LoadGraphFrom` | Activity feed entry, written while a run is going on |
| Graph brought up to date | line 1256, `LoadGraphFrom` | Activity feed entry, written while a run is going on |
| Graph load warning | line 1261, `LoadGraphFrom` | Activity feed entry, written while a run is going on |
| Graph not loaded | line 1266, `LoadGraphFrom` | Modal dialog, shown over whatever is on screen |

### `src/LocalNEXUS.App/ViewModels/ModelBrowserViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;File.SizeGb:0.0&#125; GB | line 52, `SizeLabel` | Bound to the interface and read whenever the panel is drawn |
| The repository publishes a hash, so the download is checked. The repository publishes no hash, so the download cannot be checked. | line 63, `VerificationText` | Bound to the interface and read whenever the panel is drawn |
| Estimates are against &#123;card.GpuName&#125;, &#123;card.TotalGb:0.0&#125; GB, at &#123;ContextTokens / 1024&#125;k context. No graphics card was detected, so nothing is claimed about what will fit. | line 190, `CardSummary` | Bound to the interface and read whenever the panel is drawn |
| Nothing on Hugging Face matched &#123;Query&#125; among models published as GGUF. | line 226, `SearchAsync` | Status line, updated as the thing it describes changes |
| &#123;repository.Id&#125; is tagged GGUF and has no GGUF files in it. | line 266, `OpenAsync` | Status line, updated as the thing it describes changes |
| Downloaded and checked against the published hash. Downloaded. The repository published no hash, so it could not be checked. | line 320, `DownloadAsync` | Status line, updated as the thing it describes changes |
| Stopped. What arrived is kept, so starting again resumes from there. | line 330, `DownloadAsync` | Status line, updated as the thing it describes changes |
| Discarded, and the partly downloaded file was deleted. | line 365, `Discard` | Status line, updated as the thing it describes changes |

### `src/LocalNEXUS.App/ViewModels/ModelCatalogViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;folders&#125; folder(s) | line 42, `IsScanning` | Bound to the interface and read whenever the panel is drawn |
| Folder not added | line 79, `AddFolder` | Modal dialog, shown over whatever is on screen |

### `src/LocalNEXUS.App/ViewModels/Network/DiscoveredMeshRow.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| not joined | line 45, `StatusText` | Bound to the interface and read whenever the panel is drawn |
| &#123;Mesh.CapacityText&#125; · &#123;Mesh.NodeCount&#125; of them · &#123;Mesh.ServingText&#125; | line 110, `RowDetail` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/ViewModels/Network/HostedMeshRow.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| could not be published | line 84, `ShortId` | Bound to the interface and read whenever the panel is drawn |
| &#123;_members()&#125; &#123;(_members() == 1 ? none until it is up | line 108, `MembersText` | Bound to the interface and read whenever the panel is drawn |
| not offering this machine | line 123, `MembersText` | Bound to the interface and read whenever the panel is drawn |
| hosting, serving hosting, routing only | line 137, `StateText` | Bound to the interface and read whenever the panel is drawn |
| node stopped | line 141, `StateText` | Bound to the interface and read whenever the panel is drawn |
| The mesh is up and this machine is serving models into it. The mesh is up. This machine routes for it and is not serving any models of its own. The node is coming up. Its invite appears once it has created the mesh. | line 147, `StateDetail` | Bound to the interface and read whenever the panel is drawn |
| The node stopped with an error. What it said is under This machine. Nothing is hosted while the node is stopped. Start it and the mesh comes back with the same identity. | line 150, `StateDetail` | Bound to the interface and read whenever the panel is drawn |
| &#123;VisibilityText&#125; · &#123;MembersText&#125; · &#123;SharingText&#125; | line 167, `RowDetail` | Bound to the interface and read whenever the panel is drawn |
| MeshNodeState.&#123;State&#125;.Brush | line 170, `RowStateBrushKey` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/ViewModels/NetworkViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Off. Safetensors models are served whole on this machine. | line 185, `CanAddDistributedPeer` | Bound to the interface and read whenever the panel is drawn |
| Already listed | line 218, `AddDistributedPeer` | Activity feed entry, written while a run is going on |
| &#123;address&#125; is already one of the machines. | line 218, `AddDistributedPeer` | Activity feed entry, written while a run is going on |
| Back to the model | line 398, `ClearInspectorText` | Bound to the interface and read whenever the panel is drawn |
| Select nothing | line 398, `ClearInspectorText` | Bound to the interface and read whenever the panel is drawn |
| mesh node stopped | line 407, `ClearInspectorText` | Bound to the interface and read whenever the panel is drawn |
| &#123;MemoryShareGb:0.#&#125; GB shared, &#123;Math.Max(0d, MemoryCeilingGb - MemoryShareGb):0.#&#125; GB kept not reported | line 455, `MemoryShareLabel` | Bound to the interface and read whenever the panel is drawn |
| No graphics driver answered, so there is no ceiling to check a cap against. The engine decides how much it can use. | line 491, `MemoryReadout` | Bound to the interface and read whenever the panel is drawn |
| Nothing to copy | line 560, `CopyInvite` | Activity feed entry, written while a run is going on |
| The mesh node has not issued an invite token yet. | line 560, `CopyInvite` | Activity feed entry, written while a run is going on |
| Invite token copied | line 565, `CopyInvite` | Activity feed entry, written while a run is going on |
| It is private and only usable on the local network. | line 565, `CopyInvite` | Activity feed entry, written while a run is going on |
| Stop the node | line 604, `StartButtonText` | Bound to the interface and read whenever the panel is drawn |
| Start the node | line 604, `StartButtonText` | Bound to the interface and read whenever the panel is drawn |
| Not in anybody else's mesh. Find meshes above, pick one and join it, and it appears here. | line 778, `JoinedEmptyText` | Bound to the interface and read whenever the panel is drawn |
| joining &#123;JoiningName&#125; | line 782, `MembershipText` | Bound to the interface and read whenever the panel is drawn |
| hosting &#123;(string.IsNullOrWhiteSpace(MeshName) ? | line 785, `MembershipText` | Bound to the interface and read whenever the panel is drawn |
| : MeshName)&#125; in &#123;Joined[0].DisplayName&#125; in &#123;many&#125; meshes | line 785, `MembershipText` | Bound to the interface and read whenever the panel is drawn |
| Searching... | line 977, `FindMeshesText` | Bound to the interface and read whenever the panel is drawn |
| Find meshes | line 977, `FindMeshesText` | Bound to the interface and read whenever the panel is drawn |
| Mesh node failed | line 1126, `ToggleMeshAsync` | Activity feed entry, written while a run is going on |
| Apply and restart the node | line 1138, `ApplyButtonText` | Bound to the interface and read whenever the panel is drawn |
| Save these settings | line 1138, `ApplyButtonText` | Bound to the interface and read whenever the panel is drawn |
| Port ignored | line 1551, `ParsePort` | Activity feed entry, written while a run is going on |
| The port has to be a number between 1 and 65535. Keeping &#123;fallback&#125;. | line 1551, `ParsePort` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/ViewModels/NodeSearchViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;PinName&#125;  ·  &#123;Description&#125; | line 259, `Detail` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/ViewModels/NodeViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;elapsed.TotalSeconds.ToString( | line 124, `Elapsed` | Bound to the interface and read whenever the panel is drawn |
| , CultureInfo.InvariantCulture)&#125;s | line 124, `Elapsed` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/ViewModels/ProblemViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;Diagnostic.Line&#125;,&#123;Diagnostic.Column&#125; | line 42, `LocationText` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/ViewModels/ProjectSetupViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Detected as a &#123;Describe(Kind)&#125;. Detected as a &#123;Describe(_detected)&#125;, overridden to &#123;Describe(Kind)&#125;. | line 76, `KindNote` | Bound to the interface and read whenever the panel is drawn |
| The Unity write rules will be applied to this project. The Unity write rules will not be applied, so a rename that would break a scene will not be refused. | line 79, `KindNote` | Bound to the interface and read whenever the panel is drawn |
| &#123;ProjectSettings.SharedFileName&#125; will hold the folder and the project kind, for everybody working on this project. Your model choice and the tool call switch stay in &#123;ProjectSettings.LocalFileName&#125;, which is never committed. | line 84, `SharingNote` | Bound to the interface and read whenever the panel is drawn |
| Everything goes in &#123;ProjectSettings.LocalFileName&#125;, and it is added to .gitignore if this project has one. Turn this on to share the folder and the project kind with the rest of your team. | line 87, `SharingNote` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/ViewModels/SemanticSearchViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Off. History is searched by keyword, which finds the words that were actually written and needs no model. | line 106, `ModelName` | Bound to the interface and read whenever the panel is drawn |
| If you have no embedding model, &#123;RecommendedFile&#125; is a good small one at about 35 MB: search for &#123;RecommendedRepository&#125; under Find a model to download, above. | line 122, `Recommendation` | Bound to the interface and read whenever the panel is drawn |
| Chosen. Nothing is indexed yet: runs from now on are indexed as they finish, and Index the history covers what is already recorded. | line 143, `ChooseAsync` | Status line, updated as the thing it describes changes |
| Off. Searches are by keyword again. The vectors are kept, so turning it back on with the same model does not mean indexing everything twice. | line 159, `TurnOff` | Status line, updated as the thing it describes changes |
| Every vector was deleted. Indexing again rebuilds them. | line 169, `ForgetAsync` | Status line, updated as the thing it describes changes |
| Indexing. The first run also starts the embedding model, which takes a moment. | line 202, `BackfillAsync` | Status line, updated as the thing it describes changes |
| Indexed &#123;p.Done&#125; of &#123;p.Total&#125;. | line 208, `BackfillAsync` | Status line, updated as the thing it describes changes |
| Nothing was indexed. &#123;result.Failed&#125; run(s) could not be embedded, which usually means the file chosen is not an embedding model. Indexed &#123;result.Indexed&#125; run(s) in &#123;result.Elapsed.TotalSeconds:0.0&#125; seconds, | line 213, `BackfillAsync` | Status line, updated as the thing it describes changes |
| about &#123;result.Each.TotalMilliseconds:0&#125; ms each. &#123;result.Failed&#125; could not be embedded. | line 216, `BackfillAsync` | Status line, updated as the thing it describes changes |
| Stopped. What was indexed is kept, and indexing again carries on from there. | line 221, `BackfillAsync` | Status line, updated as the thing it describes changes |

### `src/LocalNEXUS.App/ViewModels/SpecViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| OpenSpec: &#123;change.Name&#125; | line 211, `AdvanceAsync` | Activity feed entry, written while a run is going on |
| OpenSpec: &#123;change.Name&#125; sent to the Workspace The task list is in the request box. Nothing has run yet. | line 278, `SendTasksToWorkspaceAsync` | Activity feed entry, written while a run is going on |
| OpenSpec could not be reached | line 341, `WithWorkerAsync` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.Installer/ViewModels/SetupViewModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;(int)Math.Round(InstallFraction * 100d)&#125;% | line 156, `PercentText` | Bound to the interface and read whenever the panel is drawn |
| &#123;SelectedEngineCount&#125; of 3 engines selected | line 162, `EngineCountText` | Bound to the interface and read whenever the panel is drawn |
| 1 file to download | line 165, `FetchCountText` | Bound to the interface and read whenever the panel is drawn |
| &#123;FetchList.Count&#125; files to download | line 165, `FetchCountText` | Bound to the interface and read whenever the panel is drawn |

## Services


### `src/LocalNEXUS.App/Services/Agent/AgentToolbox.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;type.Name&#125; is already declared in &#123;existing&#125;. Change that one rather than adding a second copy, or give this a different name. | line 471, `EnforceNothingDeclaredTwice` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Compilation/CompileResult.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Compiled in &#123;seconds:0&#125; ms | line 74, `IsInconclusive` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Compilation/ProjectSourceSet.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| no source files | line 45, `Empty` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Credentials/DpapiCredentialStore.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Saved keys could not be read &#123;FilePath&#125; was encrypted for a different Windows account or machine, so it cannot be decrypted here. Enter the keys again to replace it. | line 117, `Load` | Activity feed entry, written while a run is going on |
| Saved keys could not be read | line 123, `Load` | Activity feed entry, written while a run is going on |
| &#123;FilePath&#125; could not be opened: &#123;ex.Message&#125; | line 123, `Load` | Activity feed entry, written while a run is going on |
| Keys were not saved &#123;FilePath&#125; could not be written: &#123;ex.Message&#125; They will work for this session and be gone when the application closes. | line 148, `Save` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Services/Debate/ConvergenceMeter.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;value&#125; percent | line 59, `Text` | Bound to the interface and read whenever the panel is drawn |
| not measurable | line 59, `Text` | Bound to the interface and read whenever the panel is drawn |
| &#123;value&#125; percent not measurable, because &#123;Reason.TrimEnd('.').ToLowerInvariant()&#125; | line 63, `Explanation` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Diagnostics/CrashReport.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Crash on | line 135, `Title` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Diagnostics/CrashReporter.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| LocalNEXUS crashed the last time it ran Sorry. Here is what was recorded, with your user name and the paths under it | line 95, `AskAboutAnyCrash` | Modal dialog, shown over whatever is on screen |
| Nothing has been sent anywhere. Open a pre-filled issue on GitHub so this can be fixed? It opens in your browser with this text in it, and you can read and change | line 99, `AskAboutAnyCrash` | Modal dialog, shown over whatever is on screen |

### `src/LocalNEXUS.App/Services/Distributed/CoveragePlan.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;a.Section.Label&#125;: &#123;a.SourceText&#125; | line 73, `Summary` | Status line, updated as the thing it describes changes |

### `src/LocalNEXUS.App/Services/Distributed/DiscoveredMesh.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;NodeCount&#125; &#123;(NodeCount == 1 ? nobody using it yet. &#123;ClientCount&#125; already using it. | line 85, `IsLookingForModels` | Bound to the interface and read whenever the panel is drawn |
| &#123;CapacityGb:0.#&#125; GB | line 89, `CapacityText` | Bound to the interface and read whenever the panel is drawn |
| not reported | line 89, `CapacityText` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Distributed/InferenceSource.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;ShortId&#125; at &#123;rtt&#125; ms | line 87, `EndpointText` | Bound to the interface and read whenever the panel is drawn |
| &#123;MemoryMb&#125; MiB | line 95, `EndpointText` | Bound to the interface and read whenever the panel is drawn |
| memory not announced | line 95, `EndpointText` | Bound to the interface and read whenever the panel is drawn |
| &#123;memory&#125;, serving 1 model | line 99, `EndpointText` | Bound to the interface and read whenever the panel is drawn |
| &#123;DisplayName&#125; (&#123;ShortId&#125;) | line 110, `ToString` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Distributed/JoinedMesh.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| in it, models ready loading models reaching the mesh | line 80, `StateText` | Bound to the interface and read whenever the panel is drawn |
| starting the node node stopped | line 83, `StateText` | Bound to the interface and read whenever the panel is drawn |
| The mesh is attached and its models can answer. Attached. The runtime is bringing models up, which is the slow part and is disk bound. The node is up and looking for this mesh over the network. It is not attached yet. | line 91, `StateDetail` | Bound to the interface and read whenever the panel is drawn |
| The node process has started and has not answered its own console yet. This takes a second or two. The node stopped with an error. What it said is under This machine. The invite is saved and the node is not running, so nothing is connected. Start the node. | line 94, `StateDetail` | Bound to the interface and read whenever the panel is drawn |
| &#123;ShortId&#125; · &#123;StateText&#125; | line 130, `RowDetail` | Bound to the interface and read whenever the panel is drawn |
| JoinState.&#123;State&#125;.Brush | line 133, `RowStateBrushKey` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Distributed/MeshDirectory.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Mesh directory unavailable &#123;AppPaths.MeshExecutableName&#125; was not found, so there is nothing to ask. | line 226, `RunAsync` | Activity feed entry, written while a run is going on |
| Mesh directory did not answer | line 269, `RunAsync` | Activity feed entry, written while a run is going on |
| Nothing came back within &#123;Timeout.TotalSeconds:0&#125; seconds. | line 269, `RunAsync` | Activity feed entry, written while a run is going on |
| Mesh directory could not be asked | line 274, `RunAsync` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Services/Distributed/MeshManager.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Starting the mesh node. Models appear here as the mesh reports them. The mesh node is not running. Its last error is under This machine. The mesh node is not running. Start it to see what your own mesh can serve, or find meshes to see who else is out there. | line 152, `EmptyModelsText` | Bound to the interface and read whenever the panel is drawn |
| The mesh node is up and has not reported a model yet. | line 155, `EmptyModelsText` | Bound to the interface and read whenever the panel is drawn |
| Starting the mesh node. Sources appear here as it finds them. The mesh node is not running. The mesh node is not running. | line 161, `EmptySourcesText` | Bound to the interface and read whenever the panel is drawn |
| The mesh node is up and has not reported a source yet. | line 164, `EmptySourcesText` | Bound to the interface and read whenever the panel is drawn |
| Mesh node not started | line 210, `RestoreAsync` | Activity feed entry, written while a run is going on |
| Mesh node starting Serving on port &#123;options.ApiPort&#125;, &#123;(options.Publish ? private mesh on the local network | line 252, `StartAsync` | Activity feed entry, written while a run is going on |
| Joining as a client on port &#123;options.ApiPort&#125;. | line 255, `StartAsync` | Activity feed entry, written while a run is going on |
| Models not offered The mesh node did not answer in time, so the models after the first were not loaded. | line 300, `LoadAdditionalModelsAsync` | Activity feed entry, written while a run is going on |
| Model offered | line 316, `LoadAdditionalModelsAsync` | Activity feed entry, written while a run is going on |
| Model not offered | line 320, `LoadAdditionalModelsAsync` | Activity feed entry, written while a run is going on |
| &#123;name&#125; was refused by the mesh node. | line 320, `LoadAdditionalModelsAsync` | Activity feed entry, written while a run is going on |
| Model not offered | line 325, `LoadAdditionalModelsAsync` | Activity feed entry, written while a run is going on |
| &#123;name&#125;: &#123;ex.Message&#125; | line 325, `LoadAdditionalModelsAsync` | Activity feed entry, written while a run is going on |
| Token not replaced | line 346, `RotateIdentityAsync` | Activity feed entry, written while a run is going on |
| Token not replaced | line 363, `RotateIdentityAsync` | Activity feed entry, written while a run is going on |
| The mesh node refused to rotate its identity. | line 363, `RotateIdentityAsync` | Activity feed entry, written while a run is going on |
| Invite token replaced This machine has a new identity, so the previous token no longer works and anyone who had it is no longer in this mesh. | line 368, `RotateIdentityAsync` | Activity feed entry, written while a run is going on |
| Token not replaced | line 373, `RotateIdentityAsync` | Activity feed entry, written while a run is going on |
| The mesh command could not be started. | line 404, `RunMeshCommandAsync` | Run fault or refusal, shown when something is stopped or declined |
| Mesh node stopped | line 459, `StopAsync` | Activity feed entry, written while a run is going on |
| This install left the mesh. Local inference is unaffected. | line 459, `StopAsync` | Activity feed entry, written while a run is going on |
| Windows did not start the mesh node and gave no reason. | line 533, `StartProcess` | Run fault or refusal, shown when something is stopped or declined |
| Could not start the mesh node: &#123;ex.Message&#125; | line 537, `StartProcess` | Run fault or refusal, shown when something is stopped or declined |
| Mesh node stopped unexpectedly The node process exited with code &#123;exitCode&#125;. Local inference is unaffected; the distributed path is unavailable until it is started again. | line 648, `ReportProcessDeathAsync` | Activity feed entry, written while a run is going on |
| Mesh node ready &#123;DescribeMesh()&#125; with &#123;Count(Sources.Count, )&#125; and &#123;Count(complete, )&#125; ready to serve. | line 707, `Apply` | Activity feed entry, written while a run is going on |
| Source joined | line 804, `ReconcileSources` | Activity feed entry, written while a run is going on |
| &#123;peer.DisplayName&#125; joined &#123;DescribeMesh()&#125;. | line 804, `ReconcileSources` | Activity feed entry, written while a run is going on |
| Source left | line 819, `ReconcileSources` | Activity feed entry, written while a run is going on |
| &#123;gone.DisplayName&#125; is no longer in the mesh. | line 819, `ReconcileSources` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Services/Distributed/ModelSection.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| section &#123;Index + 1&#125; (layers &#123;FirstLayer&#125;-&#123;LastLayer&#125;) section &#123;Index + 1&#125; | line 35, `Label` | Bound to the interface and read whenever the panel is drawn |
| SECTION &#123;Index + 1&#125; | line 39, `Ordinal` | Bound to the interface and read whenever the panel is drawn |
| layers &#123;FirstLayer&#125;-&#123;LastLayer&#125; | line 42, `LayerRangeText` | Bound to the interface and read whenever the panel is drawn |
| layers not reported | line 42, `LayerRangeText` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Distributed/NetworkServedModel.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| not reported | line 142, `ContextText` | Bound to the interface and read whenever the panel is drawn |
| not reported | line 145, `ParametersText` | Bound to the interface and read whenever the panel is drawn |
| 1 spare machine | line 148, `SpareText` | Bound to the interface and read whenever the panel is drawn |
| &#123;WeakestSpare&#125; spare machines | line 148, `SpareText` | Bound to the interface and read whenever the panel is drawn |
| Complete and armed. Every section is serving, with | line 176, `ChainStatusText` | Bound to the interface and read whenever the panel is drawn |
| behind the weakest. Complete and armed. Every section is serving, with no spare machine behind the weakest. Blocked: the mesh cannot assemble this model right now. | line 176, `ChainStatusText` | Bound to the interface and read whenever the panel is drawn |
| Starting. Waiting for the mesh to report how this model is assembled. | line 179, `ChainStatusText` | Bound to the interface and read whenever the panel is drawn |
| &#123;ParameterSize&#125; parameters | line 200, `HasDepth3` | Bound to the interface and read whenever the panel is drawn |
| 1 source | line 217, `PeerCountText` | Bound to the interface and read whenever the panel is drawn |
| &#123;PeerCount&#125; sources | line 217, `PeerCountText` | Bound to the interface and read whenever the panel is drawn |
| &#123;Name&#125; (&#123;Quantization&#125;) | line 220, `DisplayLabel` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Distributed/SourceAssignment.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| no source | line 48, `SourceText` | Bound to the interface and read whenever the panel is drawn |
| waiting for a source | line 48, `SourceText` | Bound to the interface and read whenever the panel is drawn |
| 1 spare source | line 54, `CoverageText` | Bound to the interface and read whenever the panel is drawn |
| &#123;SpareSources&#125; spare sources no spare source not placed yet | line 54, `CoverageText` | Bound to the interface and read whenever the panel is drawn |
| not in the mesh | line 58, `CoverageText` | Bound to the interface and read whenever the panel is drawn |
| &#123;Section.Label&#125; is serving on &#123;SourceText&#125;. The mesh has not placed &#123;Section.Label&#125; yet. &#123;Section.Label&#125; is coming up on &#123;SourceText&#125; (&#123;CoverageText&#125;). | line 71, `StatusDetail` | Bound to the interface and read whenever the panel is drawn |
| No source in the mesh holds &#123;Section.Label&#125;. &#123;Section.Label&#125; is on &#123;SourceText&#125; but the mesh reports it &#123;CoverageText&#125;. | line 74, `StatusDetail` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Editing/CodeEditApplier.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| The coder returned nothing, so there is no change to apply. | line 145, `Apply` | Run fault or refusal, shown when something is stopped or declined |
| The reply stopped part way through and left a comment saying the rest of the file is unchanged. Writing that would delete everything it stood for. Return the complete file with every member written out. | line 153, `Apply` | Run fault or refusal, shown when something is stopped or declined |
| The coder returned a diff for a new file, and it added no lines. | line 168, `Apply` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Editing/LineTaggedDiff.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| The reply contained no change blocks, so there was nothing to apply. | line 139, `Apply` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Execution/BreakpointService.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Stopped on the wire from &#123;connection.Source.Owner.Title&#125; &#123;stop.Where&#125;. Edit what is passing, or release it. | line 48, `HoldAsync` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Services/Execution/BreakpointStop.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| This wire is carrying &#123;DescribeType(_original)&#125;, which is shown as it is rather than as text that could be typed back into it. Release it, or stop the run. | line 56, `ReadOnlyReason` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Execution/GraphExecutor.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| A run is already in progress. | line 65, `RunAsync` | Run fault or refusal, shown when something is stopped or declined |
| Run started | line 93, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;run.Graph.Nodes.Count&#125; nodes, &#123;run.Graph.Connections.Count&#125; connections | line 93, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| Read as configuration by another node. Nothing ran here. | line 115, `ExecuteAsync` | Status line, updated as the thing it describes changes |
| Run finished with work left over | line 161, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| Run completed &#123;sort.Ordered.Count&#125; nodes in &#123;stopwatch.Elapsed.TotalSeconds:0.0&#125; s. &#123;_services.Staging.Summary&#125;. | line 161, `ExecuteAsync` | Activity feed entry, written while a run is going on |
| &#123;sort.Ordered.Count&#125; nodes in &#123;stopwatch.Elapsed.TotalSeconds:0.0&#125; s | line 164, `ExecuteAsync` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Services/Extensions/ExtensionHost.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;extension.Manifest.Name&#125; is switched off, so it was not started. | line 81, `EnsureRunningAsync` | Run fault or refusal, shown when something is stopped or declined |
| Windows did not start '&#123;launch.Command&#125;' and did not say why. | line 244, `StartAsync` | Run fault or refusal, shown when something is stopped or declined |
| '&#123;launch.Command&#125;' could not be run. It is either not installed or not on the path. The full command was: &#123;launch.DisplayCommand&#125; | line 251, `StartAsync` | Run fault or refusal, shown when something is stopped or declined |
| extension &#123;extension.Manifest.Id&#125; | line 255, `StartAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;extension.Manifest.Name&#125; exited immediately with code &#123;process.ExitCode&#125;. Its log is at &#123;logPath&#125;. It said: &#123;tail&#125; | line 278, `StartAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;extension.Manifest.Name&#125; wrote to stdout stdout carries the protocol and nothing else, so this line was discarded. Logging belongs on stderr, which is captured at &#123;logPath&#125;. The line was: &#123;Truncate(line)&#125; | line 307, `StartRpc` | Activity feed entry, written while a run is going on |
| &#123;extension.Manifest.Name&#125; started but never answered the MCP handshake within &#123;StartTimeout.TotalSeconds:0&#125; seconds. Its log is at &#123;logPath&#125;. | line 362, `StartMcpAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;extension.Manifest.Name&#125; started but the MCP handshake failed: &#123;ex.Message&#125; Its log is at &#123;logPath&#125;. | line 369, `StartMcpAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Extensions/ExtensionInstaller.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| No package name was given. | line 51, `FromNpm` | Run fault or refusal, shown when something is stopped or declined |
| No repository url was given. | line 95, `FromGitAsync` | Run fault or refusal, shown when something is stopped or declined |
| Cloning &#123;url&#125; failed with exit code &#123;exit&#125;. Check the url, and that git is installed and on the path. | line 121, `FromGitAsync` | Run fault or refusal, shown when something is stopped or declined |
| There is no folder at &#123;folder&#125;. | line 133, `FromDisk` | Run fault or refusal, shown when something is stopped or declined |
| &#123;folder&#125; has no &#123;ExtensionManifestJson.FileName&#125;, so there is nothing saying what it contributes or how to start it. Add one, or use the command option to launch it directly. | line 141, `FromDisk` | Run fault or refusal, shown when something is stopped or declined |
| &#123;manifestPath&#125; is not a JSON object. | line 151, `FromDisk` | Run fault or refusal, shown when something is stopped or declined |
| &#123;manifestPath&#125; is not valid JSON: &#123;ex.Message&#125; | line 158, `FromDisk` | Run fault or refusal, shown when something is stopped or declined |
| &#123;manifestPath&#125; could not be read: &#123;ex.Message&#125; | line 162, `FromDisk` | Run fault or refusal, shown when something is stopped or declined |
| No command was given. | line 190, `FromCommand` | Run fault or refusal, shown when something is stopped or declined |
| Say which contract this speaks. Without one the host has no way to talk to it. | line 196, `FromCommand` | Run fault or refusal, shown when something is stopped or declined |
| &#123;prerequisite.Name&#125; cannot be installed from here. &#123;prerequisite.Reason&#125; | line 230, `InstallPrerequisiteAsync` | Run fault or refusal, shown when something is stopped or declined |
| Installing &#123;prerequisite.Name&#125; | line 233, `InstallPrerequisiteAsync` | Run fault or refusal, shown when something is stopped or declined |
| Installing &#123;prerequisite.Name&#125; failed with exit code &#123;exit&#125;. The command was: &#123;prerequisite.InstallCommand&#125; &#123;string.Join(' ', prerequisite.InstallArguments ?? Array.Empty<string>())&#125; | line 246, `InstallPrerequisiteAsync` | Run fault or refusal, shown when something is stopped or declined |
| Windows did not start '&#123;command&#125;'. | line 277, `RunAsync` | Run fault or refusal, shown when something is stopped or declined |
| '&#123;command&#125;' could not be run. It is either not installed or not on the path. | line 282, `RunAsync` | Run fault or refusal, shown when something is stopped or declined |
| extension install &#123;command&#125; | line 286, `RunAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Extensions/ExtensionManifestJson.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| '&#123;id&#125;' declares the contract '&#123;name&#125;', which does not exist. The contracts are: &#123;string.Join(, Enum.GetNames<ExtensionContract>())&#125;. | line 39, `Read` | Run fault or refusal, shown when something is stopped or declined |
| '&#123;id&#125;' declares no contracts, so there is nothing the host could do with it. Add at least one of: &#123;string.Join(, Enum.GetNames<ExtensionContract>())&#125;. | line 50, `Read` | Run fault or refusal, shown when something is stopped or declined |
| '&#123;id&#125;' has no 'launch' section, so there is no way to start it. | line 177, `ReadLaunch` | Run fault or refusal, shown when something is stopped or declined |
| '&#123;id&#125;' has a 'launch' section with no 'command' in it. | line 184, `ReadLaunch` | Run fault or refusal, shown when something is stopped or declined |
| A node contributed by '&#123;id&#125;' has no 'typeKey'. | line 232, `ReadNodes` | Run fault or refusal, shown when something is stopped or declined |
| A pin on '&#123;typeKey&#125;' has no 'name'. | line 252, `ReadPins` | Run fault or refusal, shown when something is stopped or declined |
| '&#123;id&#125;' declares a prerequisite of kind '&#123;kindName&#125;', which does not exist. The kinds are: &#123;string.Join(, Enum.GetNames<PrerequisiteKind>())&#125;. | line 275, `ReadPrerequisites` | Run fault or refusal, shown when something is stopped or declined |
| A prerequisite of '&#123;id&#125;' has no 'name'. installCommand | line 281, `ReadPrerequisites` | Run fault or refusal, shown when something is stopped or declined |
| installArguments minimumVersion | line 284, `ReadPrerequisites` | Run fault or refusal, shown when something is stopped or declined |
| The manifest has no '&#123;field&#125;', which every extension needs. | line 297, `Required` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Extensions/ExtensionPinTypes.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| The pin '&#123;pinName&#125;' on node '&#123;typeKey&#125;' does not say what type it carries. It has to be one of: &#123;string.Join(, Available)&#125;. | line 41, `Parse` | Run fault or refusal, shown when something is stopped or declined |
| The pin '&#123;pinName&#125;' on node '&#123;typeKey&#125;' asks for the type '&#123;declared&#125;', which does not exist. Extensions use the types the graph already has: &#123;string.Join(, Available)&#125;. A new pin type would only match another extension's by name, so it is refused rather than guessed at. | line 51, `Parse` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Extensions/ExtensionRegistry.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Extensions not loaded | line 88, `OpenProject` | Activity feed entry, written while a run is going on |
| &#123;file&#125; is not in the expected shape, so it was ignored. | line 88, `OpenProject` | Activity feed entry, written while a run is going on |
| Extensions not loaded | line 100, `OpenProject` | Activity feed entry, written while a run is going on |
| &#123;file&#125; could not be read: &#123;ex.Message&#125; | line 100, `OpenProject` | Activity feed entry, written while a run is going on |
| Extensions not saved | line 178, `Save` | Activity feed entry, written while a run is going on |
| &#123;file&#125; could not be written: &#123;ex.Message&#125; | line 178, `Save` | Activity feed entry, written while a run is going on |
| Extensions moved This project's extension registry moved from &#123;legacy&#125; to &#123;file&#125;, which is where everything this application keeps about a project now lives. | line 215, `MoveFromLegacyLocation` | Activity feed entry, written while a run is going on |
| Extensions not moved &#123;legacy&#125; could not be moved to &#123;file&#125;, so it is being read where it is. &#123;ex.Message&#125; | line 224, `MoveFromLegacyLocation` | Activity feed entry, written while a run is going on |
| Extension not loaded | line 271, `LoadOne` | Activity feed entry, written while a run is going on |
| An entry in &#123;file&#125; could not be read: &#123;ex.Message&#125; | line 271, `LoadOne` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Services/Extensions/ExtensionStarter.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;answered&#125; of &#123;pending.Count&#125; extension(s) answered | line 120, `ConnectAllAsync` | Activity feed entry, written while a run is going on |
| The rest are listed as unreachable in Extensions, each with what stopped it. | line 123, `ConnectAllAsync` | Activity feed entry, written while a run is going on |
| &#123;extension.Manifest.Name&#125; answered | line 158, `ConnectAsync` | Activity feed entry, written while a run is going on |
| &#123;tools.Count&#125; tool(s). | line 158, `ConnectAsync` | Activity feed entry, written while a run is going on |
| &#123;extension.Manifest.Name&#125; answered | line 169, `ConnectAsync` | Activity feed entry, written while a run is going on |
| &#123;described.Count&#125; node type(s). | line 169, `ConnectAsync` | Activity feed entry, written while a run is going on |
| &#123;extension.Manifest.Name&#125; did not answer | line 197, `ConnectAsync` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Services/Extensions/JsonRpcConnection.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| The extension is not running, so '&#123;method&#125;' could not be sent. Check its log for why it stopped. | line 75, `InvokeAsync` | Run fault or refusal, shown when something is stopped or declined |
| The extension closed its input before the request could be sent. It has probably exited. | line 184, `SendAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Extensions/McpToolClient.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| This session is not an MCP session, so tools cannot be listed over it. | line 32 | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Extensions/NodeWorkerClient.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| This session is not a node contract session, so the node protocol cannot be spoken over it. | line 36, `DescribeTimeout` | Run fault or refusal, shown when something is stopped or declined |
| The worker answered node/describe without a 'nodes' array, so what it contributes could not be read. | line 50, `DescribeAsync` | Run fault or refusal, shown when something is stopped or declined |
| The worker answered node/execute for '&#123;typeKey&#125;' with something that was not an object. | line 99, `ExecuteAsync` | Run fault or refusal, shown when something is stopped or declined |
| A node in node/describe has no typeKey. | line 132, `ReadNode` | Run fault or refusal, shown when something is stopped or declined |
| A pin on '&#123;typeKey&#125;' has no name. | line 155, `ReadPins` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Files/DiffStat.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| +&#123;Added&#125; -&#123;Removed&#125; | line 27, `Text` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Files/ProjectService.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Unity project C# project No project | line 47, `KindText` | Bound to the interface and read whenever the panel is drawn |
| No project open | line 59, `KindText` | Bound to the interface and read whenever the panel is drawn |
| The folder does not exist: &#123;folder&#125; | line 124, `Open` | Run fault or refusal, shown when something is stopped or declined |
| Open a project before running a graph that writes files. | line 148, `ResolveTargetPath` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Files/ProjectSettingsService.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| This project's settings were not saved &#123;root&#125; could not be written to, so the answers will be asked for again next time. &#123;ex.Message&#125; | line 199, `Save` | Activity feed entry, written while a run is going on |
| Added to .gitignore, missing) + | line 281, `Append` | Activity feed entry, written while a run is going on |
| .gitignore was not updated &#123;string.Join(, missing)&#125; may be committed unless you add them yourself. &#123;ex.Message&#125; | line 287, `Append` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Services/Files/ProjectWriteBatch.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;full&#125; was planned as an edit, and there is no such file. Nothing was written. | line 58, `EnforceExpectedExistence` | Run fault or refusal, shown when something is stopped or declined |
| &#123;full&#125; was planned as a new file, and one already exists there. Overwriting it would discard whatever it holds, so nothing was written. Plan this as an edit instead. | line 65, `EnforceExpectedExistence` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Files/StagedFile.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;RelativePath&#125; was refused by the project rules &#123;RelativePath&#125; could not be written &#123;RelativePath&#125; could not be changed as asked | line 76, `Summary` | Status line, updated as the thing it describes changes |
| &#123;RelativePath&#125; could not be read &#123;RelativePath&#125; does not compile yet | line 79, `Summary` | Status line, updated as the thing it describes changes |
| The change the coder asked for Nothing was proposed, because the file could not be read The file as the coder left it | line 103, `AttemptLabel` | Bound to the interface and read whenever the panel is drawn |
| Refused. It compiles, and writing it would have broken something Unity binds by more than a name. The write failed, so the file on disk is untouched. | line 115, `ReasonText` | Bound to the interface and read whenever the panel is drawn |
| The coder kept asking to replace lines that are not in this file, so nothing was written. The file on disk is untouched. | line 118, `ReasonText` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Files/StagingStore.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| 1 file waiting to be resolved &#123;Pending.Count&#125; files waiting to be resolved | line 79, `Summary` | Status line, updated as the thing it describes changes |

### `src/LocalNEXUS.App/Services/Files/UnityScriptRules.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;relativePath&#125; declares the MonoBehaviour &#123;names&#125;, and Unity only binds a component when the file name matches its class name exactly. Name the file &#123;behaviours[0].Name&#125;.cs, or rename the class to &#123;fileName&#125;. | line 94, `EnforceFileNameMatchesBehaviour` | Run fault or refusal, shown when something is stopped or declined |
| &#123;relativePath&#125; currently declares &#123;was.Name&#125; and the new content does not. Scenes and prefabs reference a script by the GUID of its file and resolve the type by name, so removing or renaming it breaks every object using it with no compiler error. Keep &#123;was.Name&#125;, or add | line 122, `EnforceNoTypeDisappeared` | Run fault or refusal, shown when something is stopped or declined |
| [MovedFrom(true, sourceClassName: \"&#123;was.Name&#125;\")] to the type that replaces it. | line 125, `EnforceNoTypeDisappeared` | Run fault or refusal, shown when something is stopped or declined |
| &#123;relativePath&#125; moves &#123;was.Name&#125; from &#123;from&#125; to &#123;to&#125;. Unity resolves a serialized reference by namespace and class name together, so every scene and prefab using it would lose its script. Keep the namespace, or add [MovedFrom(true, sourceNamespace: \"&#123;was.Namespace&#125;\", sourceClassName: \"&#123;was.Name&#125;\")]. | line 158, `EnforceNoNamespaceChange` | Run fault or refusal, shown when something is stopped or declined |
| &#123;relativePath&#125; removes or renames the serialized field &#123;was.Name&#125;.&#123;field.Name&#125;. Unity stores serialized values by field name, so whatever is set on it in every scene and prefab would be lost. Keep the field, or mark its replacement with [FormerlySerializedAs(\"&#123;field.Name&#125;\")]. | line 194, `EnforceNoSerializedFieldRenamed` | Run fault or refusal, shown when something is stopped or declined |
| &#123;relativePath&#125; stops &#123;was.Name&#125; deriving from MonoBehaviour. Any GameObject with it attached would lose the component, and nothing about that is a compiler error. Keep the base type. | line 221, `EnforceBehaviourStaysBehaviour` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/History/RunHistoryStore.Reads.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| That search could not be run against this project's history: &#123;ex.Message&#125; | line 128, `SearchAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/History/RunHistoryStore.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Recording every run for this project. No project is open, so there is nothing to record against. | line 84, `StatusText` | Bound to the interface and read whenever the panel is drawn |
| This project's history could not be opened, so nothing is being recorded: &#123;ex.Message&#125; | line 129, `OpenProjectAsync` | Status line, updated as the thing it describes changes |

### `src/LocalNEXUS.App/Services/History/RunRecords.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Nothing recorded for this project yet. &#123;Runs&#125; run(s) recorded, &#123;Format(DatabaseBytes)&#125; of history and &#123;Snapshots&#125; snapshot(s) holding &#123;Format(SnapshotBytes)&#125;. | line 130, `Summary` | Status line, updated as the thing it describes changes |
| &#123;bytes&#125; bytes | line 135, `Format` | Status line, updated as the thing it describes changes |
| &#123;bytes / 1024.0:0.#&#125; KB &#123;bytes / (1024.0 * 1024):0.#&#125; MB | line 136, `Format` | Bound to the interface and read whenever the panel is drawn |
| &#123;Restored&#125; file(s) put back | line 160, `Complete` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Inference/AnthropicClient.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| No model is selected for this node. | line 89, `StreamChatAsync` | Run fault or refusal, shown when something is stopped or declined |
| Anthropic needs an API key. Add one in Settings under API keys, then run again. | line 95, `StreamChatAsync` | Run fault or refusal, shown when something is stopped or declined |
| Anthropic needs a maximum reply length and this node has none set. Set max tokens on the node to something above zero. | line 104, `StreamChatAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;(int)response.StatusCode&#125; &#123;response.ReasonPhrase&#125; from &#123;endpoint.SafeUrlFor(endpoint.MessagesUrl)&#125;. &#123;body&#125; | line 120, `StreamChatAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Inference/CloudProvider.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| rates not known $&#123;InputPerMillion:0.##&#125; in, $&#123;OutputPerMillion:0.##&#125; out per million tokens | line 54, `RateSummary` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Inference/DistributedRuntimeManager.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| The model folder no longer exists: &#123;model.Path&#125; | line 137, `EnsureServingAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;model.DisplayName&#125; cannot be run. &#123;unavailable&#125; | line 142, `EnsureServingAsync` | Run fault or refusal, shown when something is stopped or declined |
| The distributed inference package is missing from the vendor folder, so a model cannot be split across machines. Serve it on one machine by turning the mesh off. | line 148, `EnsureServingAsync` | Run fault or refusal, shown when something is stopped or declined |
| The Python runtime's interpreter is missing. Repair the runtime from the Local model panel. | line 336, `StartHost` | Run fault or refusal, shown when something is stopped or declined |
| Windows did not start the distributed runtime and gave no reason. | line 395, `StartHost` | Run fault or refusal, shown when something is stopped or declined |
| Could not start the distributed runtime: &#123;ex.Message&#125; | line 399, `StartHost` | Run fault or refusal, shown when something is stopped or declined |
| The distributed runtime stopped while bringing the pipeline up. Recent output:&#123;Environment.NewLine&#125;&#123;instance.GetRecentOutput()&#125; | line 431, `WaitUntilHealthyAsync` | Run fault or refusal, shown when something is stopped or declined |
| The pipeline is ready on port &#123;instance.Port&#125; | line 436, `WaitUntilHealthyAsync` | Run fault or refusal, shown when something is stopped or declined |
| The distributed runtime did not become ready within &#123;StartupTimeout.TotalMinutes:0&#125; minutes. See &#123;instance.LogPath&#125; | line 450, `WaitUntilHealthyAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Inference/GeminiClient.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| No model is selected for this node. | line 81, `StreamChatAsync` | Run fault or refusal, shown when something is stopped or declined |
| Gemini needs an API key. Add one in Settings under API keys, then run again. | line 87, `StreamChatAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;(int)response.StatusCode&#125; &#123;response.ReasonPhrase&#125; from &#123;safeUrl&#125;. &#123;body&#125; | line 113, `StreamChatAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Inference/LlamaServerManager.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| No local model is selected for this node. | line 145, `EnsureServerAsync` | Run fault or refusal, shown when something is stopped or declined |
| The model file no longer exists: &#123;ggufPath&#125; | line 150, `EnsureServerAsync` | Run fault or refusal, shown when something is stopped or declined |
| The multimodal projector no longer exists: &#123;declared&#125; | line 155, `EnsureServerAsync` | Run fault or refusal, shown when something is stopped or declined |
| Windows did not start llama-server and gave no reason. | line 387, `StartServer` | Run fault or refusal, shown when something is stopped or declined |
| Could not start llama-server: &#123;ex.Message&#125; | line 391, `StartServer` | Run fault or refusal, shown when something is stopped or declined |
| llama-server exited while loading the model. Recent output:&#123;Environment.NewLine&#125;&#123;instance.GetRecentOutput()&#125; | line 420, `WaitUntilHealthyAsync` | Run fault or refusal, shown when something is stopped or declined |
| Model ready on port &#123;instance.Port&#125; | line 425, `WaitUntilHealthyAsync` | Run fault or refusal, shown when something is stopped or declined |
| llama-server did not become ready within &#123;StartupTimeout.TotalMinutes:0&#125; minutes. See &#123;instance.LogPath&#125; | line 439, `WaitUntilHealthyAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Inference/ModelDescriptor.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| safetensors component unrecognised | line 57, `FormatLabel` | Bound to the interface and read whenever the panel is drawn |
| &#123;SizeGb:0.0&#125; GB | line 77, `SizeLabel` | Bound to the interface and read whenever the panel is drawn |
| size unknown | line 77, `SizeLabel` | Bound to the interface and read whenever the panel is drawn |
| &#123;DisplayName&#125;  (&#123;SizeBytes / 1024d / 1024d / 1024d:0.0&#125; GB, &#123;FormatLabel&#125;) &#123;DisplayName&#125;  (&#123;FormatLabel&#125;) | line 81, `CatalogLabel` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Inference/OpenAiCompatibleClient.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| No base URL is configured for this node. | line 79, `StreamChatAsync` | Run fault or refusal, shown when something is stopped or declined |
| No model is selected for this node. | line 84, `StreamChatAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;(int)response.StatusCode&#125; &#123;response.ReasonPhrase&#125; from &#123;endpoint.ChatCompletionsUrl&#125;. &#123;body&#125; | line 99, `StreamChatAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Inference/PythonRuntimeManager.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| The model folder no longer exists: &#123;model.Path&#125; | line 66, `EnsureServingAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;model.DisplayName&#125; cannot be run. &#123;unavailable&#125; | line 71, `EnsureServingAsync` | Run fault or refusal, shown when something is stopped or declined |
| The Python runtime's interpreter is missing. Repair the runtime from the Local model panel. | line 195, `StartServer` | Run fault or refusal, shown when something is stopped or declined |
| Windows did not start the Python runtime and gave no reason. | line 233, `StartServer` | Run fault or refusal, shown when something is stopped or declined |
| Could not start the Python runtime: &#123;ex.Message&#125; | line 237, `StartServer` | Run fault or refusal, shown when something is stopped or declined |
| The Python runtime exited while loading the model. Recent output:&#123;Environment.NewLine&#125;&#123;instance.GetRecentOutput()&#125; | line 266, `WaitUntilHealthyAsync` | Run fault or refusal, shown when something is stopped or declined |
| Model ready on port &#123;instance.Port&#125; | line 271, `WaitUntilHealthyAsync` | Run fault or refusal, shown when something is stopped or declined |
| The Python runtime did not become ready within &#123;StartupTimeout.TotalMinutes:0&#125; minutes. See &#123;instance.LogPath&#125; | line 285, `WaitUntilHealthyAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Inference/RunCostTracker.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;RunCost.Format(Total)&#125; so far | line 32, `Summary` | Status line, updated as the thing it describes changes |

### `src/LocalNEXUS.App/Services/Inference/RuntimeResolver.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;model.Path&#125; cannot be served. &#123;reason&#125; &#123;model.Path&#125; is not a model this build recognises. | line 35, `Resolve` | Run fault or refusal, shown when something is stopped or declined |
| &#123;model.DisplayName&#125; is &#123;model.FormatLabel&#125;, and no runtime in this build serves that format. | line 48, `Resolve` | Run fault or refusal, shown when something is stopped or declined |
| No local model is selected for this node. | line 63, `ServeAsync` | Run fault or refusal, shown when something is stopped or declined |
| The model no longer exists: &#123;path&#125; | line 68, `ServeAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Mcp/McpAppSurface.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| There is no folder at &#123;path&#125;. | line 63, `OpenProjectAsync` | Run fault or refusal, shown when something is stopped or declined |
| There is no graph or template called '&#123;name&#125;'. Use localnexus_list_graphs for what there is. | line 150, `OpenGraphAsync` | Run fault or refusal, shown when something is stopped or declined |
| The canvas is empty. Open a graph with localnexus_open_graph first. | line 169, `StartRunAsync` | Run fault or refusal, shown when something is stopped or declined |
| The graph cannot be run right now. The run state is &#123;_feed.RunState&#125;, so a run already in progress has to finish first. | line 183, `StartRunAsync` | Run fault or refusal, shown when something is stopped or declined |
| . Check that a graph is open and that the Workspace is the active section. | line 186, `StartRunAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Mcp/McpBridgeServer.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| MCP server on Answering tool calls on &#123;McpBridge.PipeName&#125;. A caller can open a project, open a graph and run it. It cannot write a file except through the graph, and it cannot read a key. | line 62, `Start` | Activity feed entry, written while a run is going on |
| MCP server off | line 76, `Stop` | Activity feed entry, written while a run is going on |
| Tool calls are no longer answered. | line 76, `Stop` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Services/Models/HuggingFaceCatalogue.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Hugging Face could not be reached. Check the connection and try again. | line 189, `FilesAsync` | Run fault or refusal, shown when something is stopped or declined |
| Hugging Face did not recognise that request. &#123;repository&#125; was not found. It may have been renamed or removed. | line 204, `FilesAsync` | Run fault or refusal, shown when something is stopped or declined |
| Hugging Face answered &#123;(int)response.StatusCode&#125;. Try again shortly. | line 211, `FilesAsync` | Run fault or refusal, shown when something is stopped or declined |
| Hugging Face answered with something this could not read. | line 221, `FilesAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Models/ModelDownloader.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| The download could not be started. Check the connection and try again; anything already fetched is kept. | line 158, `FetchAsync` | Run fault or refusal, shown when something is stopped or declined |
| The download was refused with &#123;(int)response.StatusCode&#125;. Try again shortly. | line 172, `FetchAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;file.Path&#125; arrived but does not match the hash the repository published, so it was not kept. That usually means the download was interrupted in a way that went unnoticed. Try again. | line 270, `FinishAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;file.Path&#125; downloaded but could not be moved into the models folder: &#123;ex.Message&#125; | line 285, `FinishAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Persistence/GraphTemplates.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| There is no template called &#123;template.Id&#125;. | line 150, `Apply` | Run fault or refusal, shown when something is stopped or declined |
| &#123;from.Title&#125; has no output called &#123;outputName&#125;. | line 378, `Wire` | Run fault or refusal, shown when something is stopped or declined |
| &#123;to.Title&#125; has no input called &#123;inputName&#125;. | line 381, `Wire` | Run fault or refusal, shown when something is stopped or declined |
| The template cannot wire &#123;from.Title&#125;.&#123;outputName&#125; to &#123;to.Title&#125;.&#123;inputName&#125;: &#123;reason&#125;. | line 386, `Wire` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Persistence/RecentProjectEntry.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Not found | line 77, `IsMissing` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Planning/CodeTask.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Nothing to write. | line 152, `Empty` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/ProjectIndex/ContextBudget.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Context budget &#123;TotalCharacters&#125; characters, roughly &#123;ApproximateTokens&#125; tokens: &#123;MapCharacters&#125; for the project map, &#123;CandidateCharacters&#125; for candidate files, &#123;EmittedSignatureCharacters&#125; for what this run has already written. | line 42, `ApproximateTokens` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/ProjectIndex/ProjectIndexService.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Reading the project. &#123;TypeCount&#125; type(s) across &#123;FileCount&#125; file(s), read in &#123;LastDuration.TotalSeconds:0.0&#125; s. The project has no C# files yet. | line 67, `StatusText` | Bound to the interface and read whenever the panel is drawn |
| No project is open, so nothing is known about what it contains. Not indexed yet. | line 70, `StatusText` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/ProjectIndex/RankedFile.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| reached through the reference graph matches &#123;string.Join(, Keywords)&#125; | line 11, `Reason` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Python/GraphicsMemory.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;BackoffGb:0.#&#125; GB is held back for your own models, a quarter of the card and never less than &#123;MinimumBackoffGb:0.#&#125; GB. | line 40, `BackoffSummary` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Python/PythonProvisioner.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Ready. Safetensors models can be run. Setting up. GGUF models work while this runs. Setup did not finish. Safetensors models cannot be run yet. | line 83, `StatusText` | Bound to the interface and read whenever the panel is drawn |
| Not set up yet. Safetensors models cannot be run until it is. Not checked yet. | line 86, `StatusText` | Bound to the interface and read whenever the panel is drawn |
| Python runtime | line 137, `RunAsync` | Activity feed entry, written while a run is going on |
| Python runtime ready | line 229, `RunAsync` | Activity feed entry, written while a run is going on |
| Safetensors models can be run. &#123;choice.Reason&#125; | line 229, `RunAsync` | Activity feed entry, written while a run is going on |
| Windows did not start &#123;startInfo.FileName&#125; and gave no reason. | line 331, `CreateStartInfo` | Run fault or refusal, shown when something is stopped or declined |
| Reclaimed the Python download cache &#123;before / (1024.0 * 1024):0.#&#125; MB of wheels that are already installed. | line 430, `PruneDownloadCache` | Activity feed entry, written while a run is going on |
| Python runtime unavailable | line 538, `FailAsync` | Activity feed entry, written while a run is going on |
| Python runtime | line 558, `SetStageAsync` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Services/Search/LocalEmbedder.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| The embedding model is not where it was: &#123;_modelPath&#125;. Choose one again in Settings, or turn semantic search off to go back to keyword search. | line 69, `EmbedAsync` | Run fault or refusal, shown when something is stopped or declined |
| The embedding model answered &#123;(int)response.StatusCode&#125;. It may not be an embedding model: a chat model loaded this way refuses every request. | line 87, `EmbedAsync` | Run fault or refusal, shown when something is stopped or declined |
| The embedding model answered without a vector in it. | line 100, `EmbedAsync` | Run fault or refusal, shown when something is stopped or declined |
| The embedding model could not be reached: &#123;ex.Message&#125; | line 114, `EmbedAsync` | Run fault or refusal, shown when something is stopped or declined |
| The embedding model could not be started: &#123;ex.Message&#125; | line 135, `StartAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Search/WebSearchService.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| A search needs something to search for. | line 151, `SearchAsync` | Run fault or refusal, shown when something is stopped or declined |
| There is no search key. Add one in Settings under API keys, Search providers; you can get one at &#123;KeyUrl&#125;. | line 157, `SearchAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;Endpoint&#125;?q=&#123;Uri.EscapeDataString(query.Trim())&#125;&count=&#123;ResultCount&#125; | line 162, `SearchAsync` | Run fault or refusal, shown when something is stopped or declined |
| The search could not be sent: &#123;ex.Message&#125; | line 175, `SearchAsync` | Run fault or refusal, shown when something is stopped or declined |
| The search answered with something that could not be read: &#123;ex.Message&#125; | line 224, `Read` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Spec/SpecModels.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;done&#125; of &#123;Artifacts.Count&#125; done, next is &#123;next.Name&#125; | line 77, `NextReady` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Spec/SpecWorkerClient.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| This session is not a spec contract session, so the spec protocol cannot be spoken over it. | line 50, `AdvanceTimeout` | Run fault or refusal, shown when something is stopped or declined |
| spec/describe | line 55, `DescribeAsync` | Run fault or refusal, shown when something is stopped or declined |
| The worker answered spec/changes without a 'changes' array, so nothing could be read. | line 71, `ListChangesAsync` | Run fault or refusal, shown when something is stopped or declined |
| The worker answered &#123;method&#125; with something that was not an object. | line 111, `ObjectAsync` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Services/Theming/ThemeService.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;CurrentDefinition.DisplayName&#125; is opaque. Pick a theme that can be seen through to set this. | line 126, `IsTransparencyAvailable` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Services/Vision/VisionReader.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;_config.VisionModelId&#125; at &#123;_config.VisionBaseUrl&#125;. Paste or drop an image on the request box. | line 149, `Status` | Status line, updated as the thing it describes changes |
| Nothing configured, so pasting an image says so and does nothing else. | line 151, `Status` | Bound to the interface and read whenever the panel is drawn |
| That image is empty. | line 187, `ReadAsync` | Run fault or refusal, shown when something is stopped or declined |
| That image is &#123;image.Length / (1024 * 1024)&#125; MB, and the limit is &#123;MaximumBytes / (1024 * 1024)&#125; MB. | line 193, `ReadAsync` | Run fault or refusal, shown when something is stopped or declined |
| The vision model could not be reached: &#123;ex.Message&#125; | line 246, `ReadAsync` | Run fault or refusal, shown when something is stopped or declined |
| The vision model answered with nothing. | line 264, `ReadAsync` | Run fault or refusal, shown when something is stopped or declined |
| The vision model could not be started: &#123;ex.Message&#125; | line 304, `ResolveAsync` | Run fault or refusal, shown when something is stopped or declined |
| The vision model answered with something that could not be read: &#123;ex.Message&#125; | line 350, `ReadReply` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.Installer/Services/AssetDownloader.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Could not reach the download for &#123;asset.Label&#125;. Check that this machine is online and that a proxy or firewall is not blocking github.com. The address was &#123;asset.Url&#125; | line 72, `DownloadAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;asset.Label&#125; is no longer at the address this installer was built with, which usually means this installer is an old one and the release has moved. Take a newer installer, or install LocalNEXUS on its own and place the binaries by hand. Each folder under the vendor directory | line 86, `DownloadAsync` | Run fault or refusal, shown when something is stopped or declined |
| has a README naming what to download. The address was &#123;asset.Url&#125; | line 89, `DownloadAsync` | Run fault or refusal, shown when something is stopped or declined |
| The server answered &#123;(int)response.StatusCode&#125; &#123;response.ReasonPhrase&#125; for &#123;asset.Label&#125;. | line 95, `DownloadAsync` | Run fault or refusal, shown when something is stopped or declined |
| &#123;asset.Label&#125; downloaded but did not match its checksum, which means it arrived damaged or was altered in transit. Nothing was installed from it. Trying again is usually enough. | line 141, `DownloadAsync` | Run fault or refusal, shown when something is stopped or declined |
| The disk filled up while downloading &#123;asset.Label&#125;. Free some space and try again. It needs &#123;asset.SizeText&#125; to download and about twice that once unpacked. | line 153, `DownloadAsync` | Run fault or refusal, shown when something is stopped or declined |
| The connection dropped while downloading &#123;asset.Label&#125;. Trying again resumes from the start. | line 165, `DownloadAsync` | Run fault or refusal, shown when something is stopped or declined |
| There is not enough room on &#123;root&#125; for &#123;label&#125;. It needs about &#123;(required + 524_288L) / 1_048_576L&#125; MB and there is &#123;(drive.AvailableFreeSpace + 524_288L) / 1_048_576L&#125; MB free. | line 201, `EnsureRoom` | Run fault or refusal, shown when something is stopped or declined |
| Free some space and try again, or go back and untick a component. | line 204, `EnsureRoom` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.Installer/Services/SetupRunner.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| This installer was built without the application inside it, so there is nothing to install. That is a packaging fault rather than anything you did. Take a release build from the project's releases page. | line 165, `WriteApplication` | Run fault or refusal, shown when something is stopped or declined |
| The application payload could not be read out of this installer. | line 173, `WriteApplication` | Run fault or refusal, shown when something is stopped or declined |
| LocalNEXUS is running, so it could not be replaced. Close it and try again. | line 194, `WriteApplication` | Run fault or refusal, shown when something is stopped or declined |
| The application could not be written to &#123;InstallLocations.InstallRoot&#125;: &#123;ex.Message&#125; | line 203, `WriteApplication` | Run fault or refusal, shown when something is stopped or declined |
| &#123;label&#125; contains an entry that would be written outside its own folder, so it was refused. Nothing from it has been installed. | line 248, `Extract` | Run fault or refusal, shown when something is stopped or declined |
| &#123;label&#125; passed its checksum but could not be opened as an archive, which should not happen. Trying again is worth one attempt. | line 259, `Extract` | Run fault or refusal, shown when something is stopped or declined |
| The disk filled up while unpacking &#123;label&#125;. Free some space and try again. | line 269, `Extract` | Run fault or refusal, shown when something is stopped or declined |
| &#123;label&#125; could not be unpacked: &#123;ex.Message&#125; | line 277, `Extract` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.Installer/Services/ShortcutWriter.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| The Windows Script Host is not available on this machine, so the shortcut could not be created. The application is installed and can be started from &#123;targetPath&#125; | line 23, `Write` | Run fault or refusal, shown when something is stopped or declined |
| The shortcut at &#123;shortcutPath&#125; could not be created: &#123;ex.Message&#125; The application itself is installed and works. | line 64, `Write` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.Installer/Services/UninstallRegistrar.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| The install could not be recorded in Add or remove programs. Everything else was installed and works; removing it later means deleting &#123;InstallLocations.InstallRoot&#125; by hand. | line 50, `Register` | Run fault or refusal, shown when something is stopped or declined |

## Application


### `src/LocalNEXUS.App/App.xaml.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| Settings were not loaded | line 418, `Compose` | Activity feed entry, written while a run is going on |
| Project index &#123;index.StatusText&#125; &#123;index.ReparsedCount&#125; file(s) had to be read again; the rest came from the cache. | line 523, `IndexProjectAsync` | Activity feed entry, written while a run is going on |
| Project index unavailable | line 533, `IndexProjectAsync` | Activity feed entry, written while a run is going on |
| Set up the Python runtime? Safetensors models are served through Python, which has to be built once. That is roughly 3 GB on an NVIDIA machine because it pulls a CUDA build of torch, and a | line 595, `HasConsentedToPythonAsync` | Modal dialog, shown over whatever is on screen |
| few hundred megabytes otherwise. It runs in the background and nothing waits on GGUF models never use it, so choose No if those are all you run. You can set it | line 598, `HasConsentedToPythonAsync` | Modal dialog, shown over whatever is on screen |
| Python runtime | line 607, `HasConsentedToPythonAsync` | Activity feed entry, written while a run is going on |
| Python runtime declined Building in the background. The Local model panel shows how far it has got. | line 607, `HasConsentedToPythonAsync` | Activity feed entry, written while a run is going on |
| Not building it. Safetensors models will refuse until it is set up from the Local model panel. GGUF models are unaffected. | line 610, `HasConsentedToPythonAsync` | Activity feed entry, written while a run is going on |
| Local inference unavailable &#123;AppPaths.LlamaServerExecutableName&#125; was not found. Place a llama.cpp build in vendor\\llama to run local models. OpenRouter nodes work without it. | line 647, `ReportEnvironment` | Activity feed entry, written while a run is going on |
| Local inference ready | line 652, `ReportEnvironment` | Activity feed entry, written while a run is going on |
| Model catalog No models found. Drop one into &#123;AppPaths.Models&#125;, add a folder from a model node, or list a folder in &#123;AppPaths.ModelPathsFile&#125;. | line 656, `ReportEnvironment` | Activity feed entry, written while a run is going on |
| &#123;catalog.Models.Count&#125; model(s) available. | line 659, `ReportEnvironment` | Activity feed entry, written while a run is going on |
| Script mode ready | line 667, `ReportEnvironment` | Activity feed entry, written while a run is going on |
| Script mode unavailable A Reshape node can run a C# expression for anything its four presets do not cover. | line 667, `ReportEnvironment` | Activity feed entry, written while a run is going on |
| The script compiler cannot be built into a single file executable, so a Reshape node has its four other modes and not this one. Nothing else is affected. | line 670, `ReportEnvironment` | Activity feed entry, written while a run is going on |
| Bundled font loaded | line 696, `ReportBundledFont` | Activity feed entry, written while a run is going on |
| Bundled font unavailable &#123;Expected&#125; is rendering paths, identifiers and diagnostics. | line 696, `ReportBundledFont` | Activity feed entry, written while a run is going on |
| &#123;Expected&#125; did not resolve from this build, so the monospace fallback is being used instead. | line 699, `ReportBundledFont` | Activity feed entry, written while a run is going on |
| Bundled font unavailable | line 703, `ReportBundledFont` | Activity feed entry, written while a run is going on |
| Cleaned up after a previous session &#123;stopped&#125; engine process(es) were still running from a session that did not close properly. They were stopped so this one starts from a clean machine. | line 718, `ReportAbandonedProcesses` | Activity feed entry, written while a run is going on |
| Process cleanup is degraded Windows refused a job object, so engine processes are stopped explicitly but would survive this application being killed outright. | line 725, `ReportAbandonedProcesses` | Activity feed entry, written while a run is going on |

### `src/LocalNEXUS.App/Infrastructure/ActivityEvent.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| +&#123;DiffCounts.Groups[ | line 117, `AddedText` | Bound to the interface and read whenever the panel is drawn |
| -&#123;DiffCounts.Groups[ | line 120, `RemovedText` | Bound to the interface and read whenever the panel is drawn |
| ^\+(?<added>\d+) -(?<removed>\d+)$ | line 126, `DiffCountPattern` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Infrastructure/Converters/BooleanToVisibilityConverter.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| BooleanToVisibilityConverter is a one way converter. | line 27, `ConvertBack` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Infrastructure/Converters/BrushKeyConverter.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| BrushKeyConverter is a one way converter. | line 35, `ConvertBack` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Infrastructure/Converters/CountToVisibilityConverter.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| CountToVisibilityConverter is a one way converter. | line 29, `ConvertBack` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Infrastructure/Converters/EnumToVisibilityConverter.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| EnumToVisibilityConverter is a one way converter. | line 40, `ConvertBack` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Infrastructure/Converters/NullToVisibilityConverter.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| NullToVisibilityConverter is a one way converter. | line 21, `ConvertBack` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Infrastructure/Converters/ResourceLookupConverter.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| ResourceLookupConverter is a one way converter. | line 44, `ConvertBack` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Infrastructure/Converters/SettingsSectionNameConverter.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| SettingsSectionNameConverter is a one way converter. | line 57, `ConvertBack` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Infrastructure/Converters/StringToVisibilityConverter.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| StringToVisibilityConverter is a one way converter. | line 22, `ConvertBack` | Run fault or refusal, shown when something is stopped or declined |

### `src/LocalNEXUS.App/Models/Extensions/ExtensionManifest.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;Tools.Count&#125; tools | line 57, `ProvidesTab` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.App/Models/Extensions/InstalledExtension.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| not installed | line 66, `StateText` | Bound to the interface and read whenever the panel is drawn |

### `src/LocalNEXUS.Installer/Models/EngineAsset.cs`

| Text | Where | When it shows |
| --- | --- | --- |
| &#123;(Bytes + 524_288L) / 1_048_576L&#125; MB &#123;(Bytes + 512L) / 1024L&#125; KB | line 27, `SizeText` | Bound to the interface and read whenever the panel is drawn |
