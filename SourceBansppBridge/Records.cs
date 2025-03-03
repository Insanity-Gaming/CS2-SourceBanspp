namespace SourceBansppBridge;

public record AdminOverrides(string? type, string? name, string flags);

public record AdminGroup(string? name, string? flags, uint immunity, string? groups_immune);

public record Admin(string? authid, string? srv_password, string? srv_group, string? srv_flags, string? user,
    int? immunity);
public record BanData(int bid, string? ip);