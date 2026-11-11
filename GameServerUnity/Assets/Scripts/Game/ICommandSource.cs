using GameServer;

namespace GameServer
{
    public interface ICommandSource
    {
        CharacterCommand ReadCommand();
        void ResetSource();
    }
}

