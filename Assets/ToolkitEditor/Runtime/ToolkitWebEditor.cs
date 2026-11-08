using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Scripting;
using UnityEngine.InputSystem;

/// <summary>
/// Adds web-editor-only behavior on top of the shared Toolkit viewer scene.
/// The first version can open an existing published scene or start blank, then
/// receive a processed image uploaded through the browser session page.
/// </summary>
// This component must run before ToolkitSceneLoader (which uses Unity's default
// execution order of 0). It decides whether the shared loader should start and
// therefore whether the player needs to be paused while scene data is applied.
[DefaultExecutionOrder(-100)]
[Preserve]
public class ToolkitWebEditor : MonoBehaviour
{
    // This shape must match the JSON object constructed in the editor WebGL
    // template before it calls SendMessage("ToolkitWebEditor", ...).
    [Serializable]
    private class UploadedImageMessage
    {
        public string filename;
        public string imageUrl;
    }

    [Serializable]
    private class SaveImageSource
    {
        public string fileName;
        public string sourceUrl;
    }

    [Serializable]
    private class WebSceneSavePayload
    {
        public string sceneId;
        public string sceneJson;
        public List<SaveImageSource> images = new List<SaveImageSource>();
        public List<SaveImageSource> audio = new List<SaveImageSource>();
    }

    [Tooltip("Shared loader used when the editor URL contains ?scene=SCENE_ID.")]
    public ToolkitSceneLoader sceneLoader;

    [Tooltip("Player used to place a newly uploaded image in front of the camera.")]
    public KeyboardMove player;

    // Uploaded objects are kept separate from objects created by the shared
    // scene loader. This will make later selection and saving logic easier to add.
    private GameObject uploadedImagesRoot;
    private ToolkitSceneAudio heldSound;
    private bool heldSoundIsNew;
    private Vector3 heldSoundOriginalPosition;
    private bool soundChoiceVisible;
    private bool soundChoicePlayerEnabled, soundChoiceHorizontalEnabled, soundChoiceVerticalEnabled;
    private ToolkitEditableImage aimedImage;
    private ToolkitEditableImage heldImage;
    private ToolkitEditableImage ignoredPlacedImage;
    private Vector3 heldOriginalPosition;
    private Quaternion heldOriginalRotation;
    private Quaternion heldRotationRelativeToView;
    private Vector3 heldOriginalScale;
    private float heldDistance = 2f;
    private string currentComposePrompt;
    private bool rotationMode;
    private GameObject rotationWidget;
    private ToolkitRotationRing draggedRotationRing;
    private Vector3 draggedRotationAxisWorld;
    private Vector3 previousRotationVector;
    private MouseLookHorizontal horizontalLook;
    private MouseLookVertical verticalLook;
    private bool hasTrackedPlayerTransform;
    private Vector3 trackedPlayerPosition;
    private Quaternion trackedPlayerRotation;
    private Quaternion trackedViewRotation;

#if UNITY_WEBGL && !UNITY_EDITOR
    // ToolkitViewerBridge.jslib implements these functions. Although their
    // names predate the editor, both WebGL templates provide the same status UI.
    // They are excluded in the Unity Editor, where there is no surrounding page.
    [DllImport("__Internal")]
    private static extern void ToolkitViewerShowReady(int failedImageCount);

    [DllImport("__Internal")]
    private static extern void ToolkitViewerShowError(string message, string details);

    [DllImport("__Internal")]
    private static extern void ToolkitEditorImageAdded(string filename);

    [DllImport("__Internal")]
    private static extern void ToolkitEditorSetComposePrompt(string message);

    [DllImport("__Internal")]
    private static extern void ToolkitEditorSavePayload(string payloadJson);

    [DllImport("__Internal")]
    private static extern void ToolkitEditorSceneChanged();

    [DllImport("__Internal")]
    private static extern void ToolkitEditorSetRotationMode(int active);
#endif

    private void Awake()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        WebGLInput.captureAllKeyboardInput = false;
#endif
        // Inspector references are used when assigned; otherwise this searches
        // the copied viewer scene so the prototype does not require manual setup.
        FindSceneReferences();

        if (sceneLoader != null)
        {
            // Run before ToolkitSceneLoader.Awake so its existing player-pause
            // behavior is enabled only when an existing scene will be loaded.
            sceneLoader.loadOnStart = PageHasSceneQuery();
        }

