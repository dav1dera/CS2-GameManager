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
    public override string ModuleVersion => "0.2.0";
    public override string ModuleAuthor => "dav1dera";
    public override string ModuleDescription => "CS2 mode switcher, bot manager, map helper and contextual command help.";

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

        AddCommand("css_help", "Show GameManager and mode-specific help.", OnHelpCommand);
        AddCommand("css_bots", "Bot manager: !bots off | !bots auto [target].", OnBotsCommand);
        AddCommand("css_gmap", "Change a normal/local map: !gmap <map>.", OnMapCommand);
        AddCommand("css_maps", "List maps known to the server: !maps [page].", OnMapsCommand);
        AddCommand("css_wsmap", "Download/load Workshop map by ID: !wsmap <id>.", OnWorkshopMapCommand);
        AddCommand("css_wmap", "Change to downloaded Workshop map by name: !wmap <name>.", OnWorkshopChangeCommand);
        AddCommand("css_wsmaps", "List downloaded Workshop maps in server console.", OnWorkshopListCommand);

        Logger.LogInformation("GameManager {Version} loaded. Config: {ConfigPath}", ModuleVersion, _configPath);
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
            return HookResult.Continue;

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

    private void OnBotsCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!CanManage(player))
        {
            Reply(player, info, "[GameManager] Non hai i permessi.");
            return;
        }

        if (info.ArgCount < 2)
        {
            Reply(player, info, $"[GameManager] Uso: !bots off | !bots auto [target]. Default auto: {_config.ArenaBotFillTarget}");
            return;
        }

        var action = info.GetArg(1).Trim().ToLowerInvariant();

        if (action is "off" or "0" or "kick")
        {
            Server.ExecuteCommand("bot_auto_vacate 1");
            Server.ExecuteCommand("bot_quota 0");
            Server.ExecuteCommand("bot_kick");
            Reply(player, info, "[GameManager] Bot disattivati e rimossi.");
            return;
        }

        if (action is "auto" or "fill" or "on")
        {
            var target = _config.ArenaBotFillTarget;
            if (info.ArgCount >= 3 && int.TryParse(info.GetArg(2), out var requested))
                target = Math.Clamp(requested, 1, 64);

            ConfigureAutoBots(target);
            Reply(player, info, $"[GameManager] Bot AUTO: target totale {target}; i bot liberano slot ai player veri.");
            return;
        }

        Reply(player, info, "[GameManager] Uso: !bots off | !bots auto [target]");
    }

    private void ConfigureAutoBots(int target)
    {
        target = Math.Clamp(target, 1, 64);
        Server.ExecuteCommand("bot_auto_vacate 1");
        Server.ExecuteCommand("bot_quota_mode fill");
        Server.ExecuteCommand($"bot_quota {target}");
        Server.ExecuteCommand("bot_join_after_player 0");
        Server.ExecuteCommand("bot_join_delay 0");
    }

    private void OnMapCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!CanManage(player))
        {
            Reply(player, info, "[GameManager] Non hai i permessi.");
            return;
        }

        if (info.ArgCount < 2)
        {
            Reply(player, info, "[GameManager] Uso: !gmap <nome_mappa>");
            return;
        }

        var map = info.GetArg(1).Trim();
        if (!IsSafeMapToken(map))
        {
            Reply(player, info, "[GameManager] Nome mappa non valido.");
            return;
        }

        if (!Server.IsMapValid(map))
        {
            Reply(player, info, $"[GameManager] Mappa non riconosciuta: {map}. Per Workshop usa !wsmap <ID> o !wmap <nome>.");
            return;
        }

        Server.ExecuteCommand($"changelevel {map}");
    }

    private void OnMapsCommand(CCSPlayerController? player, CommandInfo info)
    {
        var page = 1;
        if (info.ArgCount >= 2 && int.TryParse(info.GetArg(1), out var requestedPage))
            page = Math.Max(1, requestedPage);

        var maps = Server.GetMapList()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (maps.Length == 0)
        {
            Reply(player, info, "[GameManager] Nessuna mappa restituita dal server.");
            return;
        }

        const int pageSize = 10;
        var totalPages = (int)Math.Ceiling(maps.Length / (double)pageSize);
        page = Math.Min(page, totalPages);

        var slice = maps.Skip((page - 1) * pageSize).Take(pageSize);
        Reply(player, info, $"[GameManager] Mappe {page}/{totalPages}: {string.Join(", ", slice)}");
    }

    private void OnWorkshopMapCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!CanManage(player))
        {
            Reply(player, info, "[GameManager] Non hai i permessi.");
            return;
        }

        if (info.ArgCount < 2 || !ulong.TryParse(info.GetArg(1), out var workshopId) || workshopId == 0)
        {
            Reply(player, info, "[GameManager] Uso: !wsmap <Workshop ID numerico>");
            return;
        }

        Reply(player, info, $"[GameManager] Carico Workshop item {workshopId}. Al primo avvio può dover essere scaricato.");
        Server.ExecuteCommand($"host_workshop_map {workshopId}");
    }

    private void OnWorkshopChangeCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!CanManage(player))
        {
            Reply(player, info, "[GameManager] Non hai i permessi.");
            return;
        }

        if (info.ArgCount < 2)
        {
            Reply(player, info, "[GameManager] Uso: !wmap <nome restituito da ds_workshop_listmaps>");
            return;
        }

        var map = info.GetArg(1).Trim();
        if (!IsSafeMapToken(map))
        {
            Reply(player, info, "[GameManager] Nome Workshop non valido.");
            return;
        }

        Server.ExecuteCommand($"ds_workshop_changelevel {map}");
    }

    private void OnWorkshopListCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!CanManage(player))
        {
            Reply(player, info, "[GameManager] Non hai i permessi.");
            return;
        }

        Server.ExecuteCommand("ds_workshop_listmaps");
        Reply(player, info, "[GameManager] Lista Workshop richiesta: guarda la console server. I nomi mostrati sono utilizzabili con !wmap <nome>.");
    }

    private void OnHelpCommand(CCSPlayerController? player, CommandInfo info)
    {
        var section = info.ArgCount >= 2
            ? info.GetArg(1).Trim().ToLowerInvariant()
            : "summary";

        switch (section)
        {
            case "all":
                PrintHelpSummary(player, info);
                PrintHelpServer(player, info);
                PrintHelpMatch(player, info);
                PrintHelpPractice(player, info);
                PrintHelpRetake(player, info);
                PrintHelpArena(player, info);
                break;
            case "server":
            case "admin":
            case "map":
            case "maps":
                PrintHelpServer(player, info);
                break;
            case "match":
            case "5v5":
            case "mix":
            case "matchzy":
                PrintHelpMatch(player, info);
                break;
            case "prac":
            case "practice":
                PrintHelpPractice(player, info);
                break;
            case "retake":
            case "retakes":
                PrintHelpRetake(player, info);
                break;
            case "1v1":
            case "arena":
                PrintHelpArena(player, info);
                break;
            default:
                PrintHelpSummary(player, info);
                break;
        }
    }

    private void PrintHelpSummary(CCSPlayerController? player, CommandInfo info)
    {
        Reply(player, info, "[HELP] Modalità: .1v1 | .retake | .5v5 | .mix | .prac | .mode | .modes");
        Reply(player, info, "[HELP] Dettagli: !help server | !help match | !help prac | !help retake | !help 1v1 | !help all");
    }

    private void PrintHelpServer(CCSPlayerController? player, CommandInfo info)
    {
        Reply(player, info, "[HELP server] !bots off | !bots auto [N] | !gmap <map> | !maps [pagina]");
        Reply(player, info, "[HELP workshop] !wsmap <ID> | !wsmaps | !wmap <nome> | console: ds_workshop_listmaps");
        Reply(player, info, "[HELP manager] .mode | .modes | .gmreload | css_mode <mode>");
    }

    private void PrintHelpMatch(CCSPlayerController? player, CommandInfo info)
    {
        Reply(player, info, "[HELP MatchZy] .ready/.r | .unready/.ur | .pause | .tech | .unpause | .tac | .stay | .switch/.swap | .stop");
        Reply(player, info, "[HELP MatchZy] .coach <t|ct> | .uncoach | admin: .start | .restart | .forcepause/.fp | .forceunpause/.fup | .restore <round>");
        Reply(player, info, "[HELP MatchZy] admin: .skipveto/.sv | .roundknife/.rk | .playout | .whitelist | .readyrequired <N> | .settings");
        Reply(player, info, "[HELP MatchZy] admin: .map <map> | .asay <msg> | .reload_admins | .team1 <nome> | .team2 <nome> | .prac | .rcon <cmd>");
    }

    private void PrintHelpPractice(CCSPlayerController? player, CommandInfo info)
    {
        Reply(player, info, "[HELP prac 1/4] .spawn N | .ctspawn/.cts N | .tspawn/.ts N | .bestspawn | .worstspawn | .bestctspawn | .worstctspawn | .besttspawn | .worsttspawn");
        Reply(player, info, "[HELP prac 2/4] .showspawns | .hidespawns | .bot | .crouchbot/.cbot | .boost | .crouchboost | .ct | .t | .spec | .fas/.watchme | .nobots");
        Reply(player, info, "[HELP prac 3/4] .clear | .fastforward/.ff | .noflash/.noblind | .dryrun/.dry | .god | .break | .rethrow/.rt | .timer | .last | .back N | .delay SEC");
        Reply(player, info, "[HELP prac 4/4] .throwindex ... | .lastindex | .rethrowsmoke | .rethrownade | .rethrowflash | .rethrowmolotov | .rethrowdecoy | .solid | .impacts | .traj | .exitprac");
        Reply(player, info, "[HELP nades] .savenade/.sn | .loadnade | .deletenade/.dn | .importnade/.in | .listnades/.lin");
    }

    private void PrintHelpRetake(CCSPlayerController? player, CommandInfo info)
    {
        Reply(player, info, "[HELP retake] !voices | admin: !forcebombsite <A|B> | !forcebombsitestop | !scramble/!scrambleteams");
        Reply(player, info, "[HELP retake spawns] !showspawns/!spawns/!edit <A|B> | !addspawn/!add/!newspawn/!new <CT|T> <Y|N> [luogo]");
        Reply(player, info, "[HELP retake spawns] !removespawn/!remove/!deletespawn/!delete | !nearestspawn/!nearest | !hidespawns/!done/!exitedit");
        Reply(player, info, "[HELP retake cfg] !mapconfig/!setmapconfig/!loadmapconfig <file> | !mapconfigs/!viewmapconfigs/!listmapconfigs");
    }

    private void PrintHelpArena(CCSPlayerController? player, CommandInfo info)
    {
        Reply(player, info, "[HELP 1v1] .1v1/.arena attiva ErkutArena. GameManager imposta bot AUTO e bot_auto_vacate=1 per liberare slot ai player veri.");
        Reply(player, info, "[HELP 1v1] I comandi nativi di ErkutArena non sono pubblicamente documentati nel pacchetto rilevato; !bots off rimuove tutti i bot, !bots auto [N] li ripristina.");
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

            var expanded = cmd.Replace("{ArenaBotFillTarget}", _config.ArenaBotFillTarget.ToString());
            Logger.LogInformation("[{Mode}] exec: {Command}", modeName, expanded);
            Server.ExecuteCommand(expanded);
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

    private static bool IsSafeMapToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            return false;

        return value.All(ch =>
            char.IsLetterOrDigit(ch) || ch is '_' or '-' or '/' or '.');
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
    public int ArenaBotFillTarget { get; set; } = 12;

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
                        "css_plugins unload plugins/MatchZy/MatchZy.dll",
                        "css_plugins unload optional/RetakesPlugin/RetakesPlugin.dll",
                        "css_plugins load optional/ErkutArena/ErkutArena.dll",
                        "bot_auto_vacate 1",
                        "bot_quota_mode fill",
                        "bot_quota {ArenaBotFillTarget}",
                        "bot_join_after_player 0",
                        "bot_join_delay 0",
                        "mp_restartgame 1"
                    ]
                },
                ["retake"] = new ModeConfig
                {
                    DisplayName = "Retake",
                    Aliases = [".retake", ".retakes"],
                    Commands =
                    [
                        "css_plugins unload plugins/MatchZy/MatchZy.dll",
                        "css_plugins unload optional/ErkutArena/ErkutArena.dll",
                        "bot_quota 0",
                        "bot_kick",
                        "css_plugins load optional/RetakesPlugin/RetakesPlugin.dll",
                        "mp_restartgame 1"
                    ]
                },
                ["5v5"] = new ModeConfig
                {
                    DisplayName = "5v5",
                    Aliases = [".5v5", ".comp"],
                    Commands =
                    [
                        "css_plugins unload optional/RetakesPlugin/RetakesPlugin.dll",
                        "css_plugins unload optional/ErkutArena/ErkutArena.dll",
                        "bot_quota 0",
                        "bot_kick",
                        "css_plugins load plugins/MatchZy/MatchZy.dll",
                        "mp_restartgame 1"
                    ]
                },
                ["mix"] = new ModeConfig
                {
                    DisplayName = "Mix / PUG",
                    Aliases = [".mix", ".pug"],
                    Commands =
                    [
                        "css_plugins unload optional/RetakesPlugin/RetakesPlugin.dll",
                        "css_plugins unload optional/ErkutArena/ErkutArena.dll",
                        "bot_quota 0",
                        "bot_kick",
                        "css_plugins load plugins/MatchZy/MatchZy.dll",
                        "mp_restartgame 1"
                    ]
                },
                ["prac"] = new ModeConfig
                {
                    DisplayName = "Practice",
                    Aliases = [".prac", ".practice"],
                    Commands =
                    [
                        "css_plugins unload optional/RetakesPlugin/RetakesPlugin.dll",
                        "css_plugins unload optional/ErkutArena/ErkutArena.dll",
                        "bot_quota 0",
                        "bot_kick",
                        "css_plugins load plugins/MatchZy/MatchZy.dll",
                        "css_prac"
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
