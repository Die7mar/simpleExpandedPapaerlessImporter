# Simple Expanded Paperless Importer

**[Deutsch](#deutsch) | [English](#english)**

---
# Testprojkt für umgang mit Github Copilot
<a name="deutsch"></a>
# 🇩🇪 Deutsch

## Überblick

**Paperless Importer** ist ein selbst gehosteter .NET 10-Dienst, der Dokumente automatisch in [Paperless-NGX](https://docs.paperless-ngx.com/) importiert. Er überwacht Eingangsordner, weist Korrespondenten und Tags zu, konvertiert E-Mails in PDFs und bietet ein Blazor-Web-Dashboard mit Login, Live-Statusanzeige und editierbaren Einstellungen.

## Features

- 📂 **Automatische Korrespondenten-Ordner** – erstellt für jeden Paperless-Korrespondenten einen Unterordner im Inbox-Verzeichnis
- 📨 **E-Mail-Konvertierung** – `.eml`-Dateien werden in PDFs mit Metadaten-Header konvertiert (QuestPDF)
- 🏷️ **Automatische Tags** – frei konfigurierbare Standard-Tags werden bei jedem Import gesetzt
- 📦 **ZIP-Archivierung** – erfolgreich importierte und fehlgeschlagene Dokumente werden als `.zip` im Done/Error-Ordner archiviert
- 🗑️ **Automatische Bereinigung** – ZIP-Archive werden nach konfigurierbaren Tagen gelöscht
- 🗄️ **SQLite-Datenbank** – Import-History wird persistent gespeichert
- 🌐 **Blazor-Dashboard** – Live-Status, Fehlerübersicht und editierbare Einstellungen
- ⚙️ **Live-Reload** – Einstellungen greifen sofort ohne App-Neustart
- 🔐 **Login-System** – Cookie-basierte Authentifizierung mit PBKDF2-Passwort-Hash; Standardbenutzer `admin`/`admin` wird beim ersten Start automatisch erstellt
- 🐳 **Docker-ready** – Multi-Stage Dockerfile für Linux

## Voraussetzungen

- [Docker](https://www.docker.com/) (empfohlen) **oder** .NET 10 SDK
- Eine laufende [Paperless-NGX](https://github.com/paperless-ngx/paperless-ngx)-Instanz
- Paperless API-Token (in Paperless unter *Einstellungen → API*)

## Schnellstart mit Docker

```bash
git clone <dieses-repo>
cd simpleExpandedPapaerlessImporter

# Ordner anlegen
mkdir -p data/inbox data/done data/error data/db logs

# API-Token setzen und starten
PAPERLESS_TOKEN=dein_token docker-compose up -d
```

Die Weboberfläche ist erreichbar unter: **http://localhost:8080**

## Docker Compose Konfiguration

```yaml
environment:
  - Paperless__BaseUrl=http://paperless-ngx:8000   # URL deiner Paperless-Instanz
  - Paperless__ApiToken=DEIN_TOKEN_HIER
  - Paperless__DefaultTags__0=importiert            # Standard-Tags
  - Paperless__InboxFolder=/data/inbox
  - Paperless__DoneFolder=/data/done
  - Paperless__ErrorFolder=/data/error
  - Paperless__RetentionDays=7                      # Aufbewahrung in Tagen
  - Paperless__PollingIntervalSeconds=30
  - SettingsFilePath=/app/data/settings.json        # Pfad für Live-Einstellungen
  - DatabasePath=/app/data/paperless-importer.db    # SQLite-Datenbankpfad
```

## Ordnerstruktur

```
data/inbox/
├── Max_Mustermann/     ← Korrespondenten-Ordner (automatisch erstellt)
│   └── rechnung.pdf   ← Datei hier ablegen → wird importiert
├── ACME_GmbH/
└── rechnung.pdf        ← Direkter Inbox ohne Korrespondent
data/done/
└── 20240407_143022/    ← Zeitgestempel-Unterordner
data/error/
└── 20240407_143055/
    ├── dokument.pdf
    └── dokument_error.log
data/db/
└── paperless-importer.db
```

## Unterstützte Dateiformate

| Format | Beschreibung |
|--------|-------------|
| `.pdf` | PDF-Dokumente |
| `.jpg` / `.jpeg` | JPEG-Bilder |
| `.png` | PNG-Bilder |
| `.tif` / `.tiff` | TIFF-Bilder |
| `.txt` | Textdateien |
| `.eml` | E-Mail-Dateien (werden in PDF konvertiert) |

## Lokal starten (ohne Docker)

```bash
cd src/PaperlessImporter
# appsettings.json anpassen
dotnet run
```

## Erster Start – Login

Beim ersten Start wird automatisch ein Standard-Benutzer angelegt:

- **Benutzername:** `admin`
- **Passwort:** `admin`

> ⚠️ **Bitte ändere das Passwort sofort nach dem ersten Login!** (Passwort-Änderung in den Einstellungen – kommt in einer zukünftigen Version, bis dahin: Datenbankdatei direkt editieren oder eigenen Benutzer anlegen)

## Einstellungen über die Weboberfläche ändern

1. **http://localhost:8080/settings** aufrufen
2. Felder bearbeiten
3. **„💾 Speichern & Übernehmen"** klicken → Einstellungen greifen sofort

---

<a name="english"></a>
# 🇬🇧 English

## Overview

**Paperless Importer** is a self-hosted .NET 10 service that automatically imports documents into [Paperless-NGX](https://docs.paperless-ngx.com/). It monitors inbox folders, assigns correspondents and tags, converts emails to PDFs, and provides a Blazor web dashboard with login, live status monitoring, and editable settings.

## Features

- 📂 **Automatic correspondent folders** – creates a sub-folder for every Paperless correspondent in the inbox directory
- 📨 **Email conversion** – `.eml` files are converted to PDFs with a metadata header (QuestPDF)
- 🏷️ **Automatic tags** – freely configurable default tags are applied to every import
- 📦 **ZIP archiving** – successfully imported and failed documents are archived as `.zip` in the done/error folder
- 🗑️ **Automatic cleanup** – ZIP archives are deleted after a configurable number of days
- 🗄️ **SQLite database** – import history is persisted across restarts
- 🌐 **Blazor dashboard** – live status, error overview, and editable settings
- ⚙️ **Live reload** – setting changes take effect immediately without restarting the app
- 🔐 **Login system** – cookie-based authentication with PBKDF2 password hashing; default user `admin`/`admin` is created automatically on first startup
- 🐳 **Docker-ready** – multi-stage Dockerfile for Linux

## Prerequisites

- [Docker](https://www.docker.com/) (recommended) **or** .NET 10 SDK
- A running [Paperless-NGX](https://github.com/paperless-ngx/paperless-ngx) instance
- Paperless API token (in Paperless under *Settings → API*)

## Quick Start with Docker

```bash
git clone <this-repo>
cd simpleExpandedPapaerlessImporter

# Create folders
mkdir -p data/inbox data/done data/error data/db logs

# Set API token and start
PAPERLESS_TOKEN=your_token docker-compose up -d
```

The web interface is available at: **http://localhost:8080**

## Docker Compose Configuration

```yaml
environment:
  - Paperless__BaseUrl=http://paperless-ngx:8000   # URL of your Paperless instance
  - Paperless__ApiToken=YOUR_TOKEN_HERE
  - Paperless__DefaultTags__0=imported              # Default tags
  - Paperless__InboxFolder=/data/inbox
  - Paperless__DoneFolder=/data/done
  - Paperless__ErrorFolder=/data/error
  - Paperless__RetentionDays=7                      # Retention period in days
  - Paperless__PollingIntervalSeconds=30
  - SettingsFilePath=/app/data/settings.json        # Path for live settings
  - DatabasePath=/app/data/paperless-importer.db    # SQLite database path
```

## Folder Structure

```
data/inbox/
├── John_Doe/           ← Correspondent folder (auto-created)
│   └── invoice.pdf    ← Drop file here → it will be imported
├── ACME_Corp/
└── invoice.pdf         ← Direct inbox without correspondent
data/done/
└── 20240407_143022/    ← Timestamped sub-folder
data/error/
└── 20240407_143055/
    ├── document.pdf
    └── document_error.log
data/db/
└── paperless-importer.db
```

## Supported File Formats

| Format | Description |
|--------|-------------|
| `.pdf` | PDF documents |
| `.jpg` / `.jpeg` | JPEG images |
| `.png` | PNG images |
| `.tif` / `.tiff` | TIFF images |
| `.txt` | Text files |
| `.eml` | Email files (converted to PDF) |

## Running Locally (without Docker)

```bash
cd src/PaperlessImporter
# Adjust appsettings.json
dotnet run
```

## First Start – Login

On the first start, a default user is created automatically:

- **Username:** `admin`
- **Password:** `admin`

> ⚠️ **Please change the password immediately after your first login!**

## Changing Settings via Web Interface

1. Open **http://localhost:8080/settings**
2. Edit the fields
3. Click **"💾 Save & Apply"** → changes take effect immediately