        // A blank editor has no floor. Keep its player suspended so the camera
        // does not fall away from newly added objects. Existing scenes retain
        // the flying/grounded setting stored in their scene data.
        if (!PageHasSceneQuery() && player != null)
        {
            player.flying = true;
            player.gravity = 0f;
            player.minFall = 0f;
        }
    }

    private void Start()
    {
        // ToolkitSceneLoader.Start handles an existing ?scene=... URL. Without
        // one, this editor intentionally opens as a blank world.
        if (sceneLoader == null || !sceneLoader.loadOnStart)
        {
            ShowPageReady();
        }
    }

    private void Update()
    {
        if (soundChoiceVisible) return;
        TrackPlayerChanges();
        if (heldSound != null) {
            SetComposePrompt("WASD — Move · Mouse — Look · Click — Place sound · Esc — Cancel");
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) CancelHeldSound();
            return;
        }

        if (rotationMode)
        {
            UpdateRotationWidgetInput();
            return;
        }

        if (heldImage != null)
        {
            ScaleHeldImage();
            SetComposePrompt(
                "Click — Place   ·   [ & ] to Scale   ·   R — Rotate   ·   Esc — Cancel"
            );

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                EnterRotationMode();
                return;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                PlaceHeldImage();
            }
            else if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelHeldImage();
            }

            return;
        }

        ToolkitEditableImage nextAimedImage = FindAimedImage();

        // After placement the reticle is normally still over the same image.
        // Keep its outline off until the player looks away and targets it again.
        if (ignoredPlacedImage != null)
        {
            if (nextAimedImage == ignoredPlacedImage)
            {
                nextAimedImage = null;
            }
            else
            {
                ignoredPlacedImage = null;
            }
        }
        if (nextAimedImage != aimedImage)
        {
            if (aimedImage != null)
            {
                aimedImage.SetHighlighted(false);
            }

            aimedImage = nextAimedImage;
            if (aimedImage != null)
            {
                aimedImage.SetHighlighted(true);
            }
        }

        SetComposePrompt(FindAimedSound() != null ? "Click — Move sound" : aimedImage != null ? "Click — Pick up image" : "");

        if (aimedImage != null && Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame)
        {
            PickUpAimedImage();
        }
    }

    private void LateUpdate()
    {
        // Player movement and mouse-look both run during Update. Following the
        // camera here avoids carrying the image using the previous frame's view,
        // which otherwise appears as jitter while moving and looking around.
        if (heldSound != null) heldSound.transform.position = AudioPlacementPosition();
        if (heldImage != null && !rotationMode)
        {
            MoveHeldImage();
        }
    }

    /// <summary>
    /// Called by the editor WebGL page after its upload tab has saved an image.
    /// Unity's SendMessage API accepts one string, so the payload arrives as JSON.
    /// </summary>
    [Preserve]
    public void AddUploadedImage(string messageJson)
    {
        // The browser provides an absolute URL to the server-processed PNG. It
        // sends a URL instead of image bytes so large files never cross through
        // SendMessage as base64 text.
        UploadedImageMessage message = JsonUtility.FromJson<UploadedImageMessage>(messageJson);

        if (message == null || string.IsNullOrWhiteSpace(message.imageUrl))
        {
            Debug.LogError("The browser returned an invalid uploaded-image message.");
            return;
        }

        // Downloading is asynchronous in Unity, particularly in WebGL. A
        // coroutine lets frames continue rendering while the request is active.
        StartCoroutine(LoadUploadedImage(message));
    }

    [Serializable]
    private class AudioMessage
    {
        public string objectId;
        public string sourceUrl;
        public ToolkitAudioSettings audio;
    }

    [Preserve]
    public void AddSceneAudio(string json)
    {
        AudioMessage message = JsonUtility.FromJson<AudioMessage>(json);
        if (message == null || string.IsNullOrEmpty(message.sourceUrl)) return;
        if (message.audio == null || string.IsNullOrWhiteSpace(message.audio.displayName)) return;
        string displayName = message.audio.displayName.Trim();
        if (displayName.Length < 3 || displayName.Length > 40) return;
        message.audio.displayName = displayName;
        if (FindObjectsByType<ToolkitSceneAudio>(FindObjectsSortMode.None).Length >= 16) return;
        ToolkitObjectData data = new ToolkitObjectData {
            objectId = Guid.NewGuid().ToString("N"), type = "audio",
            audio = message.audio ?? new ToolkitAudioSettings(),
            transform = new ToolkitTransformData { position = AudioPlacementPosition(), scale = Vector3.one }
        };
        ToolkitSceneAudio sound = ToolkitSceneAudio.Create(null, data, message.sourceUrl, true);
        if (sound.settings.spatial) BeginHoldingSound(sound, true);
        else { sound.settings.loop = true; sound.settings.autoplay = true; NotifySceneChanged(); }
    }

    [Preserve]
    public void UpdateSceneAudio(string json)
    {
        AudioMessage message = JsonUtility.FromJson<AudioMessage>(json);
        foreach (ToolkitSceneAudio sound in FindObjectsByType<ToolkitSceneAudio>(FindObjectsSortMode.None))
            if (sound.objectId == message.objectId && message.audio != null) {
                sound.settings = message.audio;
                NotifySceneChanged();
                break;
            }
    }

    [Preserve]
    public void PlaceSceneAudio(string id)
    {
        foreach (ToolkitSceneAudio sound in FindObjectsByType<ToolkitSceneAudio>(FindObjectsSortMode.None))
            if (sound.objectId == id) { BeginHoldingSound(sound, false); break; }
    }

    [Preserve]
    public void SetSoundChoiceVisible(string value)
    {
        bool visible = value == "1";
        if (visible == soundChoiceVisible) return;
        soundChoiceVisible = visible;
        FindSceneReferences();
        horizontalLook = player != null ? player.GetComponent<MouseLookHorizontal>() : null;
        verticalLook = player != null ? player.GetComponentInChildren<MouseLookVertical>(true) : null;
        if (visible) {
            soundChoicePlayerEnabled = player != null && player.enabled;
            soundChoiceHorizontalEnabled = horizontalLook != null && horizontalLook.enabled;
            soundChoiceVerticalEnabled = verticalLook != null && verticalLook.enabled;
            if (player != null) player.enabled = false;
            if (horizontalLook != null) horizontalLook.enabled = false;
            if (verticalLook != null) verticalLook.enabled = false;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            SetComposePrompt("");
        } else {
            if (player != null) player.enabled = soundChoicePlayerEnabled;
            if (horizontalLook != null) horizontalLook.enabled = soundChoiceHorizontalEnabled;
            if (verticalLook != null) verticalLook.enabled = soundChoiceVerticalEnabled;
        }
    }

    private void BeginHoldingSound(ToolkitSceneAudio sound, bool isNew)
    {
        if (heldSound != null) CancelHeldSound();
        if (heldImage != null) CancelHeldImage();
        heldSound = sound; heldSoundIsNew = isNew;
        heldSoundOriginalPosition = sound.transform.position;
        sound.pendingPlacement = true;
        sound.transform.position = AudioPlacementPosition();
    }

    [Preserve]
    public void HandleSceneAudioClick()
    {
        if (soundChoiceVisible || rotationMode || heldImage != null) return;
        if (heldSound != null) {
            heldSound.transform.position = AudioPlacementPosition();
            heldSound.pendingPlacement = false;
            heldSound = null;
            SetComposePrompt(""); NotifySceneChanged();
            return;
        }
        ToolkitSceneAudio aimed = FindAimedSound();
        if (aimed != null) BeginHoldingSound(aimed, false);
    }

    private ToolkitSceneAudio FindAimedSound()
    {
        Camera view = GetPlacementCamera();
        if (view == null) return null;
        RaycastHit[] hits = Physics.RaycastAll(view.transform.position, view.transform.forward, 20f);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits) {
            ToolkitSceneAudio sound = hit.collider.GetComponentInParent<ToolkitSceneAudio>();
            if (sound != null && sound.settings.spatial) return sound;
            if (hit.collider.GetComponentInParent<ToolkitEditableImage>() != null) return null;
        }
        return null;
    }

    private void CancelHeldSound()
    {
        if (heldSoundIsNew) Destroy(heldSound.gameObject);
        else { heldSound.transform.position = heldSoundOriginalPosition; heldSound.pendingPlacement = false; }
        heldSound = null; SetComposePrompt("");
    }

    [Preserve]
    public void RemoveSceneAudio(string id)
    {
        foreach (ToolkitSceneAudio sound in FindObjectsByType<ToolkitSceneAudio>(FindObjectsSortMode.None))
            if (sound.objectId == id) { Destroy(sound.gameObject); StartCoroutine(NotifyAudioRemoved()); break; }
    }

    private IEnumerator NotifyAudioRemoved()
    {
        yield return null;
        NotifySceneChanged();
    }

    private Vector3 AudioPlacementPosition()
    {
        Camera view = GetPlacementCamera();
        return view != null ? view.transform.position + view.transform.forward * 2f : Vector3.zero;
    }

    /// <summary>
    /// Called by the web page after the user enters a path-safe save code.
    /// </summary>
    [Preserve]
    public void SaveScene(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
        {
            return;
        }

        ToolkitEditableImage[] editableImages =
            FindObjectsByType<ToolkitEditableImage>(FindObjectsSortMode.None);

        ToolkitSceneData sceneData = new ToolkitSceneData
        {
            sceneId = sceneId,
            title = sceneId,
            // Web-editor saves are private by default. There is intentionally
            // no public toggle yet, so a user cannot publish accidentally.
            isPublic = false,
            player = BuildPlayerData()
        };
        WebSceneSavePayload payload = new WebSceneSavePayload
        {
            sceneId = sceneId
        };

        for (int index = 0; index < editableImages.Length; index++)
        {
            ToolkitEditableImage editableImage = editableImages[index];
            if (string.IsNullOrWhiteSpace(editableImage.SourceUrl))
            {
                continue;
            }

            string fileName = $"image-{index + 1:000}.png";
            string objectId = string.IsNullOrWhiteSpace(editableImage.ObjectId)
                ? Guid.NewGuid().ToString("N")
                : editableImage.ObjectId;

            sceneData.objects.Add(new ToolkitObjectData
            {
                objectId = objectId,
                type = "image",
                assetUrl = "images/" + fileName,
                transform = new ToolkitTransformData
                {
                    position = editableImage.transform.position,
                    rotation = editableImage.transform.eulerAngles,
                    scale = editableImage.PublishedScale
                }
            });
            payload.images.Add(new SaveImageSource
            {
                fileName = fileName,
                sourceUrl = editableImage.SourceUrl
            });
        }

        ToolkitSceneAudio[] sounds = FindObjectsByType<ToolkitSceneAudio>(FindObjectsSortMode.None);
        for (int i = 0; i < sounds.Length; i++)
        {
            ToolkitSceneAudio sound = sounds[i];
            if (sound == heldSound && heldSoundIsNew) continue;
            string extension = System.IO.Path.GetExtension(new Uri(sound.sourceUrl).AbsolutePath);
            string fileName = "sound-" + sound.objectId + extension;
            sceneData.objects.Add(new ToolkitObjectData {
                objectId = sound.objectId, type = "audio", assetUrl = "audio/" + fileName,
                audio = sound.settings,
                transform = new ToolkitTransformData { position = sound == heldSound ? heldSoundOriginalPosition : sound.transform.position, rotation = Vector3.zero, scale = Vector3.one }
            });
            payload.audio.Add(new SaveImageSource { fileName = fileName, sourceUrl = sound.sourceUrl });
        }
        payload.sceneJson = JsonUtility.ToJson(sceneData, true);

#if UNITY_WEBGL && !UNITY_EDITOR
        ToolkitEditorSavePayload(JsonUtility.ToJson(payload));
#else
        Debug.Log(payload.sceneJson);
#endif
    }

    // Publishing may rename an older bundle's files. Keep subsequent autosaves
    // pointed at the committed bundle rather than its now-replaced source paths.
    [Preserve]
    public void SceneAudioSaved(string sceneJson)
    {
        ToolkitSceneData saved = JsonUtility.FromJson<ToolkitSceneData>(sceneJson);
        if (saved == null || saved.objects == null) return;
        foreach (ToolkitSceneAudio sound in FindObjectsByType<ToolkitSceneAudio>(FindObjectsSortMode.None))
            foreach (ToolkitObjectData data in saved.objects)
                if (data.type == "audio" && data.objectId == sound.objectId)
                    sound.sourceUrl = new Uri(new Uri(sound.sourceUrl),
                        "/scenes/" + saved.sceneId + "/" + data.assetUrl).AbsoluteUri;
    }

    private IEnumerator LoadUploadedImage(UploadedImageMessage message)
    {
        // UnityWebRequestTexture downloads the PNG and decodes it directly into
        // a Texture2D, avoiding a separate byte-array decoding step.
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(message.imageUrl))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Could not download uploaded image: " + request.error);
                ShowPageError("The uploaded image could not be added.", request.error);
                yield break;
            }

            // The server normalizes accepted uploads to PNG before this point.
            Texture2D texture = DownloadHandlerTexture.GetContent(request);
            texture.name = message.filename;

            CreateImageObject(texture, message.filename, message.imageUrl);
            NotifySceneChanged();
            NotifyPageImageAdded(message.filename);
            ShowPageReady();
        }
    }

    private void CreateImageObject(Texture2D texture, string filename, string sourceUrl)
    {

            // Create the root lazily because a blank editor may never upload an
            // image during its lifetime.
            if (uploadedImagesRoot == null)
            {
                uploadedImagesRoot = new GameObject("Toolkit Editor Uploads");
            }

            GameObject imageObject = new GameObject("Toolkit Upload - " + filename);
            imageObject.transform.SetParent(uploadedImagesRoot.transform);
            MeshFilter meshFilter = imageObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = imageObject.AddComponent<MeshRenderer>();

            // The editor scene contains no built-in sprite assets, so WebGL can
            // remove Unity's default sprite shader while optimizing the build.
            // Load our explicitly included two-sided shader instead.
            Shader uploadedImageShader = Resources.Load<Shader>("ToolkitTwoSidedSprite");
            if (uploadedImageShader != null)
            {
                Material imageMaterial = new Material(uploadedImageShader);
                imageMaterial.mainTexture = texture;
                meshRenderer.material = imageMaterial;
            }
            else
            {
                Debug.LogError("ToolkitTwoSidedSprite shader was not included in the build.");
            }

            // Manipulation tools do not exist yet. For now, place the image two
            // units in front of the current view so the upload result is visible.
            Camera viewCamera = GetPlacementCamera();
            Transform view = viewCamera != null ? viewCamera.transform : GetPlacementView();

            // Ignore camera pitch so a downward glance does not put the image
            // below the player or lay it flat on the ground.
            Vector3 horizontalForward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            if (horizontalForward.sqrMagnitude < 0.001f)
            {
                horizontalForward = player != null ? player.transform.forward : Vector3.forward;
            }

            imageObject.transform.position = view.position + horizontalForward * 2f;
            imageObject.transform.rotation = Quaternion.LookRotation(-horizontalForward, Vector3.up);

            // Build a 1.5-unit rectangle while preserving the image proportions.
            meshFilter.mesh = CreateImageMesh(texture.width, texture.height);
            BoxCollider imageCollider = imageObject.AddComponent<BoxCollider>();
            imageCollider.size = new Vector3(
                meshFilter.mesh.bounds.size.x,
                meshFilter.mesh.bounds.size.y,
                0.05f
            );
            ToolkitEditableImage editableImage = imageObject.AddComponent<ToolkitEditableImage>();
            float publishedScaleMultiplier = 150f / Mathf.Max(texture.width, texture.height);
            editableImage.Initialize(
                meshRenderer,
                Guid.NewGuid().ToString("N"),
                sourceUrl,
                publishedScaleMultiplier
            );

            if (viewCamera != null)
            {
                Vector3 viewportPoint = viewCamera.WorldToViewportPoint(imageObject.transform.position);
                Debug.Log("Added uploaded Toolkit image " + filename +
                    " at viewport " + viewportPoint + ", visible=" + meshRenderer.isVisible);
            }
            else
            {
                Debug.Log("Added uploaded Toolkit image: " + filename);
            }
    }

    private void FindSceneReferences()
    {
        // Include inactive objects because ToolkitSceneLoader can temporarily
        // disable the player while applying a published scene's settings.
        if (sceneLoader == null)
        {
            sceneLoader = FindFirstObjectByType<ToolkitSceneLoader>(FindObjectsInactive.Include);
        }

        if (player == null)
        {
            player = FindFirstObjectByType<KeyboardMove>(FindObjectsInactive.Include);
        }
    }

    private ToolkitPlayerData BuildPlayerData()
    {
        FindSceneReferences();
        if (player == null)
        {
            return null;
        }

        MouseLookVertical verticalLook =
            player.GetComponentInChildren<MouseLookVertical>(true);
        float verticalAngle = verticalLook != null
            ? Mathf.DeltaAngle(0f, verticalLook.transform.localEulerAngles.x)
            : 0f;

        return new ToolkitPlayerData
        {
            flying = player.flying,
            speed = player.speed,
            gravity = player.gravity,
            jumpSpeed = player.jumpSpeed,
            minFall = player.minFall,
            pushForce = player.pushForce,
            position = player.transform.position,
            rotation = player.transform.eulerAngles,
            verticalLookAngle = verticalAngle
        };
    }

    private Transform GetPlacementView()
    {
        FindSceneReferences();

        // Prefer the player's child camera: its pitch may differ from the player
        // root. Fall back in stages so uploads still work in a modified scene.
        if (player != null)
        {
            Camera playerCamera = player.GetComponentInChildren<Camera>(true);
            return playerCamera != null ? playerCamera.transform : player.transform;
        }

        return Camera.main != null ? Camera.main.transform : transform;
    }

    private Camera GetPlacementCamera()
    {
        FindSceneReferences();

        if (player != null)
        {
            Camera playerCamera = player.GetComponentInChildren<Camera>(true);
            if (playerCamera != null)
            {
                return playerCamera;
            }
        }

        return Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
    }

    private static Mesh CreateImageMesh(int pixelWidth, int pixelHeight)
    {
        float aspect = pixelHeight > 0 ? (float)pixelWidth / pixelHeight : 1f;
        float halfWidth = aspect >= 1f ? 0.75f : 0.75f * aspect;
        float halfHeight = aspect >= 1f ? 0.75f / aspect : 0.75f;

        Mesh mesh = new Mesh { name = "Toolkit Uploaded Image Mesh" };
        mesh.vertices = new[]
        {
            new Vector3(-halfWidth, -halfHeight, 0f),
            new Vector3( halfWidth, -halfHeight, 0f),
            new Vector3( halfWidth,  halfHeight, 0f),
            new Vector3(-halfWidth,  halfHeight, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private ToolkitEditableImage FindAimedImage()
    {
        Camera viewCamera = GetPlacementCamera();
        if (viewCamera == null)
        {
            return null;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            viewCamera.transform.position,
            viewCamera.transform.forward,
            20f
        );
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.GetComponentInParent<ToolkitSceneAudio>() != null) return null;
            ToolkitEditableImage editableImage = hit.collider.GetComponentInParent<ToolkitEditableImage>();
            if (editableImage != null)
            {
                return editableImage;
            }
        }

        return null;
    }

    private void PickUpAimedImage()
    {
        heldImage = aimedImage;
        aimedImage = null;
        heldOriginalPosition = heldImage.transform.position;
        heldOriginalRotation = heldImage.transform.rotation;
        heldOriginalScale = heldImage.transform.localScale;

        Camera viewCamera = GetPlacementCamera();
        if (viewCamera != null)
        {
            // Preserve one exact camera-relative quaternion while carrying.
            // Reconstructing pitch/yaw separately caused small visible warbles.
            heldRotationRelativeToView =
                Quaternion.Inverse(viewCamera.transform.rotation) * heldImage.transform.rotation;

            // Preserve how far away the image was when selected instead of
            // snapping every picked-up image to the same two-unit distance.
            heldDistance = Vector3.Distance(
                viewCamera.transform.position,
                heldImage.transform.position
            );
            heldDistance = Mathf.Clamp(heldDistance, 0.5f, 20f);
        }

        MoveHeldImage();
    }

    private void MoveHeldImage()
    {
        Camera viewCamera = GetPlacementCamera();
        if (viewCamera == null || heldImage == null)
        {
            return;
        }

        heldImage.transform.position =
            viewCamera.transform.position + viewCamera.transform.forward * heldDistance;

        heldImage.transform.rotation =
            viewCamera.transform.rotation * heldRotationRelativeToView;
    }

    private void ScaleHeldImage()
    {
        if (heldImage == null || Keyboard.current == null)
        {
            return;
        }

        float scaleFactor = 1f;
        if (Keyboard.current.leftBracketKey.wasPressedThisFrame)
        {
            scaleFactor = 0.9f;
        }
        else if (Keyboard.current.rightBracketKey.wasPressedThisFrame)
        {
            scaleFactor = 1.1f;
        }

        if (Mathf.Approximately(scaleFactor, 1f))
        {
            return;
        }

        Vector3 nextScale = heldImage.transform.localScale * scaleFactor;
        float largestAxis = Mathf.Max(nextScale.x, nextScale.y, nextScale.z);
        float smallestAxis = Mathf.Min(nextScale.x, nextScale.y, nextScale.z);
        if (largestAxis <= 20f && smallestAxis >= 0.05f)
        {
            heldImage.transform.localScale = nextScale;
        }
    }

    private void EnterRotationMode()
    {
        if (heldImage == null || rotationMode)
        {
            return;
        }

        rotationMode = true;
        SetComposePrompt("");
        CreateRotationWidget();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        FindSceneReferences();
        horizontalLook = player != null
            ? player.GetComponent<MouseLookHorizontal>()
            : null;
        verticalLook = player != null
            ? player.GetComponentInChildren<MouseLookVertical>(true)
            : null;

        if (player != null)
        {
            player.enabled = false;
        }
        if (horizontalLook != null)
        {
            horizontalLook.enabled = false;
        }
        if (verticalLook != null)
        {
            verticalLook.enabled = false;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        ToolkitEditorSetRotationMode(1);
#endif
    }

    private void CreateRotationWidget()
    {
        if (heldImage == null)
        {
            return;
        }

        rotationWidget = new GameObject("Toolkit Rotation Widget");
        rotationWidget.transform.position = heldImage.transform.position;
        rotationWidget.transform.rotation = heldImage.transform.rotation;

        // Keep the gizmo approximately the same size on screen instead of
        // scaling it with the image. Large and small images then get the same
        // manageable controls.
        Camera viewCamera = GetPlacementCamera();
        float distance = viewCamera != null
            ? Vector3.Distance(viewCamera.transform.position, heldImage.transform.position)
            : 2f;
        float worldViewHeight = viewCamera != null
            ? 2f * distance * Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad)
            : 2f;
        float radius = Mathf.Clamp(worldViewHeight * 0.1f, 0.3f, 2.5f);

        CreateRotationRing(
            "Local Vertical Rotation",
            ToolkitRotationRing.RingAxis.LocalVertical,
            new Color(0.2f, 0.95f, 0.35f, 1f),
            Quaternion.Euler(90f, 0f, 0f),
            radius
        );
        CreateRotationRing(
            "Local Horizontal Rotation",
            ToolkitRotationRing.RingAxis.LocalHorizontal,
            new Color(1f, 0.25f, 0.2f, 1f),
            Quaternion.Euler(0f, 90f, 0f),
            radius
        );
    }

    private void CreateRotationRing(
        string ringName,
        ToolkitRotationRing.RingAxis axis,
        Color color,
        Quaternion localRotation,
        float radius
    )
    {
        const int SegmentCount = 48;
        float thickness = Mathf.Max(radius * 0.024f, 0.015f);
        GameObject ringObject = new GameObject(ringName);
        ringObject.transform.SetParent(rotationWidget.transform, false);
        ringObject.transform.localRotation = localRotation;
        ToolkitRotationRing ring = ringObject.AddComponent<ToolkitRotationRing>();
        ring.axis = axis;

        LineRenderer line = ringObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = SegmentCount;
        line.startWidth = thickness;
        line.endWidth = thickness;
        line.numCornerVertices = 2;
        line.sortingOrder = 1100;

        Shader shader = Resources.Load<Shader>("ToolkitTwoSidedSprite");
        if (shader != null)
        {
            Material material = new Material(shader);
            material.color = color;
            line.material = material;
        }

        Vector3[] positions = new Vector3[SegmentCount];
        for (int index = 0; index < SegmentCount; index++)
        {
            float angle = index * Mathf.PI * 2f / SegmentCount;
            positions[index] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
        }
        line.SetPositions(positions);

        for (int index = 0; index < SegmentCount; index++)
        {
            Vector3 start = positions[index];
            Vector3 end = positions[(index + 1) % SegmentCount];
            GameObject hitSegment = new GameObject("Ring Hit Area");
            hitSegment.transform.SetParent(ringObject.transform, false);
            hitSegment.transform.localPosition = (start + end) * 0.5f;
            Vector3 direction = end - start;
            hitSegment.transform.localRotation =
                Quaternion.FromToRotation(Vector3.right, direction.normalized);
            BoxCollider collider = hitSegment.AddComponent<BoxCollider>();
            collider.size = new Vector3(direction.magnitude, thickness * 7f, thickness * 7f);
        }
    }

    private void UpdateRotationWidgetInput()
    {
        if (heldImage == null || Mouse.current == null)
        {
            return;
        }

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            draggedRotationRing = FindRotationRingAtMouse(mousePosition);
            if (draggedRotationRing != null &&
                !TryGetRotationVector(mousePosition, draggedRotationRing, out previousRotationVector))
            {
                draggedRotationRing = null;
            }
        }

        if (draggedRotationRing != null && Mouse.current.leftButton.isPressed)
        {
            if (TryGetRotationVector(mousePosition, draggedRotationRing, out Vector3 rotationVector))
            {
                float angle = Vector3.SignedAngle(
                    previousRotationVector,
                    rotationVector,
                    draggedRotationAxisWorld
                );
                heldImage.transform.rotation =
                    Quaternion.AngleAxis(angle, draggedRotationAxisWorld) *
                    heldImage.transform.rotation;
                previousRotationVector = rotationVector;

                SyncRotationWidget();
            }
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            draggedRotationRing = null;
        }
    }

    private ToolkitRotationRing FindRotationRingAtMouse(Vector2 mousePosition)
    {
        Camera viewCamera = GetPlacementCamera();
        if (viewCamera == null)
        {
            return null;
        }

        Ray ray = viewCamera.ScreenPointToRay(mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            ToolkitRotationRing ring = hit.collider.GetComponentInParent<ToolkitRotationRing>();
            if (ring != null)
            {
                return ring;
            }
        }

        return null;
    }

    private bool TryGetRotationVector(
        Vector2 mousePosition,
        ToolkitRotationRing ring,
        out Vector3 rotationVector
    )
    {
        rotationVector = Vector3.zero;
        Camera viewCamera = GetPlacementCamera();
        if (viewCamera == null || heldImage == null)
        {
            return false;
        }

        draggedRotationAxisWorld = ring.axis == ToolkitRotationRing.RingAxis.LocalVertical
            ? heldImage.transform.up
            : heldImage.transform.right;

        Plane rotationPlane = new Plane(
            draggedRotationAxisWorld,
            heldImage.transform.position
        );
        Ray mouseRay = viewCamera.ScreenPointToRay(mousePosition);
        if (!rotationPlane.Raycast(mouseRay, out float distance))
        {
            return false;
        }

        rotationVector =
            (mouseRay.GetPoint(distance) - heldImage.transform.position).normalized;
        return rotationVector.sqrMagnitude > 0.001f;
    }

    private void SyncRotationWidget()
    {
        if (rotationWidget == null || heldImage == null)
        {
            return;
        }

        rotationWidget.transform.position = heldImage.transform.position;
        rotationWidget.transform.rotation = heldImage.transform.rotation;
    }

    [Preserve]
    public void FinishRotationAndPlace()
    {
        ExitRotationMode();
        PlaceHeldImage();
    }

    [Preserve]
    public void CancelRotation()
    {
        ExitRotationMode();
        CancelHeldImage();
    }

    [Preserve]
    public void FaceHeldImageToCamera()
    {
        if (heldImage == null)
        {
            return;
        }

        Camera viewCamera = GetPlacementCamera();
        if (viewCamera == null)
        {
            return;
        }

        Vector3 towardCamera =
            (viewCamera.transform.position - heldImage.transform.position).normalized;
        heldImage.transform.rotation = Quaternion.LookRotation(towardCamera, Vector3.up);
        SyncRotationWidget();
    }

    private void ExitRotationMode()
    {
        rotationMode = false;
        draggedRotationRing = null;
        Cursor.visible = false;
        if (rotationWidget != null)
        {
            Destroy(rotationWidget);
            rotationWidget = null;
        }

        if (player != null)
        {
            player.enabled = true;
        }
        if (horizontalLook != null)
        {
            horizontalLook.enabled = true;
        }
        if (verticalLook != null)
        {
            verticalLook.enabled = true;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        ToolkitEditorSetRotationMode(0);
#endif
    }

    private void PlaceHeldImage()
    {
        ignoredPlacedImage = heldImage;
        heldImage.SetHighlighted(false);
        heldImage = null;
        SetComposePrompt("");
        NotifySceneChanged();
    }

    private void CancelHeldImage()
    {
        heldImage.transform.position = heldOriginalPosition;
        heldImage.transform.rotation = heldOriginalRotation;
        heldImage.transform.localScale = heldOriginalScale;
        heldImage.SetHighlighted(false);
        heldImage = null;
        SetComposePrompt("");
    }

    private void SetComposePrompt(string message)
    {
        if (message == currentComposePrompt)
        {
            return;
        }

        currentComposePrompt = message;
#if UNITY_WEBGL && !UNITY_EDITOR
        ToolkitEditorSetComposePrompt(message);
#endif
    }

    private void TrackPlayerChanges()
    {
        FindSceneReferences();
        Camera viewCamera = GetPlacementCamera();
        if (player == null || viewCamera == null)
        {
            return;
        }

        if (!hasTrackedPlayerTransform)
        {
            hasTrackedPlayerTransform = true;
            trackedPlayerPosition = player.transform.position;
            trackedPlayerRotation = player.transform.rotation;
            trackedViewRotation = viewCamera.transform.rotation;
            return;
        }

        bool changed = Vector3.SqrMagnitude(
            player.transform.position - trackedPlayerPosition
        ) > 0.000001f ||
            Quaternion.Angle(player.transform.rotation, trackedPlayerRotation) > 0.01f ||
            Quaternion.Angle(viewCamera.transform.rotation, trackedViewRotation) > 0.01f;

        if (!changed)
        {
            return;
        }

        trackedPlayerPosition = player.transform.position;
        trackedPlayerRotation = player.transform.rotation;
        trackedViewRotation = viewCamera.transform.rotation;
        NotifySceneChanged();
    }

    private static void NotifySceneChanged()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ToolkitEditorSceneChanged();
#endif
    }

    private static bool PageHasSceneQuery()
    {
        // Application.absoluteURL is populated by the browser in WebGL. It is
        // normally empty during Play Mode, which intentionally produces a blank
        // editor for convenient local testing.
        if (!Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out Uri pageUrl))
        {
            return false;
        }

        // Only detect whether a non-empty scene parameter exists. Validation and
        // conversion into /scenes/<id>/scene.json remain the loader's job.
        string query = pageUrl.Query.TrimStart('?');
        foreach (string parameter in query.Split('&'))
        {
            string[] nameAndValue = parameter.Split(new[] { '=' }, 2);
            if (nameAndValue.Length == 2 &&
                Uri.UnescapeDataString(nameAndValue[0]) == "scene" &&
                !string.IsNullOrWhiteSpace(Uri.UnescapeDataString(nameAndValue[1])))
            {
                return true;
            }
        }

        return false;
    }

    private static void ShowPageReady()
    {
        // These wrappers become no-ops outside a deployed WebGL player, allowing
        // the same scene and component to run normally in Unity Play Mode.
#if UNITY_WEBGL && !UNITY_EDITOR
        ToolkitViewerShowReady(0);
#endif
    }

    private static void ShowPageError(string message, string details)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ToolkitViewerShowError(message, details);
#endif
    }

    private static void NotifyPageImageAdded(string filename)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ToolkitEditorImageAdded(filename);
#endif
    }
}
