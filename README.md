<div align="center">

# NvChat

**A Windows desktop app to chat with the free LLMs on [build.nvidia.com](https://build.nvidia.com)**

Streaming chat · saved conversations · live markdown/code rendering · reasoning display · image attachments · in-app auto-update — ships as a single `.exe`

**English** · [한국어](README.ko.md)

[![build](https://github.com/akon47/NvChat/actions/workflows/build.yml/badge.svg)](https://github.com/akon47/NvChat/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/akon47/NvChat?label=release&color=76B900)](https://github.com/akon47/NvChat/releases/latest)
[![downloads](https://img.shields.io/github/downloads/akon47/NvChat/total?color=76B900)](https://github.com/akon47/NvChat/releases)
[![stars](https://img.shields.io/github/stars/akon47/NvChat?style=flat&color=76B900)](https://github.com/akon47/NvChat/stargazers)
[![license](https://img.shields.io/github/license/akon47/NvChat)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![platform](https://img.shields.io/badge/platform-Windows-0078D6)

<img src="docs/screenshot.png" alt="NvChat screenshot" width="880">

</div>

---

## ✨ Features

- **Model picker** — loads the live model list via `/v1/models` (falls back to a curated list on failure)
- **Streaming chat** — real-time token streaming with an instant **Stop** button
- **Live markdown while streaming** — formatting renders as the answer arrives (like Claude / ChatGPT), not only when it finishes
- **Reasoning display** — collapsible thinking for models that emit `reasoning_content` / `<think>` (deepseek-r1, etc.)
- **Image attachments (vision)** — attach images to ask vision models (llama-3.2-vision, etc.); large images are auto-downscaled
- **In-app auto-update** — checks GitHub Releases, downloads the new `.exe`, verifies its SHA-256, and swaps itself in place (single-file friendly, no installer)
- **Per-conversation usage** — tracks requests and prompt/completion tokens **per model**, so you can see what each model spent in a conversation (even across mid-chat model switches)
- **Desktop launcher** — a global hotkey (default `Ctrl+Shift+Space`) pops a **quick-chat mini window** from anywhere + **system tray** (close minimizes to tray)
- **Personalization** — **custom instructions** (about you / response style) applied to every chat + reusable **prompt presets**
- **Searchable model picker** — filter 100+ models by typing
- **Message actions** — **regenerate** a response, **edit & resend** a user message, **delete** or **copy** individual messages
- **Selectable markdown** — headings/lists/nested & task lists/quotes/**tables**/links, **syntax-highlighted code blocks** with a copy button; assistant text is drag-selectable
- **Conversation management** — sidebar **search**, **date groups** (Today/Yesterday/…), **pin**, **rename**, autosave, model-generated **auto titles**
- **Per-conversation settings** — system prompt, Temperature/Top P/Max Tokens/Penalties
- **Export** — copy the whole conversation or save as Markdown
- **Quality of life** — scroll-to-bottom button, remembers window size/position, collapsible sidebar, keyboard shortcuts
- **Safe storage** — API key encrypted with Windows DPAPI, atomic file writes + automatic backup of corrupt files
- **UI** — custom borderless dark theme with an NVIDIA-green accent; ChatGPT/Claude-style layout (bubble only on your messages)

## 📦 Download

Grab the single `NvChat.exe` from [**Releases**](https://github.com/akon47/NvChat/releases/latest) and run it.
It's a self-contained single file that runs even without the .NET runtime installed. (Windows x64)

> Once you're on a build with auto-update, new versions are offered inside the app — no need to re-download manually.

## 🔑 Get an API key

1. Sign in to [build.nvidia.com](https://build.nvidia.com)
2. On any model page, click **Get API Key** to create an `nvapi-...` key
3. Paste it into the **Settings** window on first launch (or the ⚙ button), then **Test connection → Save**

> The key is stored **only on this PC + this Windows account**, encrypted with DPAPI (`%APPDATA%\NvChat\settings.json`). It cannot be decrypted on another PC/account.

## ⌨️ Shortcuts

| Key | Action |
|---|---|
| `Enter` | Send (switchable to `Ctrl+Enter` in Settings) |
| `Shift+Enter` | New line |
| `Ctrl+N` | New conversation |
| `Ctrl+Shift+Space` | Quick chat from anywhere (global · configurable) |

## 🛠️ Build from source

```powershell
# Run for development
dotnet run --project NvChat/NvChat.csproj

# Build the single-file release exe
dotnet publish NvChat/NvChat.csproj -c Release
# → NvChat/bin/Release/net8.0-windows/win-x64/publish/NvChat.exe
```

Requires the .NET 8 SDK (Windows).

## 🚀 Releases (automated)

Pushing a `v*` tag triggers a GitHub Actions workflow ([`release.yml`](.github/workflows/release.yml)) that
builds the single `NvChat.exe`, publishes a matching `NvChat.exe.sha256`, and uploads both to that tag's Release.
The app's in-app updater uses that checksum to verify downloads.

```bash
git tag v1.0.0
git push origin v1.0.0
```

## 🧩 Tech

.NET 8 / WPF (`net8.0-windows`), MVVM, a custom `WindowView` (borderless WindowChrome), a dark palette, and a
self-written markdown renderer + syntax highlighter — no external UI libraries.

- Endpoint: `https://integrate.api.nvidia.com/v1` (OpenAI-compatible, configurable in Settings)
- Data: `%APPDATA%\NvChat\` (`settings.json`, `conversations.json`)
- The free tier has rate limits — on HTTP 429, retry after a moment.

## 📄 License

[MIT](LICENSE) — free to use, modify, and distribute.
