# Neon Companion

Neon Companion is a streamlined agentic TUI for local LLMs, built on .NET 10. It attempts to combine many of my favorite features from tools like Claude Code, Hermes Agent, and Cline with a local-first workflow and native toolsets.

Current State: A foundational shell for tool development (Windows-first).
On the Roadmap: Full terminal execution, coding capabilities, and official macOS/Linux support.

## Contents

- [License](#license)
- [Features](#features)
- [Components & Libraries](#components--libraries)
- [Settings & menus](#settings--menus)
- [Slash commands](#slash-commands)
- [Tools](#tools-2)
- [Screenshots](#screenshots)
- [Why "Neon"](#why-neon)

## License
[↑ Back to top](#neon-companion)

Neon Companion is released under the GPLv3 license.

## Features
[↑ Back to top](#neon-companion)

### Core Architecture & UI
* Built on **.NET 10 NativeAOT** for lightweight, high-performance execution.
* **Rich Terminal UI (TUI):** Powered by Spectre.Console, featuring robust support for menus, mouse input, Markdown, and images.
* **Vision & Media Input:** Full vision model support with seamless drag-and-drop and clipboard pasting for images directly into the terminal.
* **Quality of Life:** Intuitive auto-complete for commands, files, folders, skills, and tools.

### AI Connectivity & Context Management
* **Local AI Auto-Discovery:** Automatically detects and connects to most OpenAI-compatible servers on your local network (LM Studio, vLLM, SGLang, Ollama, Unsloth, etc.), while allowing full manual configuration for custom endpoints.
* **Smart Context Handling:** Configurable automatic context compaction to optimize token usage and prevent window overflow.
* **Prompt Transparency:** Visually inspect exactly what is being fed into the system prompt—no black boxes.
* **Persistent Memory:** A UI-editable memory system that automatically injects essential, recurring details directly into context.
* **Message Queue:** Built-in queue for stacking and executing sequential messages.

### Profiles, Sessions & Skills
* **Multi-Profile Support:** Switch between distinct configurations, each featuring its own configured working directory, fully independent settings and isolated session logging.
* **Advanced Session Management:** Easily manage, resume, and reflect on past sessions.
* **Hierarchical Skills System:** Define and manage agent skills at the global, profile, project, or machine (`.agents\skills`) level.
* **Self-Learning:** An automatic self-reflection system that dynamically updates and creates new skills based on interactions and tool outcomes.

### Built-In Tooling & Voice
* **Essential Tools:** Sandboxed file I/O, web search (DuckDuckGo/SearXNG), web browsing (httpClient/Chromium), graphical clarification prompts, and clock/timer functions.
* **Local Git Management:** Built-in capabilities to seamlessly inspect, manage, and interact with local Git repositories.
* **Shell Commands & Scripts:** `run_command` runs a command line in PowerShell, cmd or Git Bash from the working directory (foreground, or in the background with a `process` tool to poll, feed and kill it), and `execute_code` runs a Python, Node or PowerShell script that can call the other tools — all behind an approval pane (allow once, for the session, or for good) and a `yolo` mode for trusted setups.
* **MCP Server Support:** Seamless integration with Model Context Protocol (MCP) servers to expand tool capabilities and connect to external data sources.
* **Native Voice Stack:** Features in-process Whisper STT, push-to-talk, and Vosk wake-word integration.
* **Text-to-Speech:** Includes in-process Kokoro TTS, with support for an external HTTP Kokoro endpoint.

## Components & Libraries
[↑ Back to top](#neon-companion)

* `.NET 10 (NativeAOT)`
* `Spectre.Console`
* `Microsoft.Extensions.AI`
* `Microsoft.Extensions.AI.OpenAI`
* `Microsoft.Data.Sqlite`
* `Microsoft.ML.OnnxRuntime`
* `KokoroSharp`
* `Whisper.net`
* `Silero VAD`
* `Vosk`
* `PhotoSauce.MagicScaler`
* `Markdig`
* `LibGit2Sharp`

## Settings & menus
[↑ Back to top](#neon-companion)

Every setting lives in a profile and is edited from a pane inside the app — `←`/`→` switch tabs, `↑`/`↓` move, Enter edits or flips a row, ESC backs out. Five panes carry them: `/settings` for the app, the sessions, the LLM and the voice stack; `/skills` for the agent skills and the self-reflection; `/tools` for what the model may call; `/mcp` for external MCP servers; and `/sysprompt`, a read-only view of what the model is about to be sent.

### Settings (`/settings`)

#### General

| Setting | What it does | Default |
|---|---|---|
| Profile | Switches to another profile (each has its own settings, persona, memory, skills and sessions). | `default` |
| New profile mode | What `/profile add` copies from the current profile: `basic` copies the settings and memories; `advanced` also copies the persona, operating-rules and voice-directive files. | `basic` |
| Working directory | The folder the file and git tools work under; empty means the profile's own `files\` folder. | profile's `files\` |
| Queue messages | A message sent while a reply is streaming is queued and sent when the reply ends, instead of waiting on the input row. | on |
| Queue cancel mode | What a cancelled reply does with the queue: `hold` keeps it until your next message, `drain` sends the next queued message at once, `empty` drops them all. | `empty` |
| Memory | Offers the model `save_memory` / `recall_memory` and opens every conversation with what it remembers. | on |
| Copy user prompt | `/copy` includes your prompt above the reply; off copies the reply alone. | on |
| Mouse in menus | The app keeps the mouse under a menu: a double-click picks a row, one off the pane closes it, one on the hint row opens `/settings`. Off hands the mouse to the terminal there. | on |
| Show image thumbnails | Draws a small colour block of each picture you send under your line. | on |
| Image thumbnail size | The block's size: `small` (48×12), `medium` (64×16), `large` (80×20) or `xlarge` (96×24) columns × rows. | `small` |
| Transcript markdown | Renders replies as styled Markdown (bold, lists, code fences, tables) instead of plain streamed text. | on |
| Paste preview lines | How many lines of a long paste the transcript shows in dim under its `[Pasted text #n]` placeholder (0–200; 0 = the placeholder alone). | 25 |
| Hide /exit autocomplete | Leaves `/exit` out of the `/` completion list so a pick never closes the app by mistake; typed in full it still exits. | on |
| Command typo intercept | A line that is exactly a command's name without its slash (`clear`) asks *Did you mean /clear?* before sending it as text. | on |
| Welcome splash | Shows one of the splash pictures under the banner at startup until the first line is sent (`←`/`→` walk the set; a profile's own `splash\` folder replaces the built-in pictures). | on |
| Show working directory | Prints the working directory at the right edge of the banner's title line. | on |
| Draft editor | The command `/draft` opens its temporary file with (`code --wait`, `notepad`…); empty uses whatever Windows opens `.txt` files with. | (default .txt editor) |

#### Sessions

| Setting | What it does | Default |
|---|---|---|
| Session logging | Writes every completed turn to the profile's `sessions.db`, so `/session` can list, search and restore it. | on |
| Session retention (days) | Sessions whose last turn is older than this are purged at startup (0–3650; 0 = keep forever). | 0 |
| Session naming mode | How a session gets its title: `model-written` asks the model for a short slug after the first turn; `first-line` uses the first line you sent. | `model-written` |
| Session show name | Which titles show on the rule above the input row: `all-names`, `model-written` (a model-written or typed name only) or `none`. | `all-names` |
| Session tool | Offers the model `session_manager` to search, list and read this profile's earlier sessions (never restore or purge). | on |
| Session search max results | How many sessions a `session_manager` search or list returns (1–20). | 10 |

#### LLM

| Setting | What it does | Default |
|---|---|---|
| LLM scan mode | Where a blank URL looks for a server: `local` (the usual ports on this machine), `remote` (the same ports across the local network), `both`, or `disabled` (no scan; set the URL by hand). | `local` |
| LLM URL | The OpenAI-compatible base URL (`http://127.0.0.1:1234/v1`); empty scans per the mode above. `/server` fills it in. | (scan) |
| LLM model | The model id; empty takes the first the server lists. `/model` picks one. | (first listed) |
| LLM API key | The bearer token; `empty` for keyless local servers. | `empty` |
| LLM reasoning | The reasoning effort sent with every request: `none` (thinking off), `low`, `medium`, `high` or `xhigh`. `/reasoning` opens the same list. | `none` |
| LLM request timeout (s) | The most one HTTP request may take (up to 3600). | 3600 |
| LLM turn timeout (s) | The most one whole turn — every tool round trip included — may take (up to 21600). | 21600 |
| LLM context length | The model's context window in tokens, for the usage percentage; 0 takes the server's own figure. | 0 (server) |
| LLM compact type | What `/compact` does: `summary` folds the older turns into one model-written summary; `prune` stubs their bulky tool results and keeps every turn. | `summary` |
| LLM compact keep recent | How many recent user turns a compact keeps word for word (0–24). | 2 |
| LLM compact show summary | After a compact, shows what it did under the notice: the summary's text as dim lines, or one line per pruned tool result (tool and size), then how many messages were protected at the start (the opening call pairs) and at the end (the recent turns kept). | off |
| LLM auto compact (%) | The share of the context window at which the next message compacts first (1–100; 0 = off). | 85 |
| LLM offer tools | Whether the model gets any tools at all. Off makes every turn tool-free, for chat templates with no tool role; flipping it starts a new conversation. | on |
| LLM tool compact type | What happens when a single turn's tool calls approach the window: `prune` stubs this turn's older results and carries on, `stop` ends the turn with a notice, `nothing`. | `prune` |
| LLM max tool iterations | How many tool round trips one message may make before the turn stops (1–10000). | 10000 |
| LLM use fun verbs | The thinking spinner reads a random verb instead of `thinking` / `writing`. | off |

#### TTS

| Setting | What it does | Default |
|---|---|---|
| TTS output | Reads replies aloud (`/tts`). | off |
| TTS source | `in-process` runs Kokoro in this process over ONNX Runtime (the model downloads on first use); `http` uses a Kokoro-FastAPI server. | `in-process` |
| TTS HTTP URL | The Kokoro-FastAPI base URL, read while the source is `http`. | `http://localhost:8880/v1` |
| TTS voice preview | The voice pickers speak the highlighted voice as you move through them. | on |
| TTS voice | The Kokoro voice. | `af_heart` |
| TTS voice 2 | A second voice blended in; `(none)` for the primary voice alone. | `am_eric` |
| TTS voice mix | The primary voice's share of the blend, 0–100 %. | 80 |
| TTS speed | The speech rate multiplier, 0.5–2.0. | 1.2 |

#### STT

| Setting | What it does | Default |
|---|---|---|
| STT input | Turns the microphone on: the push-to-talk key records a spoken message (`/stt`). | off |
| STT wake | Saying the wake phrase at the idle line starts a listen without a key (`/wake`). | off |
| STT wake phrase | One to three words; also the interrupt phrase. | `hey neon` |
| STT interrupt | Saying the wake phrase during a spoken reply cuts it short and listens (`/interrupt`). | off |
| STT interrupt echo guard | How close the assistant's own just-spoken text must be to the wake phrase to be ignored as an echo, 50–100 % (100 = the exact phrase only). | 100 |
| STT interrupt confirm | How long the phrase must persist in the recogniser's interim results before it counts, 0–2000 ms. | 200 |
| STT push-to-talk key | The key that records: `F1`–`F10`, `Insert`, `Home`, `End`, `PageUp` or `PageDown`. | `F4` |
| STT whisper model | The Whisper model that transcribes: `ggml-tiny.en.bin`, `ggml-base.en.bin` or `ggml-small.en.bin` (downloaded on first use). | `ggml-base.en.bin` |
| STT vosk model | The Vosk model the wake word and interrupt listen with: `vosk-model-small-en-us-0.15`, `vosk-model-en-us-0.22-lgraph` or `vosk-model-small-en-in-0.4`. | `vosk-model-small-en-us-0.15` |

### Skills (`/skills`)

#### Offered

The loaded skills, one row each with its scope (`profile`, `global` or `external`) and description, then any shadowed duplicates and any folders that were skipped and why. Enter on a skill opens its scope page: move it between the profile and global roots, rename it (what you type is forced to a skill name — lower case, hyphens between the words — and a name another skill already has is refused), or delete it when *Allow skill delete* is on.

#### Options

| Setting | What it does | Default |
|---|---|---|
| Agent skills | Lists the skills in the prompt and offers `load_skill` and `skill_editor`; off also stops reading the project file. | on |
| Use external skills (.agents\skills) | Also reads `%USERPROFILE%\.agents\skills`, read-only. | off |
| Skill compact mode | `protected` keeps a loaded skill's instructions through a prune; `unprotected` prunes them like any tool result. | `protected` |
| #-mention enabled | `#` and part of a name on the input line lists the loaded skills; a pick writes `#name` as text. | on |
| Allow skill delete | The scope page offers `delete` (after a confirmation) as well as the move. | off |

#### Reflection

| Setting | What it does | Default |
|---|---|---|
| Reflection (auto-learn) | After enough tool calls, or a tool error the model recovered from, a background reflection writes or improves a skill. | on |
| Reflection reasoning | The reasoning effort of the reflection alone: `none`, `low`, `medium`, `high`, `xhigh`, or `profile` for the profile's own level. | `none` |
| Reflection window | How many of the last turns a reflection reads (1–5; the last in full, the earlier ones trimmed). | 3 |
| Reflection min tool calls | How many of the model's own tool calls, added up since the last reflection, make a task worth a skill (3–20). | 4 |
| Reflection max requests | How many model requests one reflection may spend before it gives up (1–20). | 4 |
| Reflection cooldown (minutes) | How long after a skill was written an automatic reflection waits (0–1440; 0 = off). | 5 |
| Reflection cooldown mode | `last-written-skill` makes only a turn that used the skill just written wait; `all-skills` makes every automatic reflection wait. | `last-written-skill` |
| Reflection includes sessions | The reflection opens with the earlier sessions that match the turn, and can search them. | on |

#### Project

One row, **Project file**: whether `NEON.md` (or `AGENTS.md`) in the working directory is read into the prompt as project notes. The row shows which file is found and its size. Default on.

### Tools (`/tools`)

#### Offered

Every tool the app has, grouped (Clock, Timers, Files, Git, Shell, Web, Memory, Skills, Sessions, Questions) with the description the model reads. Enter or Space flips a single tool on or off; a group whose switch is off is shown dim. `delete`, `git_discard` and `git_delete` — the tools that lose work — and `zip` / `unzip` — the bulk pack and extract — start off (a profile saved earlier keeps its own list).

#### Options

| Setting | What it does | Default |
|---|---|---|
| $-mention enabled | `$` and part of a name on the input line lists the tools the next turn offers; a pick writes `$name` as text. | on |

#### Ask

| Setting | What it does | Default |
|---|---|---|
| Ask user | Offers `ask_user`, which puts multiple-choice questions on the pane. | on |
| Ask max questions | How many questions one call may put (1–10). | 10 |
| Ask max choices per question | How many options one question may offer (2–15). | 10 |

#### Files

| Setting | What it does | Default |
|---|---|---|
| File tools | Offers the sandboxed file tools (read, write, patch, search, move, copy, zip, view_image…) under the working directory. | on |
| File safe edits | Every edit keeps the previous version in `.trash` first and `delete` moves there, with `restore` as the undo; off writes in place and `delete` removes for good. | off |
| File /tree max length | How many entries `/tree` prints before it stops (1–10000). | 500 |
| File /tree show sizes | `/tree` carries each file's size. | on |
| File @-mention folder mode | Picking a folder from the `@` list: `folder-remain` keeps the list open inside it; `folder-apply` writes `@folder/` and closes. | `folder-remain` |
| File view image max (per call) | How many pictures one `view_image` call may load (1–100). | 10 |

#### Git

| Setting | What it does | Default |
|---|---|---|
| Git tools | Offers the git tools (status, log, show, diff, blame, branch, stage, commit, stash, discard, delete) over the repository in the working directory — in-process, no `git.exe`. | on |
| Git diff max lines | Where a `git_diff` patch is cut (20–5000). | 500 |
| Git log max commits | How many commits `git_log` returns unless the call says otherwise (1–200). | 20 |
| Git email | The `user.email` that `/git user` writes into the working directory's repository config. Never read by the git tools. | (not set) |
| Git name | The `user.name` that `/git user` writes beside it. | (not set) |

#### Shell

| Setting | What it does | Default |
|---|---|---|
| Shell command policy | What stands between `run_command` and the shell: `off` (no shell tool is offered — the group's switch), `ask` (a command whose prefixes are not all allowed is put to you on the pane first: Deny, Allow once, Allow the prefixes for this session, or Allow them always; with no pane to ask on it is refused), `yolo` (everything runs, nothing is asked). `NEONCOMPANION_COMMAND_POLICY` outranks it, so a scripted `--headless` run can say `yolo`. | `ask` |
| Shell allowed commands | The prefixes allowed for good — `git status`, `dotnet build`, `python` (the program, plus its subcommand for git, dotnet, npm, pip, gh, docker, cargo, go, winget and the like). Enter on one removes it; the pane's *Allow … always* adds one. | none |
| Shell default | The shell a `run_command` without `shell` runs in: `powershell` (pwsh when installed, else Windows PowerShell 5.1), `cmd`, or `bash` (Git Bash, when found). | `powershell` |
| Shell timeout (s) | How long a foreground command without `timeout` may run before it is killed (1–3600). | 180 |
| Shell foreground cap (s) | The most a foreground command may wait, whatever its `timeout` says (10–3600). | 600 |
| Shell output max chars | The most output one result carries back (2000–500000); over it the head and tail are kept and the whole text goes to `.shell\<id>.log` under the working directory, where `read_file` reaches it. | 30000 |
| Shell code languages | The languages `execute_code` may run — `powershell`, `python`, `node`; one or more, and a language is offered only while its interpreter is found. Enter or Space flips one; the last one on stays. | all three |
| Shell code timeout (s) | How long an `execute_code` script without `timeout` may run before it is killed (1–3600). | 300 |
| Shell tool bridge | Whether an `execute_code` script may call the app's other tools through its `neon_tools` module (a loopback socket with a per-run token). Off: no module is written, the script's environment carries no bridge, and neither the tool's description nor the operating rules mention calling tools — the script does everything itself. | off |
| Shell code max tool calls | How many tool calls one script may make through its bridge (1–500), while `Shell tool bridge` is on. | 50 |

#### Web

| Setting | What it does | Default |
|---|---|---|
| Web tools | Offers `web_search`, `web_fetch`, `open_url` and `download_file`. | on |
| Web browser mode | `default` fetches with the HTTP client and falls back to a headless browser when a page is blocked or empty; `httpclient` never falls back; `chromium` uses the browser for every page. | `default` |
| Web browser path | The Chromium executable for the headless leg; empty finds Edge, Chrome or Brave in their standard folders. | (auto) |
| Web browser network mode | Where a fetch may reach: `internet` (public addresses only), `local_area_network` (this machine and the LAN only) or `both`. | `internet` |
| Web search method | `duckduckgo` (built in, no setup) or `searxng` (the instance below). | `duckduckgo` |
| Web SearXNG URL | A SearXNG instance's base URL, used while the method is `searxng`. | (not set) |
| Web search max results | How many hits a search returns (1–20). | 20 |

### MCP servers (`/mcp`)

#### Servers

One row per server named in `mcp.json` (the profile's, then the home's; the profile wins a name) with its state — `connected · N tools`, `connecting`, `failed: …`, `off` — and its transport. Enter or Space switches a server on or off and connects or disconnects it at once; Enter on a failed server retries. Below them: `edit profile mcp.json`, `edit global mcp.json` and `reload`, then any entries that were skipped and why.

#### Tools

Every connected server's tools as `<server>__<tool>` with the description the server gives; Enter or Space flips one on or off.

#### Options

| Setting | What it does | Default |
|---|---|---|
| MCP servers | The master switch: on, every enabled server is started at launch and after a profile switch and its tools are offered; off, nothing is started. | off |
| MCP connect timeout (s) | How long one server gets to answer the handshake and list its tools before it is marked failed (5–300). | 30 |

### System prompt (`/sysprompt`)

Read-only: exactly what the next reply will be sent, nothing paraphrased.

#### Prompt

The system prompt section by section, each with its status — **Persona** (default or `persona.md`), **Operating rules** (default or `operata.md`), **Reply format** (Markdown or plain text, and why), **Project notes** (`NEON.md` / `AGENTS.md`), **Memory**, **Skills** (the catalog), **Git tools**, **MCP servers**, and **Voice directive** (default or `vocalia.md`, only on a spoken turn, always last). Under *Also sent, outside the system prompt*: the opening clock, working-directory and memory calls seeded with the first message, and the reasoning fields on the request.

#### Tools

Every tool the reply may call, grouped — Clock, Timers, Files, Git, Web, Memory, Skills, Sessions, one group per connected MCP server, Questions — each with the description the model reads, and a note on any that is switched off and why.

## Slash commands
[↑ Back to top](#neon-companion)

Type `/` and the list opens with every command and its summary; after the command and a space, the argument list follows for any argument that can be listed. `//`, `///` and `////` are the aliases (for `/settings`, `/tools` and `/skills`); they are never listed.

| Command | What it does |
|---|---|
| `/about` | Show the app's version, runtime, folders, components and licence. |
| `/clear` | Start a new conversation and clear the screen. |
| `/compact [focus]` | Shrink the current context; a focus steers the summary. |
| `/copy [n \| all]` | Copy the last reply to the clipboard as Markdown, or reply *n*, or the whole transcript. |
| `/cwd [path \| ~]` | Show or change the working directory. |
| `/draft` | Write the next message in your editor; the file is sent when it is saved and closed. |
| `/echo <text>` | Print a line as a reply and read it aloud when speech is on. |
| `/emptytrash` | Empty the working directory's `.trash` for good (asks first). |
| `/exit` | Exit the app. |
| `/explore [path]` | Open the working directory in your file browser. |
| `/forget` | Forget every memory (asks first). |
| `/git user [force]` | Write the *Git email* and *Git name* settings into the working directory's repository config as `user.email` / `user.name`; a `[user]` section already there is kept unless `force`. |
| `/help` | Show the commands and the keys. |
| `/interrupt [on\|off]` | Toggle the wake-word interrupt during a spoken reply. |
| `/learn [note \| sessions [N \| text]]` | Write or improve a skill in the background from the last turn, or from the stored sessions. |
| `/loop <count> <message>`, `/loop infinite <message>` | Send the message that many times, or until ESC or Ctrl+C stops it, each reply waited for; a cancelled, withdrawn or failed turn ends the loop. |
| `/mcp` | Connect external MCP servers and switch their tools on or off. |
| `/memcopy <profile> [overwrite]` | Copy this profile's memory into another. |
| `/memory` | List and prune the memory items. |
| `/model [id]` | Pick a model from the server's list, or set one. |
| `/new` | Start a new conversation without clearing the screen. |
| `/operata [reset]` | Edit `operata.md` (the operating rules) in your editor, or go back to the default. |
| `/persona [reset]` | Edit `persona.md` (the personality) in your editor, or go back to the default. |
| `/profile [name \| add <name> \| delete <name> \| rename <name> <new> \| reset [name] \| edit \| reload]` | Switch, create, delete, rename or reset a profile; `edit` opens the loaded profile's `profile.json` in your editor and `reload` reads it back from disk, reconnecting only what changed. A name is 1 to 32 letters, digits, `-` or `_`, and not `neon` or one of the verbs. |
| `/queue` | List and prune the messages queued while a reply runs. |
| `/reasoning [level]` | Pick the reasoning effort (`none`, `low`, `medium`, `high`, `xhigh`). |
| `/remember <text>` | Add a memory. |
| `/server [url]` | Pick an LLM server found on the usual ports, or set one. |
| `/session [id \| purge <id> \| purge older <age> \| purge all \| title <text>]` | List, restore, rename and purge the stored sessions. An age is days as a bare number (`30`, `0`), or a duration with units: `12h`, `90m`, `2 hours`, `1d 6h`. |
| `/settings`, `//` | Edit and save the settings. |
| `/skills`, `////` | List the skills and edit the skill, reflection and project-file settings. |
| `/skills edit <name>` | Open a skill's `SKILL.md` in your editor. |
| `/speak [file [n] \| n]` | Read a text file from the working directory aloud as a reply; alone resumes, a number starts from that sentence. |
| `/splash` | Start a new conversation and show the splash screen. |
| `/stt [on\|off]` | Toggle speech input. |
| `/sysprompt` | Show the system prompt and the tools sent to the model. |
| `/timer [duration [name] \| stop <name> \| stop all]` | List the timers, or start one (`10m`, `90s`, `1h30m`), or stop one. |
| `/tools`, `///` | Switch the model's tools on or off and edit the Ask, Files, Git and Web settings. |
| `/tree [path]` | Print a tree of the working directory. |
| `/tts [on\|off]` | Toggle speech output. |
| `/usage` | Show token usage and performance statistics. |
| `/view <image>` | Show an image from the working directory in the transcript. |
| `/vocalia [reset]` | Edit `vocalia.md` (the spoken-reply directive) in your editor, or go back to the default. |
| `/wake [on\|off]` | Toggle the speech-input wake word. |
| `/window` | Show the terminal window's width and height. |

## Tools
[↑ Back to top](#neon-companion)

What the model can call, in the groups `/tools` and `/sysprompt` show. A group's switch (`File tools`, `Git tools`, `Shell command policy`, `Web tools`, `Memory`, `Agent skills`, `Session tool`, `Ask user`, `MCP servers`) offers or withholds the whole group; a single tool goes on or off on `/tools`' Offered tab. Required arguments come first; `?` marks an optional one.

### Clock

| Tool | Arguments | What it does |
|---|---|---|
| `get_current_time` | `zone?` | The current date, time, weekday and time zone; seeded at the start of every conversation. |
| `shift_date` | `date, days?, weeks?, months?, years?` | Adds or subtracts days, weeks, months or years to a date and returns it with its weekday. |
| `days_between` | `from, to` | Counts the days from one date to another (negative when the second is earlier). |

### Timers

| Tool | Arguments | What it does |
|---|---|---|
| `start_timer` | `name?, hours?, minutes?, seconds?` | Starts a named countdown; the user is alerted when it ends. Several can run at once. |
| `stop_timer` | `name` | Stops a running timer by name, or silences one that has gone off. |
| `list_timers` | — | Every running timer and how long each has left. |

### Files

All paths are relative to the working directory; nothing outside it is reachable. `restore` is offered only while *File safe edits* is on; `delete`, `zip` and `unzip` start switched off.

| Tool | Arguments | What it does |
|---|---|---|
| `get_working_directory` | — | The working directory's path; seeded at the start of every conversation. |
| `search_files` | `text?, path?, files?, regex?, context?, output?, order?, limit?, depth?` | Searches the text files for a word, phrase or regex (`file:line: text`, with context lines when asked); without `text` it lists a folder, a tree (`depth` 2–4), the files matching a name pattern, or the most recently changed files. |
| `file_info` | `path` | Size, modified time, line and word count, line ending and BOM of a file; counts and total size of a folder; the way to check that something exists. |
| `read_file` | `path, start_line?, max_lines?` | Reads a text file or part of it (a negative `start_line` counts from the end); a partial read names the line to continue from. |
| `view_image` | `path?, paths?` | Attaches image files to the next message so the model can see them — one, or up to *File view image max (per call)* at once. |
| `write_file` | `path, content, mode?` | Writes a text file: `create` (the default, an existing file left alone), `overwrite`, or `append` on a new line; the result reports the size, lines and words. |
| `patch_file` | `path, old_text, new_text, replace_all?` | Replaces one occurrence of `old_text` (or every one with `replace_all`), matched exactly first and then with spacing, indentation, escapes and typographic quotes tolerated; the result shows the edited lines. |
| `create_directory` | `path` | Creates a folder and any missing parents. |
| `move` | `from, to, overwrite?` | Renames or moves a file or folder; refuses to replace anything at the new path unless `overwrite` is true. |
| `copy` | `from, to, overwrite?` | Copies a file or folder to a new path under the same overwrite rule; a folder copied over a folder merges into it. |
| `delete` | `path` | Deletes a file or folder — into `.trash` while *File safe edits* is on, for good when it is off. |
| `restore` | `path, overwrite?` | Puts back the newest `.trash` copy of a file or folder; with `overwrite` it undoes the last edit of a file. |
| `zip` | `path, to?, overwrite?` | Packs a file or folder into a `.zip` archive, by default beside the original. |
| `unzip` | `path, to?, overwrite?` | Extracts a `.zip` archive into a folder, all or nothing. |
| `open` | `path?` | Opens a file in the user's own editor or viewer, or a folder in Explorer; no path opens the working directory. |

### Git

Every git tool takes an optional `path` — the file or folder the call is about, and where the repository is looked for (a nested repository is reached through it). The repository's root must be the working directory or a folder inside it. Runs in-process (LibGit2Sharp), local only: no fetch, pull, push or clone. `git_discard` and `git_delete` start switched off.

| Tool | Arguments | What it does |
|---|---|---|
| `git_status` | `path?` | The branch, how far ahead or behind its upstream it is, and every staged, modified, untracked or conflicted path. |
| `git_log` | `path?, ref?, max_commits?` | The commits reachable from `ref` (HEAD by default), newest first; with a file, only the commits that changed it. |
| `git_show` | `ref, path?` | One commit: author, date, message and the files it changed; with a file, its text at that commit; with a folder, its entries. |
| `git_diff` | `path?, ref?, from?, to?, staged?, max_lines?` | A unified diff of the unstaged changes, the staged ones, one commit against its parent, or everything between two commits. |
| `git_blame` | `path, from_line?, to_line?, ref?` | Who last changed each line of a file and in which commit, a window of lines at a time. |
| `git_branch` | `action, name?, new_name?, start_point?, switch_to?, path?` | `list`, `create`, `switch` or `rename` branches; a switch never overwrites local changes. |
| `git_stage` | `action, paths, path?` | `stage` or `unstage` the paths named, or `.` for everything changed under `path`. |
| `git_commit` | `message, amend?, allow_empty?, path?` | Commits what is staged, signed with the identity in git config (`user.name` / `user.email`). |
| `git_stash` | `action, message?, index?, include_untracked?, path?` | `push` saves the working tree's changes aside, `pop` or `apply` brings a stash back, `list` shows them. |
| `git_discard` | `paths?, ref?, path?` | Throws uncommitted changes away: the paths named back to `ref`, or with none a hard reset of the whole tree (untracked files left alone). |
| `git_delete` | `kind, name?, index?, path?` | Removes a local `branch` (never the one checked out), a `tag`, or a `stash` by index. |

### Shell

A command line on your machine. It **starts** in the working directory (`workdir` names a folder under it) but is not confined to it: a shell can reach anything you can, so the guard is the `Shell command policy` — under `ask` (the default) the command is shown on the pane with its shell and you choose Deny, Allow once, Allow its prefixes for this session, or Allow them always (saved to the profile); a denial is returned to the model as an error it is told not to work around. Every child runs with no window, its output read as UTF-8, colour and pagers off, stdin closed (a background one keeps it for `write`); on a timeout the command and everything it started are killed. Background processes die with the app. Under `--headless` nothing can ask, so `ask` runs only what the allow list covers — set `NEONCOMPANION_COMMAND_POLICY=yolo` for a scripted run. A crash of the app leaves a running command to Windows.

| Tool | Arguments | What it does |
|---|---|---|
| `run_command` | `command, shell?, workdir?, timeout?, background?, notify?` | Runs the line in `powershell` (the default), `cmd` or `bash` (Git Bash, offered when found) and returns `exit N in T s (shell): command`, then the output, stderr under its own separator. With `background` (or a `timeout` over the foreground cap) it starts the command and returns its `proc_…` id at once; with `notify` you see a `⚡` line when it exits and the model gets a `process poll` seeded into its next turn. |
| `execute_code` | `language, code, timeout?` | Runs a script in a fresh `python`, `node` or `powershell` process (the languages `Shell code languages` allows and the machine has) and returns `exit N in T s (language, K tool calls): first line`, then what it printed. With `Shell tool bridge` on the script calls the app's other tools by name through a module written beside it — Python `from neon_tools import call, read_file`, Node `const neon = require('neon_tools'); await neon.call('read_file', { path })` inside `neon.run(async () => …)`, PowerShell `Invoke-NeonTool read_file @{ path = 'x' }` — over a loopback socket with a per-run token; `execute_code` and `ask_user` are out of reach, a nested `run_command` is approved as usual but never in the background. With it off (the default) no module is written, the header has no `K tool calls` clause and the script does everything itself. The approval pane asks once per language (`Allow python scripts for this session`). No kernel: each call is a fresh process. |
| `process` | `action, session_id?, data?, timeout?, offset?, limit?` | The background processes: `list` them; `poll` one for its state and the output since the last poll; `log` a numbered window of its last 5,000 lines (`offset`, `limit`); `wait` up to `timeout` seconds; `kill` it and everything it started; `write` / `submit` text to its stdin (submit adds a newline); `close` a finished one. Any unique prefix of the id will do; at most 16 run at once and the newest 64 finished ones are kept. |

### Web

| Tool | Arguments | What it does |
|---|---|---|
| `web_search` | `query, max_results?` | Searches the web (DuckDuckGo or SearXNG) and returns the top results: title, URL, snippet. |
| `web_fetch` | `url, offset?` | Fetches a page and returns its readable content as Markdown, 32,000 characters at a time; also reads plain text, JSON, XML and CSV. |
| `open_url` | `url?, urls?` | Opens a link — or up to five — in the user's own browser. |
| `download_file` | `url, path?, overwrite?` | Downloads a file (a picture, a PDF, an archive…) into the working directory, up to 50 MB; needs *File tools* on too. |

### Memory

| Tool | Arguments | What it does |
|---|---|---|
| `save_memory` | `text` | Saves one lasting fact about the user to long-term memory, known in every later session. |
| `recall_memory` | — | Everything remembered about the user, oldest first; seeded at the start of every conversation. |

### Skills

| Tool | Arguments | What it does |
|---|---|---|
| `load_skill` | `name, file?` | Loads a skill's full instructions by name (the catalog is in the system prompt), or one of the files bundled with it. Offered only while a skill is installed. |
| `skill_editor` | `action, scope, name, description?, instructions?, summary?` | `create` or `update` a skill under the `profile` or `global` root — a named folder of instructions kept for later sessions. Never deletes. |

### Sessions

| Tool | Arguments | What it does |
|---|---|---|
| `session_manager` | `action, query?, id?, max_results?, from_turn?, to_turn?` | `search`, `list` or `read` this profile's earlier conversations; the one on screen is left out, and nothing is restored or purged. |

### Questions

| Tool | Arguments | What it does |
|---|---|---|
| `ask_user` | `questions` | Puts up to *Ask max questions* multiple-choice questions on the pane (each with 2 to *Ask max choices per question* options, `single` or `multi`, plus an *Other…* row) and waits for the answers; ESC declines them all. |

### MCP servers

Every connected MCP server is a group of its own, its tools offered as `<server>__<tool>` with the descriptions the server publishes — a gateway's `get_current_time` never collides with the app's. They come and go with the server: switch one off on `/mcp`' Servers tab and its group is gone; switch a single tool off on the Tools tab and the rest stay. No approval step stands before a call — enabling the server is the consent.

## Screenshots
[↑ Back to top](#neon-companion)

### Markdown rendering
![markdon](./assets/screenshots/screenshot_markdown.png)

### Vision support (files in working directory)
![vision](./assets/screenshots/screenshot_vision1.png)

### Vision support (drag or copy/paste to terminal)
![vision](./assets/screenshots/screenshot_vision2.png)

### Automatic reflection/introspection
![reflection](./assets/screenshots/screenshot_reflection.png)

### System prompt audit
![sysprompt](./assets/screenshots/screenshot_system_prompt.png)

### System prompt tool audit
![sysprompt](./assets/screenshots/screenshot_system_prompt_tools.png)

### Server picker
![server](./assets/screenshots/screenshot_server_picker.png)

### Model picker
![model](./assets/screenshots/screenshot_model_picker.png)

### Model picker
![reasoning](./assets/screenshots/screenshot_reasoning_picker.png)

### Profile picker
![profile](./assets/screenshots/screenshot_profile_picker.png)

### Answer picker
![ask](./assets/screenshots/screenshot_ask.png)

### Voice/Speech features
![voice](./assets/screenshots/screenshot_voice.png)

### Message queue
![queue](./assets/screenshots/screenshot_queue.png)

### @-mention for files/folders
![at_mention](./assets/screenshots/screenshot_at_mention.png)

### Dynamic auto-complete
![autocomplete](./assets/screenshots/screenshot_dynamic_autocomplete.png)

### General settings
![settings](./assets/screenshots/screenshot_general_settings.png)

### Session settings
![settings](./assets/screenshots/screenshot_session_settings.png)

### LLM settings
![settings](./assets/screenshots/screenshot_llm_settings.png)

### TTS settings
![settings](./assets/screenshots/screenshot_tts.png)

### STT settings
![settings](./assets/screenshots/screenshot_stt.png)

### Memory management
![memory](./assets/screenshots/screenshot_memory.png)

### Help system
![help](./assets/screenshots/screenshot_help.png)

### Skill management
![skills](./assets/screenshots/screenshot_skills.png)

### Skill options
![skills](./assets/screenshots/screenshot_skill_options.png)

### Reflection management
![skills](./assets/screenshots/screenshot_reflection_settings.png)

### Project files
![skills](./assets/screenshots/screenshot_skills_project.png)

### Tools offered
![tools](./assets/screenshots/screenshot_tools.png)

### Ask (questions) settings
![tools](./assets/screenshots/screenshot_ask_settings.png)

### File settings
![tools](./assets/screenshots/screenshot_file_settings.png)

### Git settings
![tools](./assets/screenshots/screenshot_git_settings.png)

### Browser settings
![tools](./assets/screenshots/screenshot_browser_settings.png)

### MCP settings
![mcp](./assets/screenshots/screenshot_mcp.png)

### Session recall
![mcp](./assets/screenshots/screenshot_session_recall.png)

### Custom persona
![persona](./assets/screenshots/screenshot_custom_profile_persona.png)

### Welcome splash screen
![splash](./assets/screenshots/screenshot_splash.png)

## Why "Neon"
[↑ Back to top](#neon-companion)

During early development, I was experimenting with synthwave-style themes in Spectre.Console while simultaneously testing the Vosk voice integration. I needed a short, punchy wake word, and "Neon" fit the aesthetic perfectly. The name stuck for the project. Today, while the default profile is still named "Neon," the system is completely configurable—allowing you to create as many custom profiles, personas, and wake words as you like.
