using System.Text.Json;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;

namespace SourceBansppBridge;

public partial class SourceBansppBridge
{
    public void OnMapStart(string mapName)
    {
        var basePath = Path.Combine(ModuleDirectory, "../../configs/plugins/SourceBansppBridge");
        if(!Directory.Exists(basePath)) return;

        if(File.Exists($"{basePath}/sb_admins.json"))
            AdminManager.LoadAdminData($"{basePath}/sb_admins.json");
           
        if(File.Exists($"{basePath}/sb_groups.json")) 
            AdminManager.LoadAdminGroups($"{basePath}/sb_groups.json");
        
        var port = ConVar.Find("hostport");
        GetServerAddr();
        if (_serverAddress is null or "0.0.0.0")
        {
            AddTimer(1f, () => OnMapStart(mapName), TimerFlags.STOP_ON_MAPCHANGE);
            return;
        }

        _serverPort = port?.GetPrimitiveValue<int>();

        if (_serverAddress is null || _serverPort is null)
        {
            AddTimer(1f, () => OnMapStart(mapName), TimerFlags.STOP_ON_MAPCHANGE);
            return;
        }
           
        Task.Run(async () =>
        {
            await Setup();
        });
    }
    
    private async Task Setup()
    {
        try
        {
            _connection = new Connection(Config.Database.Address, Config.Database.Port, Config.Database.Database,
                Config.Database.User, Config.Database.Password, Logger);

            await _connection.Query<AdminOverrides>($"SELECT type, name, flags FROM {Config.DatabasePrefix}_overrides", ProcessOverrides);
            
            await _connection.Query<AdminGroup>(
                $"SELECT name, flags, immunity, groups_immune FROM {Config.DatabasePrefix}_srvgroups ORDER BY id",  ProcessAdminGroups);
            
            var queryLastLogin = "";
            if (Config.RequireSiteLogin)
                queryLastLogin = "lastvisit IS NOT NULL AND lastvisit != '' AND";
      
            // TODO: Very High Priority  Add Automatic Detection of Which Server is associated with this IP and do corresponding un/banning logic with it.
            if (Config.ServerID == -1)
            {
                if(_serverAddress is null || _serverPort is null)
                    throw new Exception("Server Address or Port is null");
                
                var query = $@"
                        SELECT 
                            authid, 
                            srv_password, 
                            (SELECT name FROM {Config.DatabasePrefix}_srvgroups WHERE name = srv_group AND flags != '') AS srv_group, 
                            srv_flags, 
                            user, 
                            immunity
                        FROM 
                            {Config.DatabasePrefix}_admins_servers_groups AS asg
                        LEFT JOIN 
                            {Config.DatabasePrefix}_admins AS a ON a.aid = asg.admin_id
                        WHERE 
                            {queryLastLogin} (
                                server_id = (
                                    SELECT sid FROM {Config.DatabasePrefix}_servers WHERE ip = @IpAddress AND port = @Port LIMIT 0,1
                                ) OR 
                                srv_group_id = ANY (
                                    SELECT group_id FROM {Config.DatabasePrefix}_servers_groups WHERE server_id = (
                                        SELECT sid FROM {Config.DatabasePrefix}_servers WHERE ip = @IpAddress AND port = @Port LIMIT 0,1
                                    )
                                )
                            )
                        GROUP BY 
                            aid, authid, srv_password, srv_group, srv_flags, user";
                
                var parameters = new { IpAddress = _serverAddress, Port = _serverPort };
                await _connection.Query<Admin>(query, (data) => { Task.Run(async () => { await ProcessAdmins(data); }); }, parameters);
            }
            else
            {
                await _connection.Query<Admin>($"SELECT authid, srv_password, (SELECT name FROM {Config.DatabasePrefix}_srvgroups WHERE name = srv_group AND flags != '') AS srv_group, srv_flags, user, immunity  "+
                                               $"FROM {Config.DatabasePrefix}_admins_servers_groups AS asg "+
                                               $"LEFT JOIN {Config.DatabasePrefix}_admins AS a ON a.aid = asg.admin_id "+
                                               $"WHERE {queryLastLogin} {Config.DatabasePrefix} server_id = {Config.ServerID}  "+
                                               $"OR srv_group_id = ANY (SELECT group_id FROM {Config.DatabasePrefix}_servers_groups WHERE server_id = @ServerId) "+
                                               $"GROUP BY aid, authid, srv_password, srv_group, srv_flags, user", (data) => { Task.Run(async () => { await ProcessAdmins(data); }); }, new {ServerId = Config.ServerID});
            }
        }
        catch (Exception e)
        {
            Logger.LogCritical($"Failed to connect to database please check your config!");
            Logger.LogError(e.Message);
            Logger.LogError(e.StackTrace);
        }
    }

