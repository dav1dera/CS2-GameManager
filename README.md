# CS2-GameManager

Custom CounterStrikeSharp plugin for switching a single CS2 server between multiple gameplay modes through chat commands.

## Default chat commands

- `.1v1`
- `.retake`
- `.5v5`
- `.mix`
- `.prac`
- `.mode`
- `.modes`
- `.gmreload`

Unknown dot commands are intentionally passed through, so commands from other plugins such as MatchZy's `.bot`, `.rethrow`, `.spawn`, etc. keep working.

## CounterStrikeSharp commands

- `css_mode [mode]`
- `css_modes`
- `css_gmreload`

## How it works

Each mode is defined as a list of server-console commands. This lets GameManager unload/load mode-specific plugins, execute cfg files, restart the game and perform any other required transition without recompiling the plugin.

The default configuration is generated automatically on first run at:

`game/csgo/addons/counterstrikesharp/configs/plugins/GameManager/GameManager.json`

The default examples assume these plugin names:

- `MatchZy`
- `RetakesPlugin`
- `ErkutArena`

Adjust the generated JSON after checking the exact plugin load/module names with `css_plugins list`.

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

## Mode cfg files

The default config references:

- `game/csgo/cfg/gamemanager/1v1.cfg`
- `game/csgo/cfg/gamemanager/retake.cfg`
- `game/csgo/cfg/gamemanager/5v5.cfg`
- `game/csgo/cfg/gamemanager/mix.cfg`
- `game/csgo/cfg/gamemanager/prac.cfg`

They can initially be empty and filled only with mode-specific cvars/configuration.

## GitHub Actions

The included workflow builds the plugin on pushes to `main` and on manual runs, then uploads the compiled output as a workflow artifact.
