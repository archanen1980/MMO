using System.IO;
using System.Threading.Tasks;
using MMO.Shared;
using UnityEngine;

namespace MMO.Server.Persistence
{
    public class JsonPersistence : IPersistence
    {
        readonly string _root;
        public JsonPersistence(string folderName = "MMO_Save")
        {
            _root = Path.Combine(Application.persistentDataPath, folderName);
            Directory.CreateDirectory(_root);
        }

        string PathFor(string id) => Path.Combine(_root, $"{Sanitize(id)}.json");
        static string Sanitize(string s){ foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c,'_'); return s.Trim(); }

        public async Task<CharacterData> LoadCharacterAsync(string id)
        {
            string p = PathFor(id);
            if (!File.Exists(p))
            {
                return await Task.FromResult(new CharacterData{ characterName = id, position = Vector3.zero, yaw = 0f });
            }
            string json = await File.ReadAllTextAsync(p);
            return JsonUtility.FromJson<CharacterData>(json);
        }

        public async Task SaveCharacterAsync(string id, CharacterData data)
        {
            string p = PathFor(id);
            string json = JsonUtility.ToJson(data, true);
            await File.WriteAllTextAsync(p, json);
        }
    }
}
