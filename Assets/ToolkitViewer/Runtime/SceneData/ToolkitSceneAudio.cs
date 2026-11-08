using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;

[Serializable]
public class ToolkitAudioSettings
{
    public string displayName = "";
    public bool spatial;
    public bool muted;
    public float volume = 1f;
    public float radius = 10f;
    public bool loop = true;
    public bool autoplay = true;
}

// Audio stays in its original container and is played by the browser. Unity
// supplies the source and listener positions, without decoding the recording.
[Preserve]
public class ToolkitSceneAudio : MonoBehaviour
{
    public string objectId;
    public string sourceUrl;
    public ToolkitAudioSettings settings = new ToolkitAudioSettings();
    public bool pendingPlacement;
    private LineRenderer radiusRing;
    private GameObject speakerIcon;
    private Material iconMaterial;
    private Mesh iconMesh;
    private float nextUpdate;
    private Camera listenerCamera;

    [Serializable]
    private class Snapshot
    {
        public string objectId;
        public string sourceUrl;
        public ToolkitAudioSettings audio;
        public Vector3 position;
        public Vector3 listener;
        public bool pendingPlacement;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void ToolkitAudioUpdate(string json);
    [DllImport("__Internal")] private static extern void ToolkitAudioRemove(string id);
#endif

    public static ToolkitSceneAudio Create(Transform parent, ToolkitObjectData data, string url, bool editing)
    {
        GameObject item = new GameObject("Sound - " + data.objectId);
        item.transform.SetParent(parent, false);
        if (data.transform != null) item.transform.position = data.transform.position;
        ToolkitSceneAudio sound = item.AddComponent<ToolkitSceneAudio>();
        sound.objectId = data.objectId;
        sound.sourceUrl = url;
        sound.settings = data.audio ?? new ToolkitAudioSettings();
        if (editing) { sound.CreateRadiusRing(); sound.CreateSpeakerIcon(); }
        return sound;
    }

    private void CreateRadiusRing()
    {
        radiusRing = gameObject.AddComponent<LineRenderer>();
        radiusRing.useWorldSpace = false;
        radiusRing.loop = true;
        radiusRing.positionCount = 64;
        radiusRing.startWidth = radiusRing.endWidth = 0.05f;
        Shader shader = Resources.Load<Shader>("ToolkitTwoSidedSprite");
        if (shader != null)
        {
            radiusRing.material = new Material(shader);
            radiusRing.material.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        }
    }

    private void CreateSpeakerIcon()
    {
        speakerIcon = new GameObject("Speaker icon");
        speakerIcon.transform.SetParent(transform, false);
        MeshFilter filter = speakerIcon.AddComponent<MeshFilter>();
        MeshRenderer renderer = speakerIcon.AddComponent<MeshRenderer>();
        Shader shader = Resources.Load<Shader>("ToolkitTwoSidedSprite");
        if (shader != null) {
            iconMaterial = new Material(shader);
            iconMaterial.color = new Color(0.15f, 0.8f, 1f, 1f);
            renderer.sharedMaterial = iconMaterial;
        }
        // A speaker silhouette made from two flat polygons, plus sound waves.
        iconMesh = new Mesh { name = "Toolkit speaker icon" };
        iconMesh.vertices = new[] {
            new Vector3(-0.30f,-0.12f,0), new Vector3(-0.12f,-0.12f,0),
            new Vector3(-0.12f,0.12f,0), new Vector3(-0.30f,0.12f,0),
            new Vector3(-0.12f,-0.12f,0), new Vector3(0.08f,-0.30f,0),
            new Vector3(0.08f,0.30f,0), new Vector3(-0.12f,0.12f,0)
        };
        iconMesh.triangles = new[] {0,1,2,0,2,3,4,5,6,4,6,7};
        iconMesh.RecalculateBounds(); filter.sharedMesh = iconMesh;
        for (int wave = 0; wave < 2; wave++) {
            GameObject arc = new GameObject("Sound wave"); arc.transform.SetParent(speakerIcon.transform, false);
            LineRenderer line = arc.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.positionCount = 18;
            line.startWidth = line.endWidth = 0.035f; line.sharedMaterial = iconMaterial;
            float radius = 0.24f + wave * 0.15f;
            for (int i = 0; i < 18; i++) {
                float angle = Mathf.Lerp(-0.85f, 0.85f, i / 17f);
                line.SetPosition(i, new Vector3(0.04f + Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0));
            }
        }
        BoxCollider collider = speakerIcon.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.9f, 0.7f, 0.15f);
    }

    private void LateUpdate()
    {
        if (listenerCamera == null)
            listenerCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (speakerIcon != null) {
            speakerIcon.SetActive(settings.spatial);
            if (listenerCamera != null) speakerIcon.transform.rotation = listenerCamera.transform.rotation;
        }
        if (Time.unscaledTime < nextUpdate) return;
        nextUpdate = Time.unscaledTime + 0.1f;
        settings.volume = Mathf.Clamp01(settings.volume);
        settings.radius = Mathf.Clamp(settings.radius, 0.5f, 100f);
        if (radiusRing != null)
        {
            radiusRing.enabled = settings.spatial && !pendingPlacement;
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2f / 64;
                radiusRing.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * settings.radius);
            }
        }
        if (listenerCamera == null)
            listenerCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
#if UNITY_WEBGL && !UNITY_EDITOR
        ToolkitAudioUpdate(JsonUtility.ToJson(new Snapshot {
            objectId = objectId, sourceUrl = sourceUrl, audio = settings, pendingPlacement = pendingPlacement,
            position = transform.position,
            listener = listenerCamera != null ? listenerCamera.transform.position : Vector3.zero
        }));
#endif
    }

    private void OnDestroy()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ToolkitAudioRemove(objectId);
#endif
        if (radiusRing != null && radiusRing.sharedMaterial != null) Destroy(radiusRing.sharedMaterial);
        if (iconMaterial != null) Destroy(iconMaterial);
        if (iconMesh != null) Destroy(iconMesh);
    }
}
