# Security

## Reporting

Do not open a public issue.

[Open a security advisory](https://github.com/You-Know-Its-Me-Studios/LocalNEXUS/security/advisories/new).
That gives a private thread and, if it is real, a way to publish an advisory and credit you.
If advisories are not available to you, email youknowitsmestudios@gmail.com with "LocalNEXUS
security" in the subject.

Include enough to reproduce it: the version, what an attacker has to control, and what they
get. Proof of concept code is welcome.

One maintainer, so set expectations accordingly. Acknowledgement within a week. Valid reports
ship a fix in the next release with the advisory. Disagree with an assessment and say so, it
will not be held against you.

Only the latest release is supported. Pre-1.0, no maintenance branches.

## What this app does, before you decide what counts as a vulnerability

None of the following is a bug. It is what the tool is.

**It runs code a language model wrote.** Files land in your project and Unity compiles and
runs them. The compile check verifies that code builds, not that it is safe. Review what gets
written, especially if the model came from somewhere you do not control.

**A Patch node executes arbitrary C#.** In script mode it compiles an expression from the
graph with Roslyn and runs it in this process with this process's privileges. Roslyn scripting
is not a sandbox and was never meant to be one.

**So a graph file is executable content, not data.** Opening a `.nexusgraph.json` someone sent
you is running a program they sent you. There is no warning in the interface about this, which
is itself worth fixing.

**LocalNEXUS can answer to other tools, and it is off until you switch it on.** With
"Answer MCP tool calls" enabled in Settings, `LocalNEXUS.Mcp.exe` beside the application speaks
the Model Context Protocol over stdin and stdout and relays each call to the running window over
a local named pipe.

What a caller can then cause is the whole of the paragraph above this one. It can open any folder
on your account as the project, open any saved graph or shipped template, and run it. A run writes
files into the open project, spends whatever a cloud model on that graph costs, and, if the graph
has a Reshape node in script mode, compiles and runs C# from that graph file in this process. That
is the same thing a person pressing Run causes, deliberately: the tool call goes through the same
command the button does. The difference is who pressed it.

It is stdio and a named pipe, never a socket and never a port, so switching it on is not a network
exposure. The pipe is created for the current user only and is named for the account, so another
account on the same machine cannot reach it. What it is is a second way in, for anything on your
account that can start a process, which is why it is off by default and why turning it on is a
choice rather than a default.

The tool surface is a fixed list of seven and cannot be extended at runtime. There is no tool that
writes a file: the graph's Output node writes, inside the project folder and through the same write
rules, and that is the only path. There is no tool that reads a credential, and the interface the
tools reach the application through has no method that could.

**Installing an extension is running a program somebody else wrote.** An extension is a
process started with your privileges. It is out of process, so it cannot corrupt this
application's memory or crash it, and it is held in a job object so it cannot outlive it. That
is isolation from failure, not from intent: nothing sandboxes what it does to your machine. The
manifest declares what it contributes and you see that before installing, which is the point at
which to decide whether you trust it.

**API keys are encrypted, and are not in your graphs.** They live in
`%LOCALAPPDATA%\LocalNEXUS\credentials.dat`, encrypted with DPAPI for the signed in Windows
account, keyed by provider. A saved graph names the provider and never carries the key, so a
graph can be shared or committed safely. This replaces the previous arrangement, where keys sat
in plain text in `config.json` and in every saved graph that used one.

What that does not defend against is anything already running as you, which can call DPAPI too.
It defends against the realistic case, which is a file being copied, synced, backed up or
committed.

**It starts child processes and opens ports.** `llama-server` binds `127.0.0.1` on a per model
port, so it is not reachable from the network. It runs with permissive CORS and no API key,
which is only safe because of that binding. The mesh node listens on a configurable port,
9337 by default, LAN scoped unless you publish.

**The distributed inference pipeline is the one part meant to be reachable from another
machine**, so it is the part with authentication. It is a Python package the app starts as a
child process, and it is off by default.

- A *host* is started by the app and binds `127.0.0.1`. It answers the same OpenAI-compatible
  API as every other local engine.
- A *peer*, on a machine contributing layers, is started from a command line and binds
  `127.0.0.1` unless told otherwise. Port 8749 by default.
- **Binding an address the network can reach requires a shared secret.** The peer refuses to
  start otherwise, and that is not configurable.
- Every connection, in both directions, completes a challenge-response handshake before a
  single frame is dispatched. The secret is never sent; what crosses the wire is an HMAC over a
  nonce the accepting end chose, so a captured handshake cannot be replayed. The secret is read
  from `LOCALNEXUS_DISTRIBUTED_SECRET` in preference to a command line argument, because an
  argument is visible in the process list to every account on the machine.
- A model directory named by a remote host is checked to be an existing local folder before
  anything is loaded, and loading is pinned to local files. A peer will not fetch a model.
- Caps bound what a caller can ask for: connections and in-flight requests per stage, a minimum
  interval between assignments, and prompt, generation and concurrency limits on the host.

What this does **not** do: the secret is a single shared value, not per-peer credentials, and
there is no transport encryption. Activations cross the network in the clear. Treat a
distributed pipeline as something to run on a network you control, not across the internet.

**Engine processes are owned through Windows job objects**, so the OS kills them when the
app's handle closes. The app never terminates a process it did not start.

**It writes to your filesystem.** The Output node resolves paths through the project service,
which refuses anything landing outside the opened project. Escaping that is a vulnerability.

## In scope

- Escaping the project folder when writing files.
- An extension reaching anything the host did not hand it, or surviving the job object.
- Reaching the local inference server or mesh node from outside the machine when it should not
  be reachable.
- Anything making it more dangerous to open a graph, project or model file than described
  above.
- Recovering a stored key without the signed in user's credentials, or a key reaching a graph
  file, a log, an error message or the activity feed.
- Privilege escalation, or an engine process outliving the app in a way the job object was
  supposed to prevent.

## Not in scope

- A model producing bad or malicious code. That is what the compile check and your review are
  for.
- Anything requiring an attacker to already have code execution on your machine.
- An extension you installed doing what its manifest said it would. That is the feature.
- Vulnerabilities in llama.cpp, Mesh LLM, uv, transformers, torch or any extension. Report upstream. Tell us
  anyway if we ship an affected version.
- Peers on a mesh you joined behaving badly. A private mesh is joined by invitation, so every
  peer was let in deliberately. Trust scoring does not exist yet, on purpose.
