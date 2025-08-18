using System;

namespace MMO.Chat
{
    [Serializable]
    public struct ChatMessage
    {
        public ChatChannel channel;
        public string from;   // display name
        public string to;     // optional (whispers)
        public string text;

        public long unixTimeMs; // server timestamp (UTC millis)
    }
}
