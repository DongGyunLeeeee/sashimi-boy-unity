using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SashimiBoy
{
    [DefaultExecutionOrder(-9400)]
    public sealed class SceneTransitionService : MonoBehaviour
    {
        public static SceneTransitionService Instance { get; private set; }

        [SerializeField] private float minimumLoadingScreenTime = 0.05f;
        private bool isLoading;

        public bool IsLoading => isLoading;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        public void LoadScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogWarning("LoadScene ignored: empty scene name.");
                return;
            }

            if (!isLoading)
            {
                StartCoroutine(LoadRoutine(sceneName));
            }
        }

        private IEnumerator LoadRoutine(string sceneName)
        {
            isLoading = true;
            var start = Time.unscaledTime;
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

            if (op == null)
            {
                Debug.LogError($"Scene '{sceneName}' could not be loaded. Add it to Build Settings or generate the prototype scenes first.");
                isLoading = false;
                yield break;
            }

            while (!op.isDone)
            {
                yield return null;
            }

            while (Time.unscaledTime - start < minimumLoadingScreenTime)
            {
                yield return null;
            }

            isLoading = false;
        }
    }
}
