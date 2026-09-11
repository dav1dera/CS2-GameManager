using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace GameManager;

public sealed class GameManagerPlugin : BasePlugin
{
    public override string ModuleName => "Dav1dera GameManager";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "dav1dera";
    public override string ModuleDescription => "Configurable CS2 mode switcher with .1v1/.retake/.5v5/.mix/.prac commands.";

    private GameManagerConfig _config = new();
    private string _configPath = "";
    private string _currentMode = "unset";
    private readonly Dictionary<string, string> _aliasToMode = new(StringComparer.OrdinalIgnoreCase);

    public override void Load(bool hotReload)
    {
        _configPath = Path.Combine(
            Server.GameDirectory,
            "csgo",
            "addons",
            "counterstrikesharp",
            "configs",
            "plugins",
            "GameManager",
            "GameManager.json"
        );

        LoadConfig();

        AddCommandListener("say", OnChat, HookMode.Pre);
        AddCommandListener("say_team", OnChat, HookMode.Pre);

        AddCommand("css_mode", "Show or switch GameManager mode.", OnModeCommand);
        AddCommand("css_modes", "List GameManager modes.", OnModesCommand);
        AddCommand("css_gmreload", "Reload GameManager configuration.", OnReloadCommand);

        Logger.LogInformation("GameManager loaded. Config: {ConfigPath}", _configPath);
    }

    private void LoadConfig()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);

        if (!File.Exists(_configPath))
        {
            _config = GameManagerConfig.CreateDefault();
            SaveConfig();
        }
        else
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<GameManagerConfig>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    }
                ) ?? GameManagerConfig.CreateDefault();

                _config.Modes = new Dictionary<string, ModeConfig>(
                    _config.Modes,
                    StringComparer.OrdinalIgnoreCase
                );
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unable to parse GameManager config. Keeping defaults.");
                _config = GameManagerConfig.CreateDefault();
            }
        }

        if (!string.IsNullOrWhiteSpace(_config.InitialMode))
            _currentMode = _config.InitialMode.Trim().ToLowerInvariant();

        RebuildAliasMap();
    }

    private void SaveConfig()
    {
        var json = JsonSerializer.Serialize(
            _config,
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(_configPath, json);
    }

    private void RebuildAliasMap()
    {
        _aliasToMode.Clear();

        foreach (var pair in _config.Modes)
        {
            var modeName = pair.Key.Trim().ToLowerInvariant();

            _aliasToMode[modeName] = modeName;
            _aliasToMode["." + modeName] = modeName;

            foreach (var alias in pair.Value.Aliases)
            {
                var normalized = alias.Trim().ToLowerInvariant();
                if (normalized.Length == 0)
                    continue;

                _aliasToMode[normalized] = modeName;

                if (!normalized.StartsWith('.'))
                    _aliasToMode["." + normalized] = modeName;
            }
        }
    }

    private HookResult OnChat(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || info.ArgCount < 2)
            return HookResult.Continue;

        var message = GetChatText(info);
        if (string.IsNullOrWhiteSpace(message) || !message.StartsWith('.'))
            return HookResult.Continue;

        var command = message.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]
            .Trim()
            .ToLowerInvariant();

        if (command == ".mode")
        {
            player.PrintToChat($"[GameManager] Modalità attuale: {_currentMode}");
            return HookResult.Handled;
        }

        if (command == ".modes")
        {
            PrintModes(player);
            return HookResult.Handled;
        }

        if (command == ".gmreload")
        {
            if (!CanManage(player))
            {
                player.PrintToChat("[GameManager] Non hai i permessi.");
                return HookResult.Handled;
            }

            LoadConfig();
            player.PrintToChat("[GameManager] Config ricaricata.");
            return HookResult.Handled;
        }

        if (!_aliasToMode.TryGetValue(command, out var mode))
        {
            // Important: unknown dot commands (e.g. MatchZy .bot) are not swallowed.
            return HookResult.Continue;
        }

        if (!CanManage(player))
        {
            player.PrintToChat("[GameManager] Non hai i permessi per cambiare modalità.");
            return HookResult.Handled;
        }

        SwitchMode(mode, player);
        return HookResult.Handled;
    }

    private static string GetChatText(CommandInfo info)
    {
        var parts = new List<string>();
        for (var i = 1; i < info.ArgCount; i++)
            parts.Add(info.GetArg(i));

        return string.Join(" ", parts).Trim().Trim('"');
    }

    private void OnModeCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (info.ArgCount < 2)
        {
            Reply(player, info, $"[GameManager] Modalità attuale: {_currentMode}");
            return;
        }

        if (!CanManage(player))
        {
            Reply(player, info, "[GameManager] Non hai i permessi.");
            return;
        }

        var requested = info.GetArg(1).Trim().ToLowerInvariant();
        if (_aliasToMode.TryGetValue(requested, out var mapped))
            requested = mapped;
        else if (_aliasToMode.TryGetValue("." + requested, out mapped))
            requested = mapped;

        SwitchMode(requested, player, info);
    }

    private void OnModesCommand(CCSPlayerController? player, CommandInfo info)
    {
        var text = "[GameManager] Modalità: " + string.Join(", ", _config.Modes.Keys);
        Reply(player, info, text);
    }

    private void OnReloadCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!CanManage(player))
        {
            Reply(player, info, "[GameManager] Non hai i permessi.");
            return;
        }

        LoadConfig();
        Reply(player, info, "[GameManager] Config ricaricata.");
    }

    private bool CanManage(CCSPlayerController? player)
    {
        if (!_config.AdminOnly)
            return true;

        if (player == null)
            return true;

        if (!player.IsValid)
            return false;

        return AdminManager.PlayerHasPermissions(player, _config.Permission)
            || AdminManager.PlayerHasPermissions(player, "@css/root");
    }

    private void SwitchMode(
        string modeName,
        CCSPlayerController? player = null,
        CommandInfo? info = null)
    {
        modeName = modeName.Trim().ToLowerInvariant();

        if (!_config.Modes.TryGetValue(modeName, out var mode))
        {
            Reply(player, info, $"[GameManager] Modalità sconosciuta: {modeName}");
            return;
        }

        if (_currentMode.Equals(modeName, StringComparison.OrdinalIgnoreCase)
            && !_config.AllowReapplySameMode)
        {
            Reply(player, info, $"[GameManager] Sei già in modalità {modeName}.");
            return;
        }

        Logger.LogInformation(
            "Switch mode {OldMode} -> {NewMode} requested by {Player}",
            _currentMode,
            modeName,
            player?.PlayerName ?? "SERVER"
        );

        foreach (var command in mode.Commands)
        {
            var cmd = command.Trim();
            if (cmd.Length == 0)
                continue;

            Logger.LogInformation("[{Mode}] exec: {Command}", modeName, cmd);
            Server.ExecuteCommand(cmd);
        }

        _currentMode = modeName;

        var display = string.IsNullOrWhiteSpace(mode.DisplayName)
            ? modeName
            : mode.DisplayName;

        Server.PrintToChatAll($"[GameManager] Modalità cambiata: {display}");
    }

    private void PrintModes(CCSPlayerController player)
    {
        player.PrintToChat(
            "[GameManager] Modalità disponibili: " +
            string.Join(", ", _config.Modes.Keys.Select(x => "." + x))
        );
    }

    private static void Reply(
        CCSPlayerController? player,
        CommandInfo? info,
        string message)
    {
        if (player != null && player.IsValid)
            player.PrintToChat(message);
        else if (info != null)
            info.ReplyToCommand(message);
        else
            Console.WriteLine(message);
    }
}

