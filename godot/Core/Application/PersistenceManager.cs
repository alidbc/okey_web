using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace OkieRummyGodot.Core.Application
{
    public static class PersistenceManager
    {
        private static readonly string BaseDir = "user://matches";
        private static readonly string FriendsPath = "user://friends.json";

        static PersistenceManager()
        {
            if (!DirAccess.DirExistsAbsolute(BaseDir))
            {
                DirAccess.MakeDirRecursiveAbsolute(BaseDir);
            }
        }

        public static void SaveMatch(string matchId, object matchData)
        {
            try
            {
                string path = $"{BaseDir}/{matchId}.json";
                string json = JsonSerializer.Serialize(matchData, new JsonSerializerOptions { WriteIndented = true });
                using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
                if (file != null)
                {
                    file.StoreString(json);
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"PersistenceManager: Error saving match {matchId}: {e.Message}");
            }
        }

        public static string LoadMatch(string matchId)
        {
            string path = $"{BaseDir}/{matchId}.json";
            if (!Godot.FileAccess.FileExists(path)) return null;

            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            return file?.GetAsText();
        }

        public static List<string> GetAllMatchIds()
        {
            var ids = new List<string>();
            var dir = DirAccess.Open(BaseDir);
            if (dir != null)
            {
                dir.ListDirBegin();
                string fileName = dir.GetNext();
                while (fileName != "")
                {
                    if (!dir.CurrentIsDir() && fileName.EndsWith(".json"))
                    {
                        ids.Add(fileName.Replace(".json", ""));
                    }
                    fileName = dir.GetNext();
                }
            }
            return ids;
        }

        public static void SaveFriends(List<Domain.Friend> friends)
        {
            try
            {
                string json = JsonSerializer.Serialize(friends, new JsonSerializerOptions { WriteIndented = true });
                using var file = Godot.FileAccess.Open(FriendsPath, Godot.FileAccess.ModeFlags.Write);
                file?.StoreString(json);
            }
            catch (Exception e)
            {
                GD.PrintErr($"PersistenceManager: Error saving friends: {e.Message}");
            }
        }

        public static List<Domain.Friend> LoadFriends()
        {
            if (!Godot.FileAccess.FileExists(FriendsPath)) return new List<Domain.Friend>();

            try
            {
                using var file = Godot.FileAccess.Open(FriendsPath, Godot.FileAccess.ModeFlags.Read);
                string json = file?.GetAsText();
                return JsonSerializer.Deserialize<List<Domain.Friend>>(json) ?? new List<Domain.Friend>();
            }
            catch
            {
                return new List<Domain.Friend>();
            }
        }
    }
}
