using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class ToolkitSceneLoader : MonoBehaviour
{
    // Every local or remote scene bundle uses the same JSON filename.
    private const string SceneFileName = "scene.json";

    [Header("Scene Bundle")]
    [Tooltip("Optional complete URL to a remote scene.json file. The web page's ?scene= ID takes priority when present.")]
    public string remoteSceneUrl;

    [Tooltip("Local folder relative to StreamingAssets that contains scene.json and its images.")]
    public string sceneFolder = "ToolkitScenes/TestScene";

    [Header("Loading")]
    public bool loadOnStart = true;

    [Header("Player")]
    [Tooltip("Existing scene player. If empty, the loader finds the first KeyboardMove component.")]
    public KeyboardMove player;

    private GameObject loadedSceneRoot;

    private void Awake()
    {
        // Cache the existing scene player before any Start methods run.
        FindPlayerIfNeeded();

        // Delay KeyboardMove.Start until the imported settings have been applied.
        if (loadOnStart && player != null)
        {
            player.enabled = false;
            Debug.Log("Toolkit scene loader paused the player until scene settings are loaded.");
        }
    }

    private void Start()
    {
        if (loadOnStart)
        {
            LoadScene();
        }
    }

    [ContextMenu("Load Toolkit Scene")]
    public void LoadScene()
    {
        Debug.Log("Starting Toolkit scene load.");
        StartCoroutine(LoadSceneCoroutine());
    }

    private IEnumerator LoadSceneCoroutine()
    {
        // Select either the remote URL or the local StreamingAssets bundle.
        string jsonUrl = GetSceneJsonUrl();
        Debug.Log("Loading Toolkit scene JSON from: " + jsonUrl);

        // UnityWebRequest works with local file URLs and web URLs, including WebGL.
        using (UnityWebRequest jsonRequest = UnityWebRequest.Get(jsonUrl))
        {
            yield return jsonRequest.SendWebRequest();

            if (jsonRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Could not load Toolkit scene JSON: " + jsonRequest.error);
                EnablePlayer();
                yield break;
            }

            ToolkitSceneData sceneData = JsonUtility.FromJson<ToolkitSceneData>(
                jsonRequest.downloadHandler.text
            );

            if (sceneData == null)
            {
                Debug.LogError("Toolkit scene JSON could not be parsed: " + jsonUrl);
                EnablePlayer();
                yield break;
            }

            Debug.Log(
                "Parsed Toolkit scene '" + sceneData.title +
                "' (ID: " + sceneData.sceneId + ")."
            );

            // Apply the published movement values before enabling the player.
            ApplyPlayerSettings(sceneData.player);

            // Remove the previously loaded image hierarchy when reloading.
            if (loadedSceneRoot != null)
            {
                Destroy(loadedSceneRoot);
            }

            // Keep all dynamically created scene objects under one root.
            loadedSceneRoot = new GameObject("Loaded Toolkit Scene - " + sceneData.title);

            // Load supported objects one at a time for this initial implementation.
            foreach (ToolkitObjectData objectData in sceneData.objects)
            {
                if (objectData.type != "image")
                {
                    continue;
                }

                yield return LoadImageObject(jsonUrl, objectData);
            }

            Debug.Log(
                "Loaded Toolkit scene '" + sceneData.title +
                "' with " + loadedSceneRoot.transform.childCount + " image(s)."
            );
        }
    }

    private void ApplyPlayerSettings(ToolkitPlayerData playerData)
    {
        // Find the player automatically when one was not assigned in the Inspector.
        FindPlayerIfNeeded();

        if (player == null)
        {
            Debug.LogWarning("No player with a KeyboardMove component was found in the viewer scene.");
            return;
        }

        if (playerData == null)
        {
            Debug.LogWarning("The Toolkit scene JSON does not contain player settings.");
            EnablePlayer();
            return;
        }

        player.flying = playerData.flying;
        player.speed = playerData.speed;
        player.gravity = playerData.gravity;
        player.jumpSpeed = playerData.jumpSpeed;
        player.minFall = playerData.minFall;
        player.pushForce = playerData.pushForce;

        // Use the starting world-space transform exported from the source scene.
        player.transform.position = playerData.position;
        player.transform.rotation = Quaternion.Euler(playerData.rotation);

        Debug.Log(
            "Applied Toolkit player settings. Flying: " + player.flying +
            ", Speed: " + player.speed +
            ", Position: " + player.transform.position
        );

        // Enabling the component now lets KeyboardMove.Start use imported values.
        EnablePlayer();
    }

    private void FindPlayerIfNeeded()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<KeyboardMove>(FindObjectsInactive.Include);

            if (player != null)
            {
                Debug.Log("Toolkit scene loader found player: " + player.gameObject.name);
            }
        }
    }

    private void EnablePlayer()
    {
        if (player != null)
        {
            player.enabled = true;
        }
    }

    private IEnumerator LoadImageObject(string jsonUrl, ToolkitObjectData objectData)
    {
        // Resolve relative asset paths against the folder containing scene.json.
        string imageUrl = new Uri(new Uri(jsonUrl), objectData.assetUrl).AbsoluteUri;
        Debug.Log(
            "Loading Toolkit image " + objectData.objectId + " from: " + imageUrl
        );

        using (UnityWebRequest imageRequest = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            yield return imageRequest.SendWebRequest();

            if (imageRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    "Could not load image '" + objectData.assetUrl + "': " + imageRequest.error
                );
                yield break;
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(imageRequest);
            texture.name = objectData.assetUrl;

            // Match the 100 pixels-per-unit setting used by the exporting project.
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f
            );

            // Create the image beneath the current loaded-scene root.
            GameObject imageObject = new GameObject("Toolkit Image - " + objectData.objectId);
            imageObject.transform.SetParent(loadedSceneRoot.transform);

            SpriteRenderer renderer = imageObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;

            // Restore the source object's world-space placement.
            imageObject.transform.position = objectData.transform.position;
            imageObject.transform.rotation = Quaternion.Euler(objectData.transform.rotation);
            imageObject.transform.localScale = objectData.transform.scale;

            Debug.Log("Loaded Toolkit image: " + objectData.objectId);
        }
    }

    private static string ConvertPathToUrl(string path)
    {
        // Web paths already have a scheme such as https:// or jar:file://.
        if (path.Contains("://"))
        {
            return path;
        }

        // Desktop StreamingAssets paths need a file:// URL for UnityWebRequest.
        return new Uri(path).AbsoluteUri;
    }

    private string GetSceneJsonUrl()
    {
        // A public viewer URL such as /viewer/?scene=abc loads the matching
        // scene bundle from the same server and takes priority over Inspector settings.
        string querySceneUrl = GetSceneUrlFromPageQuery();
        if (!string.IsNullOrEmpty(querySceneUrl))
        {
            Debug.Log("Toolkit scene loader is using the scene ID from the page URL.");
            return querySceneUrl;
        }

        // A complete remote URL is useful for editor tests and direct scene links.
        if (!string.IsNullOrWhiteSpace(remoteSceneUrl))
        {
            string trimmedRemoteUrl = remoteSceneUrl.Trim();
            Debug.Log("Toolkit scene loader is using a remote scene URL.");
            return trimmedRemoteUrl;
        }

        // Otherwise, build a local path beneath StreamingAssets.
        string jsonPath = Path.Combine(
            Application.streamingAssetsPath,
            sceneFolder,
            SceneFileName
        );

        Debug.Log("Toolkit scene loader is using a local StreamingAssets bundle.");
        return ConvertPathToUrl(jsonPath);
    }

    private static string GetSceneUrlFromPageQuery()
    {
        if (string.IsNullOrWhiteSpace(Application.absoluteURL))
        {
            return null;
        }

        if (!Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out Uri pageUrl))
        {
            Debug.LogWarning("Could not parse the viewer page URL: " + Application.absoluteURL);
            return null;
        }

        string sceneId = GetQueryParameter(pageUrl.Query, "scene");
        if (string.IsNullOrEmpty(sceneId))
        {
            return null;
        }

        if (!IsSafeSceneId(sceneId))
        {
            Debug.LogError("The viewer page contains an invalid scene ID: " + sceneId);
            return null;
        }

        string relativeScenePath = "/scenes/" + sceneId + "/" + SceneFileName;
        return new Uri(pageUrl, relativeScenePath).AbsoluteUri;
    }

    private static string GetQueryParameter(string query, string requestedName)
    {
        string queryWithoutQuestionMark = query.TrimStart('?');
        string[] parameters = queryWithoutQuestionMark.Split('&');

        foreach (string parameter in parameters)
        {
            string[] nameAndValue = parameter.Split(new[] { '=' }, 2);
            string name = Uri.UnescapeDataString(nameAndValue[0]);

            if (name == requestedName && nameAndValue.Length == 2)
            {
                return Uri.UnescapeDataString(nameAndValue[1]);
            }
        }

        return null;
    }

    private static bool IsSafeSceneId(string sceneId)
    {
        if (sceneId.Length == 0 || sceneId.Length > 64)
        {
            return false;
        }

        foreach (char character in sceneId)
        {
            bool isLetterOrNumber =
                character >= 'a' && character <= 'z' ||
                character >= 'A' && character <= 'Z' ||
                character >= '0' && character <= '9';

            if (!isLetterOrNumber && character != '-' && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
