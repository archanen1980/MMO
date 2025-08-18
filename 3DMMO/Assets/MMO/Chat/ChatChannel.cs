namespace MMO.Chat
{
    // Bitmask so tabs/filters can combine channels
    [System.Flags]
    public enum ChatChannel : uint
    {
        None    = 0,
        System  = 1u << 0,
        General = 1u << 1,
        Say     = 1u << 2,
        Whisper = 1u << 3,
        Party   = 1u << 4,
        Guild   = 1u << 5,
        Trade   = 1u << 6,
        Loot    = 1u << 7,
        Combat  = 1u << 8,
        Global  = 1u << 9,

        All     = 0xFFFFFFFF
    }
}
