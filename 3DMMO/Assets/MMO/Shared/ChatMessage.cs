using Mirror;

namespace MMO.Shared
{
    // Server → Clients broadcast for chat lines.
    public struct ChatMessage : NetworkMessage
    {
        public string from;
        public string text;
        public double time;
    }
}
