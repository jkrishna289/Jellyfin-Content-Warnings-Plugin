# Jellyfin Content Warnings Plugin

Automatically tags your movies and TV shows with content warnings using Groq AI.

Tags are stored with a `CW:` prefix (e.g. `CW:Violence`, `CW:Language`) so they
never clash with your existing tags and are easy to filter.

---

## How It Works

1. You enter your Groq API key in the Jellyfin admin dashboard
2. Every time a new item is added (or on a metadata refresh), the plugin checks
   if it already has `CW:` tags
3. If not → it asks Groq AI for the content descriptors for that title
4. Groq returns the official rating (R, PG-13, TV-MA…) and descriptors
5. These are saved into Jellyfin as tags and the official rating field
6. Any Jellyfin client (including Wholphin) can then read them via the API

---

## Build Instructions (on your Debian server)

### 1. Install .NET 8 SDK
```bash
wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update
sudo apt install -y dotnet-sdk-8.0
```

### 2. Clone and build
```bash
git clone https://github.com/YOUR_USERNAME/jellyfin-plugin-content-warnings
cd jellyfin-plugin-content-warnings
dotnet publish --configuration Release --output ./dist
```

### 3. Install the plugin
```bash
# Find your Jellyfin plugin directory (usually one of these):
# /var/lib/jellyfin/plugins/
# /etc/jellyfin/plugins/

sudo mkdir -p /var/lib/jellyfin/plugins/ContentWarnings
sudo cp dist/Jellyfin.Plugin.ContentWarnings.dll \
        /var/lib/jellyfin/plugins/ContentWarnings/

sudo systemctl restart jellyfin
```

### 4. Configure
1. Open Jellyfin → Admin Dashboard → Plugins → Content Warnings
2. Enter your Groq API key (free at https://console.groq.com)
3. Choose which model and whether to tag movies, TV shows, or both
4. Click Save

The plugin will now automatically tag any new media added to your library.
To process your **existing** library, do a metadata refresh on each library
(Dashboard → Libraries → ··· → Scan All Metadata).

---

## Tag Format

| Tag | Meaning |
|-----|---------|
| `CW:Violence` | Violence |
| `CW:Graphic Violence` | Graphic Violence |
| `CW:Gore` | Gore |
| `CW:Language` | Mild profanity |
| `CW:Strong Language` | Heavy profanity |
| `CW:Sexual Content` | Sexual content |
| `CW:Nudity` | Nudity |
| `CW:Drug Use` | Drug use |
| `CW:Alcohol Use` | Alcohol use |
| `CW:Smoking` | Smoking |
| `CW:Frightening Scenes` | Frightening scenes |
| `CW:Disturbing Content` | Disturbing content |
| `CW:Self-Harm` | Self-harm |
| `CW:Suicide` | Suicide |
| `CW:Fantasy Violence` | Fantasy/animated violence |

---

## Clearing Tags (to re-process an item)

Remove all `CW:` tags from the item manually in Jellyfin's metadata editor,
then trigger a metadata refresh. The plugin will re-query Groq on the next scan.

---

## Requirements

- Jellyfin `10.10.x` or `10.11.x`
- .NET 8
- Free Groq API key
