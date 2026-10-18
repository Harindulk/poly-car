using System;
using UnityEngine;

namespace PolyCar
{
    [CreateAssetMenu(menuName = "Poly Car/Map Catalog")]
    public class MapCatalog : ScriptableObject
    {
        public MapSpec[] maps = Array.Empty<MapSpec>();

        private static MapCatalog _instance;
        public static MapCatalog Instance => _instance ? _instance : (_instance = Resources.Load<MapCatalog>("MapCatalog"));

        public MapSpec Get(string id)
        {
            foreach (var m in maps) if (m.id == id) return m;
            return maps.Length > 0 ? maps[0] : null;
        }

        public MapSpec ByScene(string sceneName)
        {
            foreach (var m in maps) if (m.sceneName == sceneName) return m;
            return null;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < maps.Length; i++) if (maps[i].id == id) return i;
            return 0;
        }
    }
}
