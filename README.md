# CS2-GameManager

Custom CounterStrikeSharp plugin for switching a single CS2 server between multiple gameplay modes, managing bots, changing maps and exposing contextual help through chat.

## Mode commands

- `.1v1` / `.arena` — ErkutArena
- `.retake` / `.retakes` — Retakes Plugin
- `.5v5` / `.comp` — MatchZy
- `.mix` / `.pug` — MatchZy
- `.prac` / `.practice` — MatchZy Practice
- `.mode` — current mode
- `.modes` — available modes
- `.gmreload` — reload GameManager config

Unknown dot commands are intentionally passed through, so native MatchZy commands keep working.

## Help

`!help` shows the GameManager summary.

Detailed sections:

- `!help server`
- `!help match`
- `!help prac`
- `!help retake`
- `!help 1v1`
- `!help all`

The MatchZy and Retakes sections are based on the commands exposed by the installed upstream plugins. ErkutArena's native command set is not publicly documented in the detected package, so the 1v1 help currently covers GameManager integration and bot controls.

## Bot management

### Remove every bot

```text
!bots off
```

Equivalent server console commands:

```text
bot_auto_vacate 1; bot_quota 0; bot_kick
```

### Automatic bots that make room for humans

```text
!bots auto
```

Default target is 12 total active players. You can override it:

```text
!bots auto 10
```

Equivalent server setup:

```text
bot_auto_vacate 1
bot_quota_mode fill
bot_quota 12
bot_join_after_player 0
bot_join_delay 0
```

In `fill` mode the server adjusts the number of bots around the target player count, and `bot_auto_vacate 1` makes bots leave room for real players.

GameManager automatically enables this policy when switching to `.1v1`. When switching from Arena to Retake, 5v5, Mix or Practice it sets `bot_quota 0` and executes `bot_kick` so Arena bots do not leak into another mode.

The default Arena target is configurable with:

```json
"ArenaBotFillTarget": 12
```

## Maps

### Normal/local maps

```text
!maps
!maps 2
!gmap de_mirage
```

`!maps [page]` lists maps returned by CounterStrikeSharp's server map API. `!gmap <name>` validates the map and changes level.

### Steam Workshop maps

Download/update and switch to a Workshop item by numeric ID:

```text
!wsmap 3070290869
```

Equivalent native server command:

```text
host_workshop_map 3070290869
```

List Workshop maps currently known/downloaded by the server:

```text
!wsmaps
```

GameManager executes:

```text
ds_workshop_listmaps
```

The list is printed in the server console. A Workshop map shown there is available to switch to with its map name:

```text
!wmap <map_name>
```

Equivalent native command:

```text
ds_workshop_changelevel <map_name>
```

This is also the quickest way to verify whether maps added through an AMP Workshop list/collection were actually mounted and are usable by CS2.

## Verified plugin paths for this server

```text
plugins/MatchZy/MatchZy.dll
optional/RetakesPlugin/RetakesPlugin.dll
optional/ErkutArena/ErkutArena.dll
```

The mode switcher loads/unloads these exact paths.

## CounterStrikeSharp commands

The corresponding console commands include:

- `css_mode [mode]`
- `css_modes`
- `css_gmreload`
- `css_help [section]`
- `css_bots off|auto [target]`
- `css_gmap <map>`
- `css_maps [page]`
- `css_wsmap <WorkshopID>`
- `css_wmap <map_name>`
- `css_wsmaps`

Commands prefixed with `css_` are exposed by CounterStrikeSharp in chat through the usual `!`/`/` trigger without the `css_` prefix.

## Configuration

The default configuration is generated automatically on first run at:

`game/csgo/addons/counterstrikesharp/configs/plugins/GameManager/GameManager.json`

Each mode is a list of server-console commands, so transitions can be modified without recompiling the plugin.

## Build

Requires the .NET 8 SDK.

```bash
dotnet restore
dotnet publish -c Release -o out
```

The primary output is:

`out/GameManager.dll`

## Install

Copy the build output to:

`game/csgo/addons/counterstrikesharp/plugins/GameManager/`

Then restart the CS2 server or hot-reload the plugin with CounterStrikeSharp.

## GitHub Actions

The included workflow builds the plugin on pushes to `main` and on manual runs, then uploads the compiled output as a workflow artifact.
