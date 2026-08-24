using UnityEngine;

namespace SashimiBoy
{
    /// <summary>
    /// Direct-play friendly bootstrap. Any generated scene can be opened and played without first loading Bootstrap.
    /// </summary>
    public static class RuntimeBootstrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (GameObject.Find(SashimiBoyConstants.GameRootName) != null)
            {
                return;
            }

            var root = new GameObject(SashimiBoyConstants.GameRootName);
            Object.DontDestroyOnLoad(root);
            root.AddComponent<SaveManager>();
            root.AddComponent<GameFlowManager>();
            root.AddComponent<SceneTransitionService>();
            root.AddComponent<PrototypeDebugPanel>();
        }
    }
}
