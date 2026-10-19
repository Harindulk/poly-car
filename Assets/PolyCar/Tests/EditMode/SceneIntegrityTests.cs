using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PolyCar.Tests
{
    /// <summary>Every build scene opens without missing scripts, missing prefabs or a missing race layer.</summary>
    public class SceneIntegrityTests
    {
        [Test]
        public void BuildScenesHaveNoMissingReferences()
        {
            var problems = new System.Text.StringBuilder();
            var scenes = EditorBuildSettings.scenes;
            Assert.GreaterOrEqual(scenes.Length, 5, "menu + four tracks");
            foreach (var s in scenes)
            {
                var scene = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
                int races = 0;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        var go = t.gameObject;
                        int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                        if (missing > 0) problems.AppendLine($"{s.path}: {go.name} has {missing} missing script(s)");
                        if (PrefabUtility.IsPrefabAssetMissing(go)) problems.AppendLine($"{s.path}: {go.name} is a missing prefab");
                        if (go.GetComponent<RaceManager>()) races++;
                    }
                if (!s.path.EndsWith("Menu.unity") && races != 1) problems.AppendLine($"{s.path}: expected one RaceManager, found {races}");
            }
            Assert.IsEmpty(problems.ToString(), problems.ToString());
        }
    }
}
