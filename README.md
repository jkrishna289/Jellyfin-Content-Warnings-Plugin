# 🎬 Jellyfin Content Warnings Plugin

[![License: GPL v2](https://img.shields.io/badge/License-GPLv2-blue.svg)](LICENSE)
[![Jellyfin 10.11](https://img.shields.io/badge/Jellyfin-10.11-00A4DC?logo=jellyfin)](https://jellyfin.org)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![GitHub Release](https://img.shields.io/github/v/release/jkrishna289/Jellyfin-Content-Warnings-Plugin)](https://github.com/jkrishna289/Jellyfin-Content-Warnings-Plugin/releases)
[![GitHub Stars](https://img.shields.io/github/stars/jkrishna289/Jellyfin-Content-Warnings-Plugin)](https://github.com/jkrishna289/Jellyfin-Content-Warnings-Plugin/stargazers)

Automatically tags your Jellyfin movies and TV shows with **content warnings** (Violence, Language, Nudity, etc.) using **Groq AI** — the same kind of descriptors you see on Netflix before a title starts playing.

Tags are stored with a `CW:` prefix (e.g. `CW:Violence`, `CW:Language`) so they never clash with your existing tags and are easy to filter by any Jellyfin client.

---

## ✨ Features

- 🤖 **AI-powered** — Uses Groq AI to identify content descriptors for any movie or TV show
- 🏷️ **Non-destructive** — Tags are prefixed with `CW:` and never overwrite your existing tags
- 🎬 **Movies & TV Shows** — Supports both, configurable in settings
- ⚡ **Auto-tagging** — New media added to your library is tagged automatically
- 🔄 **Manual task** — Run "Process Content Warnings" from Scheduled Tasks to tag your entire library at once
- ⏭️ **Smart skipping** — Already-tagged items are never re-processed
- 🔑 **Easy setup** — Just enter your free Groq API key in the Jellyfin admin dashboard

---

## 📋 Content Descriptors

The plugin tags items using descriptors from this standardised list:

| Tag | Description |
|-----|-------------|
| `CW:Violence` | Violence |
| `CW:Graphic Violence` | Graphic violence |
| `CW:Gore` | Gore |
| `CW:Language` | Mild profanity |
| `CW:Strong Language` | Heavy profanity |
| `CW:Profanity` | Profanity |
| `CW:Sexual Content` | Sexual content |
| `CW:Nudity` | Nudity |
| `CW:Sexual Violence` | Sexual violence |
| `CW:Drug Use` | Drug use |
| `CW:Alcohol Use` | Alcohol use |
| `CW:Smoking` | Smoking |
| `CW:Frightening Scenes` | Frightening scenes |
| `CW:Disturbing Content` | Disturbing content |
| `CW:Gambling` | Gambling |
| `CW:Self-Harm` | Self-harm |
| `CW:Suicide` | Suicide references |
| `CW:Racism` | Racism |
| `CW:Discrimination` | Discrimination |
| `CW:Fantasy Violence` | Fantasy/animated violence |

---

## 🚀 Installation

### Requirements
- Jellyfin `10.10.x` or `10.11.x`
- .NET 9 SDK (for building from source)
- A free [Groq API key](https://console.groq.com)

### Option A — Download from Releases (recommended)

1. Download the latest `Jellyfin.Plugin.ContentWarnings.dll` from the [Releases page](https://github.com/jkrishna289/Jellyfin-Content-Warnings-Plugin/releases)
2. Create the plugin folder:
   ```bash
   sudo mkdir -p /var/lib/jellyfin/plugins/ContentWarnings
   ```
3. Copy the DLL:
   ```bash
   sudo cp Jellyfin.Plugin.ContentWarnings.dll /var/lib/jellyfin/plugins/ContentWarnings/
   ```
4. Restart Jellyfin:
   ```bash
   sudo systemctl restart jellyfin
   ```

### Option B — Build from source

```bash
# 1. Install .NET 9 SDK (Debian/Ubuntu)
wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update && sudo apt install -y dotnet-sdk-9.0

# 2. Clone and build
git clone https://github.com/jkrishna289/Jellyfin-Content-Warnings-Plugin
cd Jellyfin-Content-Warnings-Plugin
dotnet publish Jellyfin.Plugin.ContentWarnings.csproj --configuration Release --output ./dist

# 3. Install
sudo mkdir -p /var/lib/jellyfin/plugins/ContentWarnings
sudo cp dist/Jellyfin.Plugin.ContentWarnings.dll /var/lib/jellyfin/plugins/ContentWarnings/
sudo systemctl restart jellyfin
```

---

## ⚙️ Configuration

1. Open Jellyfin → **Admin Dashboard** → **Plugins** → **Content Warnings** → **Settings**
2. Enter your **Groq API key** (free at [console.groq.com](https://console.groq.com))
3. Select your preferred **Groq model** (`llama-3.3-70b-versatile` recommended)
4. Choose whether to tag **Movies**, **TV Shows**, or both
5. Click **Save**

### Processing your existing library

Go to **Admin Dashboard** → **Scheduled Tasks** → **Content Warnings** → **Process Content Warnings** → click **Run**.

The task will scan your entire library, skip already-tagged items, and tag everything else. Progress is shown in the task runner.

---

## 🔄 How It Works

```
Library scan / new item added
           ↓
Plugin checks if item already has CW: tags
           ↓ (if not tagged)
Sends title + year to Groq AI
           ↓
Groq returns: official rating + content descriptors
           ↓
Saved to Jellyfin as tags: CW:Violence, CW:Language, etc.
           ↓
Any Jellyfin client can read and display them
```

---

## 🤝 Contributing

Contributions, issues and pull requests are welcome!

- Open an [issue](https://github.com/jkrishna289/Jellyfin-Content-Warnings-Plugin/issues) for bug reports or feature requests
- Fork the repo, make your changes, and open a pull request

### Development setup

```bash
git clone https://github.com/jkrishna289/Jellyfin-Content-Warnings-Plugin
cd Jellyfin-Content-Warnings-Plugin
dotnet build Jellyfin.Plugin.ContentWarnings.csproj
```

---

## 📄 License

This project is licensed under the [GNU General Public License v2.0](LICENSE).

---

## 🙏 Acknowledgements

- [Jellyfin](https://jellyfin.org) — the amazing open-source media server
- [Groq](https://groq.com) — blazing fast AI inference
- [Wholphin](https://github.com/damontecres/Wholphin) — the Android TV client this plugin was built to complement
