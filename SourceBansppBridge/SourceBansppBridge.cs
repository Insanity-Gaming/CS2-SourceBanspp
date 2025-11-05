using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.CompilerServices;

namespace SourceBansppBridge;

public partial class SourceBansppBridge : BasePlugin, IPluginConfig<SourceBansConfig>
{
    private DatabaseConfig _config;
    public SourceBansConfig Config { get; set; }
    public override string ModuleName { get; } = "SourceBans++ Bridge";
    public override string ModuleVersion { get; } = "1.0";
    public override string ModuleDescription { get; } = "Provides a bridge from the SourceBans++ Web to Server";

    private Connection? _connection = null;

    private bool _overridesLoaded = false;
    private bool _groupsLoaded = false;

    private Dictionary<string, string>? _adminGroups;
    
    private bool _enabled = false;
    private List<AdminData> _admins;

    private string? _serverAddress = null;
    private int? _serverPort = null;
    
    public readonly Dictionary<int, bool> PlayerStatus = new();


    private Dictionary<char, string> _flags = new()
    {
        // Normal Admin Flags
        {'a', "@css/reservation"}, {'b', "@css/generic"}, {'c', "@css/kick"},
        {'d', "@css/ban"}, {'e', "@css/unban"}, {'f', "@css/slay"},
        {'g', "@css/changemap"}, {'h', "@css/cvar"}, {'i', "@css/config"},
        {'j', "@css/chat"}, {'k', "@css/vote"}, {'l', "@css/password"},
        {'m', "@css/rcon"}, {'n', "@css/cheats"}, {'z', "@css/root"},
        // Custom/VIP Flags
        {'o', "@css/vip"}, {'p', "@css/vip2"}, {'q', "@css/vip3"},
        {'r', "@css/vip4"}, {'s', "@css/vip5"}, {'t', "@css/vip6"}
    };
    

    public override void Load(bool hotReadload)
    {
       RegisterListener<Listeners.OnMapStart>(OnMapStart);
       
       RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
       AddTimer(5f, () =>
       {
           foreach (var player in Utilities.GetPlayers())
           {
               OnClientAuthorized(player.Slot, new SteamID(player.SteamID));
           }
       });
    }

    private string GetControllerIpAddress(CCSPlayerController controller) => controller.IpAddress?.Split(':')[0] ?? "error";

    private void OnClientAuthorized(int playerslot, SteamID steamid)
    {
        if (steamid.SteamId2[0] == 'B' || steamid.SteamId2[9] == 'L')
        {
            PlayerStatus[playerslot] = true;
            return;
        }

        var controller = Utilities.GetPlayerFromSlot(playerslot);
        if (controller is null) return;
        var ip = GetControllerIpAddress(controller);
        var steamid2 = steamid.SteamId2.Substring(8);
        // Logger.LogInformation($"Checkign to see if {controller.PlayerName} is banned with IP {ip} or steamid {steamid2}");
        Task.Run(async () =>
        {
            await _connection.Query<BanData>(
                $"SELECT bid, ip FROM {Config.DatabasePrefix}_bans WHERE ((type = 0 AND (authid REGEXP '^STEAM_[0-9]:{steamid2}$' OR ip = @Address)) OR (type = 1 AND ip = @Address)) AND (length = '0' OR ends > UNIX_TIMESTAMP()) AND RemoveType IS NULL",
                (data) => { Server.NextFrame(() => CheckBan(playerslot, data)); }, new { Address = ip });
        });
    }

