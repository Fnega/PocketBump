#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PocketBump
{
    // 将静态 Kenney 外观写入正式场景；动态玩家、弹珠和事件特效仍由运行时创建。
    public static class PocketBumpArtBaker
    {
        const string MainMenuPath = "Assets/Scenes/MainMenu.unity";
        const string ArenaPath = "Assets/Scenes/Arena.unity";

        [MenuItem("PocketBump/Bake Kenney Static Art")]
        public static void BakeAll()
        {
            Editor.DashProductionSceneBaker.MigrateFormalScenes();
        }

        static void BakeMainMenu()
        {
            Scene scene = EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);
            MainMenuController menu = FindInScene<MainMenuController>(scene);
            if (menu == null)
            {
                Debug.LogError("PocketBump: MainMenuController not found in MainMenu.unity.");
                return;
            }

            menu.BuildAuthoredHierarchy();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void BakeArena()
        {
            Scene scene = EditorSceneManager.OpenScene(ArenaPath, OpenSceneMode.Single);
            Arena arena = FindInScene<Arena>(scene);
            if (arena == null)
            {
                Debug.LogError("PocketBump: Arena not found in Arena.unity.");
                return;
            }

            arena.ApplyArtTheme();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null) return component;
            }
            return null;
        }
    }
}
#endif