    private void ProcessOverrides(IEnumerable<AdminOverrides> data)
    {
        foreach (var adminOverride in data.Where((d)=> d.type == "command"))
        {
            if (adminOverride.name is null) continue;
            foreach (var flag in adminOverride.flags.ToCharArray())
            {
                AdminManager.AddPermissionOverride(adminOverride.name, _flags[flag]);
            }
        }

        _overridesLoaded = true;
    }

    private void ProcessAdminGroups(IEnumerable<AdminGroup> data)
    {
        _adminGroups = new Dictionary<string, string>();
        var groups = new Dictionary<string, AdminGroupData>();
        foreach (var group in data)
        {
            if(group.name is null) continue;
            var newGroupName = $"#sbpp/{group.name.Replace(" ", "-")}";
            
            _adminGroups.Add(group.name, newGroupName);
            var flags = new HashSet<string>();
            foreach (var flag in group.flags?.ToCharArray() ?? Array.Empty<char>())
            {
                flags.Add(_flags[flag]);
            }
            groups.Add(newGroupName, new AdminGroupData
            {
                Flags = flags,
                CommandOverrides = new Dictionary<string, bool>(),
                Immunity = group.immunity
            });
        }

        var path = Path.Combine(ModuleDirectory, "../../configs/plugins/SourceBansppBridge");
        if(!Directory.Exists(path))
            Directory.CreateDirectory(path);
        path += "/sb_groups.json";
        
        var json = JsonSerializer.Serialize(groups);
        File.WriteAllText(path, json);


        AdminManager.LoadAdminGroups(path);

        _groupsLoaded = true;
    }

    private int _setupAdminRetryCount = 0;
    
    private const int _setupAdminRetryCountMax = 10;
    private async Task ProcessAdmins(IEnumerable<Admin> data)
    {
        if (!_overridesLoaded || !_groupsLoaded)
        {
            if (_setupAdminRetryCount == _setupAdminRetryCountMax) return;
            AddTimer(1f, () => Task.Run(async () => await ProcessAdmins(data)));
            _setupAdminRetryCount++;
            return;
        }

        _setupAdminRetryCount = 0;
        var _adminDict = new Dictionary<string, AdminData>();
        foreach (var admin in data.Where((adm) => adm.authid is not null &&
                                                  (adm.srv_group is not null && adm.srv_group.Length > 0)))
        {
            Logger.LogInformation(JsonSerializer.Serialize(admin));
            var steamid = new SteamID(admin.authid!);
            AdminManager.AddPlayerToGroup(steamid, _adminGroups![admin.srv_group!]);
            var flags = new HashSet<string>();
            foreach (var flag in admin.srv_flags?.ToCharArray() ?? Array.Empty<char>())
            {
                AdminManager.AddPlayerPermissions(steamid, _flags[flag]);
                flags.Add(_flags[flag]);
            }
            if(admin.srv_group is not null)
                AdminManager.AddPlayerToGroup(steamid, admin.srv_group);

            uint immunity = 0;
            if(admin.immunity is not null)
                immunity = (uint) admin.immunity;
            if(AdminManager.GetPlayerAdminData(steamid)?.Immunity < admin.immunity)
                AdminManager.SetPlayerImmunity(steamid, immunity);

            _adminDict.Add(admin.user!, AdminManager.GetPlayerAdminData(steamid)!);
            await Task.Delay(1);
        }
        
        var path = Path.Combine(ModuleDirectory, "../../configs/plugins/SourceBansppBridge");
        if(!Directory.Exists(path))
            Directory.CreateDirectory(path);
        path += "/sb_admins.json";
        
        var json = JsonSerializer.Serialize(_adminDict);
        File.WriteAllText(path, json);
    }
}