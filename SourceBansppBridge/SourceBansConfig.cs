using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace SourceBansppBridge;

public class DatabaseConfig
{
    [JsonPropertyName("Address")] public string Address { get; set; } = "127.0.0.1";
    [JsonPropertyName("Port")] public int Port { get; set; } = 3306;
    [JsonPropertyName("Database")] public string Database { get; set; } = "sourcebans";
    [JsonPropertyName("User")] public string User { get; set; } = "user";
    [JsonPropertyName("Password")] public string Password { get; set; } = "password";
}

public class SourceBansConfig : BasePluginConfig
{
	// Website address to tell where the player to go for unban, etc
    [JsonPropertyName("Website")] public string Website { get; set; } = "http://www.yourwebsite.net/";
    // Allow or disallow admins access to addban command
    [JsonPropertyName("AddBab")] public bool AddBab { get; set; } = true;
    // Allow or disallow admins access to unban command
    [JsonPropertyName("Unban")] public bool Unban { get; set; } = true;
    // The Tableprefix you set while installing the webpanel. (default: "sb")
    [JsonPropertyName("DatabasePrefix")] public string DatabasePrefix { get; set; } = "sb";
    // How many seconds to wait before retrying when a players ban fails to be checked. Min = 15.0 Max = 60.0
    [JsonPropertyName("RetryTime")] public float RetryTime { get; set; } = 45f;
    // How often should we process the failed ban queue in minutes
    [JsonPropertyName("ProcessQeuueTime")] public int ProcessQeuueTime { get; set; } = 5;
    // Should the plugin automatically add the server to sourcebans 
    // (servers without -ip being set on startup need this set to 0)
    // (2 = YES ADD WITH RCON, 1 = YES ADD WITHOUT RCON, 0 = DO NOT ADD SERVER)
    [JsonPropertyName("AutoAddServer")] public bool AutoAddServer { get; set; } = true;
    // Enable admin part of the plugin (1 = enabled, 0 = disabled)
    [JsonPropertyName("EnableAdmins")] public bool EnableAdmins { get; set; } = true;
    // Require the admin to login once into website
    [JsonPropertyName("RequireSiteLogin")] public bool RequireSiteLogin { get; set; } = true;
    // This is the ID of this server (Check in the admin panel -> servers to find the ID of this server)
    [JsonPropertyName("ServerID")] public int ServerID { get; set; } = -1;
    [JsonPropertyName("Database")] public DatabaseConfig Database { get; set; } = new();
}