public sealed class GameManagerConfig
{
    public bool AdminOnly { get; set; } = true;
    public string Permission { get; set; } = "@css/changemap";
    public bool AllowReapplySameMode { get; set; } = true;
    public string InitialMode { get; set; } = "unset";

    public Dictionary<string, ModeConfig> Modes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static GameManagerConfig CreateDefault()
    {
        return new GameManagerConfig
        {
            Modes = new Dictionary<string, ModeConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["1v1"] = new ModeConfig
                {
                    DisplayName = "1v1 Arena",
                    Aliases = [".1v1", ".arena"],
                    Commands =
                    [
                        "css_plugins unload \"MatchZy\"",
                        "css_plugins unload \"RetakesPlugin\"",
                        "css_plugins load ErkutArena",
                        "exec gamemanager/1v1.cfg",
                        "mp_restartgame 1"
                    ]
                },
                ["retake"] = new ModeConfig
                {
                    DisplayName = "Retake",
                    Aliases = [".retake", ".retakes"],
                    Commands =
                    [
                        "css_plugins unload \"MatchZy\"",
                        "css_plugins unload \"ErkutArena\"",
                        "css_plugins load RetakesPlugin",
                        "exec gamemanager/retake.cfg",
                        "mp_restartgame 1"
                    ]
                },
                ["5v5"] = new ModeConfig
                {
                    DisplayName = "5v5",
                    Aliases = [".5v5", ".comp"],
                    Commands =
                    [
                        "css_plugins unload \"RetakesPlugin\"",
                        "css_plugins unload \"ErkutArena\"",
                        "css_plugins load MatchZy",
                        "exec gamemanager/5v5.cfg",
                        "mp_restartgame 1"
                    ]
                },
                ["mix"] = new ModeConfig
                {
                    DisplayName = "Mix / PUG",
                    Aliases = [".mix", ".pug"],
                    Commands =
                    [
                        "css_plugins unload \"RetakesPlugin\"",
                        "css_plugins unload \"ErkutArena\"",
                        "css_plugins load MatchZy",
                        "exec gamemanager/mix.cfg",
                        "mp_restartgame 1"
                    ]
                },
                ["prac"] = new ModeConfig
                {
                    DisplayName = "Practice",
                    Aliases = [".prac", ".practice"],
                    Commands =
                    [
                        "css_plugins unload \"RetakesPlugin\"",
                        "css_plugins unload \"ErkutArena\"",
                        "css_plugins load MatchZy",
                        "exec gamemanager/prac.cfg"
                    ]
                }
            }
        };
    }
}

public sealed class ModeConfig
{
    public string DisplayName { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public List<string> Commands { get; set; } = [];
}