    private void CheckBan(int playerSlot, IEnumerable<BanData> data)
    {
        var ban = data.FirstOrDefault();
        if (ban is null)
        {
            var tmp = Utilities.GetPlayerFromSlot(playerSlot);

            // Logger.LogInformation($"{tmp?.PlayerName ?? playerSlot.ToString()} has no active bans");

            PlayerStatus[playerSlot] = true;
            return;
        }
        
        var controller = Utilities.GetPlayerFromSlot(playerSlot);
        if (controller is null) return;

        // Logger.LogInformation($"{controller.PlayerName} has a registered ban")
;
        var name = controller.PlayerName;
        var steamid = new SteamID(controller.SteamID).SteamId2.Substring(8);
        var ip = GetControllerIpAddress(controller);
        
        if (!string.IsNullOrEmpty(ip) && !ip.Equals(ban.ip))
        {
            Task.Run(async () =>
            {
                await _connection.Execute(
                   $"UPDATE {Config.DatabasePrefix}_bans SET `ip` = @Address WHERE `bid` = @Bid",
                    new {Address = ip, Bid = ban.bid});
            });
        }

        if (Config.ServerID == -1)
        {
            Task.Run(async () =>
            {
                await _connection.Execute(
                    $"NSERT INTO {Config.DatabasePrefix}_banlog (sid ,time ,name ,bid) VALUES  ((SELECT sid FROM {Config.DatabasePrefix}_servers WHERE ip = @ServerAddress AND port = @ServerPort LIMIT 0,1), UNIX_TIMESTAMP(), @Name, (SELECT bid FROM {Config.DatabasePrefix}_bans WHERE ((type = 0 AND authid REGEXP '^STEAM_[0-9]:@SteamId') OR (type = 1 AND ip = @Address)) AND RemoveType IS NULL LIMIT 0,1))",
                    new { ServerAddress = _serverAddress, ServerPort = _serverPort, Name = name, SteamId = steamid, Address = ip });
            });
        }
        else
        {
            Task.Run(async () =>
            {
                await _connection.Execute(
                    $"NSERT INTO {Config.DatabasePrefix}_banlog (sid ,time ,name ,bid) VALUES ({Config.ServerID}, UNIX_TIMESTAMP(), @Name, (SELECT bid FROM {Config.DatabasePrefix}_bans WHERE ((type = 0 AND authid REGEXP '^STEAM_[0-9]:@SteamId') OR (type = 1 AND ip = @Address)) AND RemoveType IS NULL LIMIT 0,1))",
                    new { Name = name, SteamId = steamid, Address = ip });
            });
        }
        Server.ExecuteCommand($"banid 5 {controller.UserId}");
        controller.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKBANADDED);
        // Server.ExecuteCommand($"kickid {controller.UserId} You have been banned from this server. Access {Config.Website} for more info.");
    }
    
    public void CreateBan(CCSPlayerController target, CCSPlayerController? admin, uint time, string reason)
    {
        string adminIp = "";
        string adminAuth = "";

        if (admin is null)
        {
            adminIp = _serverAddress ?? "";
            adminAuth = "SERVER";
        }
        else
        {
            adminIp = GetControllerIpAddress(admin);
            adminAuth = new SteamID(admin.SteamID).SteamId2;
        }
        
        var ip = GetControllerIpAddress(target);
        var auth = new SteamID(target.SteamID).SteamId2;
        var name = target.PlayerName;
        InsertBan(time, name, auth, ip, reason, adminAuth, adminIp, admin, target);
    }
    
    // private string GetControllerIpAddress(CCSPlayerController? controller)
    // {
    //     var ip = controller?.IpAddress ?? "";
    //     if (ip.Length > 0)
    //         ip = ip.Split(':')[0];
    //     return ip;
    // }
    
    private void InsertBan(uint time, string name, string auth, string ip, string reason, string adminAuth, string adminIp, CCSPlayerController? admin, CCSPlayerController target)
    {
        string sql = "";
        if (Config.ServerID == -1)
        {
            sql =
                $@"INSERT INTO {Config.DatabasePrefix}_bans (ip, authid, name, created, ends, length, reason, aid, adminIp, sid, country) VALUES 
						(@Address, @AuthId, @Name, UNIX_TIMESTAMP(), UNIX_TIMESTAMP() + @Length, @Length, @Reason, IFNULL((SELECT aid FROM {Config.DatabasePrefix}_admins WHERE authid = @AdminAuth OR authid REGEXP '^STEAM_[0-9]:@SteamId$'),'0'), @AdminIp,
						(SELECT sid FROM {Config.DatabasePrefix}_servers WHERE ip = @ServerIP AND port = @ServerPort LIMIT 0,1), ' ')";
        }
        else
        {
            sql =
                $@"INSERT INTO {Config.DatabasePrefix}_bans (ip, authid, name, created, ends, length, reason, aid, adminIp, sid, country) VALUES 
						(@Address, @AuthId, @Name, UNIX_TIMESTAMP(), UNIX_TIMESTAMP() + @Length, @Length, @Reason, IFNULL((SELECT aid FROM {Config.DatabasePrefix}_admins WHERE authid = @AdminAuth OR authid REGEXP '^STEAM_[0-9]:@SteamId$'),'0'), @AdminIp, {Config.ServerID}";
        }
        

        Task.Run(async () =>
        {
            await _connection.Execute(sql, new
            {
                Address = ip,
                AuthId = auth,
                Name = name,
                Length = time * 60,
                Reason = reason,
                AdminAuth = adminAuth,
                AdminIp = adminIp,
                ServerIP = _serverAddress,
                ServerPort = _serverPort
            });
            
            Server.NextFrame(() =>
            {
                if (time == 0)
                {
                    if(string.IsNullOrEmpty(reason))
                        ShowActivity(admin, $"has Permanently banned {name}");
                    else
                        ShowActivity(admin, $"has Permanently banned {name}. Reason: {reason}");
                }
                else
                {
                    var timeString = ConvertMinutesToTime(time);
                    if(string.IsNullOrEmpty(reason))
                        ShowActivity(admin, $"has banned {name} for {timeString}.");
                    else
                        ShowActivity(admin, $"has banned {name} for {timeString}. Reason: {reason}");
                }
        
                var adminName = admin is null ? "Console" : admin.PlayerName;
        
                var kickReason = $"Admin: {adminName}\nReason: {reason}\nLength: {(time == 0 ? "Permanently" : ConvertMinutesToTime(time))}\nAppeal @: {Config.Website}";
                if (target.IsValid)
                {
                    Server.ExecuteCommand($"kickid {target.UserId} {kickReason}");
                }
            });
        });
    }
    
    public string ConvertMinutesToTime(uint totalMinutes)
    {
        var years = totalMinutes / (365 * 24 * 60); // Calculate the number of whole years
        var remainingMinutes = totalMinutes % (365 * 24 * 60); // Calculate the remaining minutes

        var months = remainingMinutes / (30 * 24 * 60); // Calculate the number of whole months
        remainingMinutes %= (30 * 24 * 60); // Calculate the remaining minutes

        var weeks = remainingMinutes / (7 * 24 * 60); // Calculate the number of whole weeks
        remainingMinutes %= (7 * 24 * 60); // Calculate the remaining minutes

        var days = remainingMinutes / (24 * 60); // Calculate the number of whole days
        remainingMinutes %= (24 * 60); // Calculate the remaining minutes

        var hours = remainingMinutes / 60; // Calculate the number of whole hours
        remainingMinutes %= 60; // Calculate the remaining minutes

        // Construct the result string
        string result = "";
        
        if (years > 0)
        {
            result += $"{years} year{(years > 1 ? "s" : "")}"; // Add years to the result
            if (months > 0 || weeks > 0 || days > 0 || hours > 0 || remainingMinutes > 0)
                result += ", "; // Add ", " if there are remaining units
        }
        
        if (months > 0)
        {
            result += $"{months} month{(months > 1 ? "s" : "")}"; // Add months to the result
            if (weeks > 0 || days > 0 || hours > 0 || remainingMinutes > 0)
                result += ", "; // Add ", " if there are remaining units
        }
        
        if (weeks > 0)
        {
            result += $"{weeks} week{(weeks > 1 ? "s" : "")}"; // Add weeks to the result
            if (days > 0 || hours > 0 || remainingMinutes > 0)
                result += ", "; // Add ", " if there are remaining units
        }
        
        if (days > 0)
        {
            result += $"{days} day{(days > 1 ? "s" : "")}"; // Add days to the result
            if (hours > 0 || remainingMinutes > 0)
                result += ", "; // Add ", " if there are remaining units
        }

        if (hours > 0)
        {
            result += $"{hours} hour{(hours > 1 ? "s" : "")}"; // Add hours to the result
            if (remainingMinutes > 0)
                result += ", "; // Add ", " if there are remaining units
        }

        if (remainingMinutes > 0)
        {
            result += $"{remainingMinutes} minute{(remainingMinutes > 1 ? "s" : "")}"; // Add remaining minutes to the result
        }

        return result;
    }
    
    public void ShowActivity(CCSPlayerController? controller, string message)
    {
        var name = controller is null ? "Server" : controller.PlayerName;
        Server.PrintToChatAll($" {ChatColors.LightRed}[IG]{ChatColors.Default} {name} {message}");
    }

    public void OnConfigParsed(SourceBansConfig config)
    {
        Config = config;
        
        var port = ConVar.Find("hostport");
        GetServerAddr();
        if (_serverAddress is null or "0.0.0.0")
        {
            AddTimer(30f, () => { OnConfigParsed(config); });
            return;
        }

        _serverPort = port?.GetPrimitiveValue<int>();
        if (Config.ServerID == -1 && _serverAddress?.Length < 1 || _serverPort is null)
        {
            Logger.LogCritical($"Automatic finding of server id is currently not working please manully set ServerID");
            Server.ExecuteCommand("css_plugins unload SourceBansppBridge");
            return;
        }

        Task.Run(async () =>
        {
            await Setup();
        });
    }

    private delegate nint CNetworkSystem_UpdatePublicIp(nint a1);
    private CNetworkSystem_UpdatePublicIp? _networkSystemUpdatePublicIp;
    private void GetServerAddr()
    {
        var network_system = NativeAPI.GetValveInterface(0, "NetworkSystemVersion001");
        unsafe
        {
            if (_networkSystemUpdatePublicIp == null)
            {
                var funcPtr = *(nint*)(*(nint*)(network_system) + 256);
                _networkSystemUpdatePublicIp = Marshal.GetDelegateForFunctionPointer<CNetworkSystem_UpdatePublicIp>(funcPtr);
            }
            /*
            struct netadr_t
            {
               uint32_t type
               uint8_t ip[4]
               uint16_t port
            }
            */
            // + 4 to skip type, because the size of uint32_t is 4 bytes
            var ipBytes = (byte*)(_networkSystemUpdatePublicIp(network_system) + 4);
    // port is always 0, use the one from convar "hostport"
           _serverAddress = $"{ipBytes[0]}.{ipBytes[1]}.{ipBytes[2]}.{ipBytes[3]}";
        }
    }
}