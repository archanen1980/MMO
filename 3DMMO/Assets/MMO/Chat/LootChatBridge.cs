using System;
using UnityEngine;
using MMO.Chat; // ChatChannel, ChatClient

namespace MMO.Chat
{
    /// <summary>
    /// Posts a local Loot-channel message with a TMP <link> for an item.
    /// Link ID is "item:{itemId}". Name is non-breaking and styled like a link.
    /// </summary>
    public static class LootChatBridge
    {
        /// <summary>
        /// Example: You have received <link="item:1234"><color=#68C1FF><u><b>Sword</b></u></color></link> x3.
        /// </summary>
        public static void PostLootReceived(string itemId, string itemName, int amount)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return;

            string safeName = EscapeRichText(itemName ?? itemId);

            // Keep the whole name on one line: space -> NBSP, hyphen -> NON-BREAKING HYPHEN
            safeName = safeName.Replace(" ", "\u00A0").Replace("-", "\u2011");

            // Make it look like a link (color + underline). You can change the hex if you like.
            string linkStyledName = $"<link=\"item:{itemId}\"><color=#68C1FF><u><b>{safeName}</b></u></color></link>";

            string text = amount > 1
                ? $"You have received a {linkStyledName} x{amount}."
                : $"You have received a {linkStyledName}.";

            if (TryRaiseLocal(ChatChannel.Loot, text)) return;

            try { ChatClient.Send(ChatChannel.Loot, text); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // --- internals ---

        static bool TryRaiseLocal(object channelEnum, string text)
        {
            try
            {
                var chatClientT = Type.GetType("MMO.Chat.ChatClient, Assembly-CSharp", false);
                var chatMsgT = Type.GetType("MMO.Chat.ChatMessage, Assembly-CSharp", false);
                if (chatClientT == null || chatMsgT == null) return false;

                string[] methodNames = { "PostLocal", "PushLocal", "EmitLocal", "ReceiveLocal", "AddLocal" };
                foreach (var name in methodNames)
                {
                    var m = chatClientT.GetMethod(
                        name,
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                        null,
                        new[] { typeof(Enum), typeof(string) }, null);
                    if (m != null) { m.Invoke(null, new[] { channelEnum, text }); return true; }
                }

                var onMsgField = chatClientT.GetField("OnMessage",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var del = onMsgField?.GetValue(null) as Delegate;
                if (del != null)
                {
                    object msg = Activator.CreateInstance(chatMsgT);
                    Set(chatMsgT, msg, "channel", channelEnum);
                    Set(chatMsgT, msg, "text", text);
                    Set(chatMsgT, msg, "from", "");
                    Set(chatMsgT, msg, "unixTimeMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    del.DynamicInvoke(msg);
                    return true;
                }
            }
            catch { /* ignore */ }

            return false;
        }

        static void Set(Type t, object obj, string name, object val)
        {
            const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var f = t.GetField(name, BF);
            if (f != null) { f.SetValue(obj, val); return; }
            var p = t.GetProperty(name, BF);
            if (p != null && p.CanWrite) p.SetValue(obj, val);
        }

        static string EscapeRichText(string s)
            => s?.Replace("<", "&lt;").Replace(">", "&gt;") ?? "";
    }
}
