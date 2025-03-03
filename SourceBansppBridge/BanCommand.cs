using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Utils;

namespace SourceBansppBridge;

public partial class SourceBansppBridge
{
    [ConsoleCommand("css_ban", "Bans a player from the server.")]
    [RequiresPermissions("@css/ban")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER, usage: "[target] [time|0] (reason)", minArgs: 2)]
    public void OnCommandExecuted(CCSPlayerController? controller, CommandInfo command)
    {
        if (command.ArgCount < 4) return;
        var admin = controller;
        var targets = new Target(command.GetArg(1)).GetTarget(admin).Players.ToList();
        if (targets.Count > 1)
        {
            command.ReplyToCommand($" {ChatColors.LightRed}[IG]{ChatColors.Default} Multiple targets found. Please specify a single target.");
            return;
        }
        var target = targets.First()!;
        if (!AdminManager.CanPlayerTarget(admin, target))
        {
            command.ReplyToCommand($"{ChatColors.LightRed}[IG]{ChatColors.Default} You do not have permission to target this player.");
            return;
        }

        uint time = 0;
        string reason = "No reason provided.";;
        if(command.ArgCount > 2)
        {
            try
            {
                time = uint.Parse(command.GetArg(2));
            }
            catch
            {
                command.ReplyToCommand($"{ChatColors.LightRed}[IG]{ChatColors.Default} Invalid time format.");
                return;
            }
            reason = command.GetArg(3);
            if (string.IsNullOrEmpty(reason))
            {
                reason = "No reason provided.";
            }
        }
        
        if(time == 0 && (admin is not null && !AdminManager.PlayerHasPermissions(admin, "@css/unban")))
        {
            command.ReplyToCommand($"{ChatColors.LightRed}[IG]{ChatColors.Default} You do not have permission to ban permanently.");
            return;
        }

        if (!PlayerStatus[target.Slot])
        {
            command.ReplyToCommand($"{ChatColors.LightRed}[IG]{ChatColors.Default} Player is not connected.");
            return;
        }
        CreateBan(target, admin, time, reason);
    }
}