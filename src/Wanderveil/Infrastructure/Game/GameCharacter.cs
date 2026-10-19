using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Wanderveil.Features.Passport;

namespace Wanderveil.Infrastructure.Game;

// The logged-in character's name, home world, and data center, read on the game's framework thread
// only when the holder asks to fill them in.
internal sealed class GameCharacter(IFramework framework, IPlayerState player) : ICharacterSource
{
    public Task<CharacterDetails?> Read() => framework.RunOnFrameworkThread(() =>
    {
        if (!player.IsLoaded) return null;
        var world = player.HomeWorld.Value;
        return (CharacterDetails?)new CharacterDetails(player.CharacterName.ToString(), world.Name.ExtractText(), world.DataCenter.Value.Name.ExtractText());
    });
}
