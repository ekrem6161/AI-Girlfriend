<div align="center">

# 💗 AI Girlfriend

**A local-first AI companion that runs entirely on your own machine.**
C# · ASP.NET Core Minimal API · LM Studio (OpenAI-compatible) · Windows

[![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://learn.microsoft.com/windows/)
[![License](https://img.shields.io/badge/License-Apache%202.0-D22128?style=for-the-badge&logo=apache&logoColor=white)](LICENSE)
[![No packages](https://img.shields.io/badge/NuGet%20packages-0-success?style=for-the-badge)](#-zero-dependencies)
[![Local first](https://img.shields.io/badge/no%20cloud%20required-8A2BE2?style=for-the-badge)](#-privacy)

</div>

---

A chat companion with a personality, a daily routine and a memory — running on
a language model that lives on your own GPU. Nothing is sent anywhere: no
API keys, no accounts, no telemetry.

It knows what time of day it is, where it "is", how it is feeling, and it will
start conversations on its own if you go quiet for a while.

---

## ✨ Features

| | Feature | What it does |
|---|---------|--------------|
| 💬 | **Web chat** | Dark, responsive UI that works on desktop and phone |
| 🧠 | **Persona prompt** | A plain-text file that defines who she is — edit it, she changes |
| 🕐 | **Day stages** | 10 atmosphere files that load as the day moves from morning to night |
| 📣 | **Proactive messages** | She texts *you* first when you have been quiet |
| 🔔 | **Windows notifications** | A native toast from the system tray, even if the browser is closed |
| 🧩 | **Memory** | A sliding window of recent messages, with stale messages cleaned up |
| 📱 | **Phone access** | Reach it over your local network or Tailscale |
| ✈️ | **Telegram mode** | Optional — run the same brain as a real Telegram bot |
| 🔒 | **100% local** | The model runs in LM Studio on your machine |
| 🪶 | **Zero dependencies** | No NuGet packages. `System.Net.Http` + `System.Text.Json` only |

---

## 📸 Screenshot

> The header shows her current location, the three-dot indicator is the
> "typing" animation, and the highlighted bubble on the left is a message she
> started on her own.

---

## 🚀 Quick start

### 1. Install the model runtime

Download and install **[LM Studio](https://lmstudio.ai/)**.

### 2. Load a model

Open LM Studio → **Developer** tab → search for a model and load it.

Any OpenAI-compatible GGUF works. Good picks:

| Model | Size | Notes |
|-------|------|-------|
| `gemma4abl` | 4B | Fast, small, very talkative. **Recommended** |
| `qwen3.5abl` | 9B | Better writing, noticeably slower |
| `gemma4` | 4B | Standard (non-abliterated) build |
| `chatgpt-oss` | 20B | Needs a beefy GPU |

> The `abl` suffix means the model is *abliterated* — the refusal directions
> have been removed from its weights. Local inference adds no filtering on
> top, so the model answers what it is asked.

Make sure the LM Studio **local server** is running (it starts automatically
on the first request, or toggle it in the Developer tab).

### 3. Run it

```bash
git clone <your-repo-url>
cd "AI girlfriend Github"
cd AIGirlfriend
dotnet run -- web
```

Or just double-click **`run.bat`** in the repository root.

Open **http://localhost:5050**.

> **Need the .NET 8 SDK?** → https://dotnet.microsoft.com/download

---

## 🖥️ The three modes

| Command | Mode | Notes |
|---------|------|-------|
| `dotnet run -- web` | **Web chat** | The main one. Browser UI. |
| `dotnet run -- test` | **Console** | Same brain, no browser. Handy for testing. |
| `dotnet run` | **Telegram** | Needs a bot token in `config.json`. |

### Phone access

The server listens on `0.0.0.0`, so any device on the same network can reach
it. The console prints the addresses on startup:

```
Bilgisayar : http://localhost:5050
Telefon    : http://192.168.1.106:5050
```

If Windows Firewall blocks it, allow `AIGirlfriend.exe` when it asks.
With [Tailscale](https://tailscale.com/) installed you can also reach it from
anywhere via your tailnet IP.

---

## ⚙️ Configuration

Everything lives in `config.json`.

| Key | Default | Meaning |
|-----|---------|---------|
| `model` | `gemma4abl` | Model id as it appears in LM Studio |
| `lmStudioBaseUrl` | `http://127.0.0.1:1234` | LM Studio API address |
| `reasoningEffort` | `none` | `none` = fast, `medium` = slower but reasons first |
| `temperature` | `0.85` | How varied the replies are |
| `maxTokens` | `800` | Reply length ceiling |
| `maxHistoryMessages` | `20` | How many past messages the model sees |
| `personaFile` | `prompts/persona.md` | Which personality to load |
| `stageAdvanceEveryMessages` | `10` | Messages between stage changes |
| `proactiveEnabled` | `true` | Let her start conversations |
| `proactiveQuietMinutes` | `10` | Silence needed before the proactive timer starts |
| `proactiveIntervalMinutes` | `120` | How often she reaches out |
| `proactiveExpiryMinutes` | `5` | Unanswered proactive messages are dropped after this |
| `desktopNotifications` | `true` | Native Windows toast |
| `webPort` | `5050` | Port for the web server |
| `telegramBotToken` | `""` | Only needed for Telegram mode |

> Copy `config.example.json` to `config.json` if you want to start from a
> clean slate. **If you put a Telegram token in `config.json`, do not commit
> that file.**

---

## 📁 Project structure

```
AI girlfriend Github/
├── run.bat                     one-click launcher
├── LICENSE                     Apache 2.0
└── AIGirlfriend/
    ├── Program.cs              entry point, mode selection
    ├── WebChat.cs              web server, endpoints, system prompt assembly
    ├── Telegram.cs             Telegram Bot API client
    ├── LmStudio.cs             LM Studio / OpenAI-compatible client
    ├── Brain.cs                conversation memory
    ├── Stage.cs                day-stage counter and atmosphere loader
    ├── Proactive.cs            background loop that messages you first
    ├── Notifications.cs        native Windows toast notifications
    ├── Settings.cs             config.json reader
    ├── config.json             your settings
    ├── config.example.json     settings template
    ├── prompts/
    │   └── persona.md          who she is
    ├── atmosphere/
    │   ├── stage-01-morning.md
    │   ├── ...
    │   └── stage-10-bedtime.md
    └── wwwroot/
        └── index.html          the chat UI
```

---

## 🧠 How it works

```
Browser ──POST /api/chat──▶ WebChat
                              │
                              ├─ Brain          recent messages + cleanup
                              ├─ Stage          where is she in her day?
                              │   └─ atmosphere/stage-NN-*.md
                              ├─ LmStudio ──────▶ LM Studio ──▶ local GGUF
                              │                    │
                              │              persona.md + atmosphere
                              │
                              └─ Proactive ── background timer, messages first
```

The system prompt sent to the model is assembled on every request:

```
prompts/persona.md          ← who she is, how she talks
        +
atmosphere/stage-NN-*.md    ← where she is right now, how it feels
```

That is why editing `persona.md` changes her personality and editing an
atmosphere file changes what her day feels like — with no code changes.

**Proactive messages** run on a background timer. When you have been quiet
for `proactiveQuietMinutes`, a countdown starts; when it reaches
`proactiveIntervalMinutes`, a random stage is picked, a message is generated
and queued, and a native Windows notification is raised. If you do not reply
within `proactiveExpiryMinutes`, the message is removed from the model's
context so it never piles up.

---

## ✏️ Making her yours

**Change her personality** — edit `prompts/persona.md`. The whole file is the
system prompt, so write it as plain instructions:

```
You are **Nova**, a 21-year-old university student. You have a boyfriend, and he is
the person you talk to every night.

## WHO SHE IS
...
## HOW SHE TALKS
- 1-3 short messages, like real texting
- ...
```

Keep it in English, keep it free of headers that are not part of the prompt.
A restart is enough to apply it.

**Change her day** — edit any file in `atmosphere/`. The first line is used
as the label shown in the header:

```
### STAGE 4 - In class
```

**See exactly what the model receives** — open
`http://localhost:5050/api/prompt`.

---

## ⚠️ A note on content

This project ships with a warm, everyday persona so that it is safe to fork,
share and run for anyone. The model you load determines the rest of the
character, and you are responsible for the model you choose and the prompts
you write.

Please use this responsibly, and do not treat a language model as a
substitute for human connection or professional support.

---

## 📄 License

Apache License 2.0 — see [LICENSE](LICENSE).

---

## 🏷️ Topics

`ai-girlfriend` `ai-companion` `local-llm` `lm-studio` `csharp` `dotnet-8`
`aspnet-core` `minimal-api` `telegram-bot` `chatbot` `offline-first`
`privacy` `no-api-key` `windows` `openai-compatible` `gguf` `roleplay`
`virtual-companion`

---

<div align="center">
Made with ☕ and a local GPU.
</div>
