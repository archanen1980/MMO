using System.Threading.Tasks;
using MMO.Shared;
namespace MMO.Server.Persistence { public interface IPersistence { System.Threading.Tasks.Task<CharacterData> LoadCharacterAsync(string accountId); System.Threading.Tasks.Task SaveCharacterAsync(string accountId, CharacterData data); } }
