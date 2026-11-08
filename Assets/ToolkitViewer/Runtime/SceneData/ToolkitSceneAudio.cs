using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;

[Serializable]
public class ToolkitAudioSettings
{
    public string displayName = "";
    public string colorHex = "";
    public bool spatial;
    public bool muted;
    public float volume = 1f;
    public float radius = 3f;
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
    private GameObject radiusSphere;
    private Material sphereMaterial;
    private LineRenderer[] sphereRings;
    private Vector3[][] sphereUnitPoints;
    private Vector3[][] sphereWorldPoints;
    private Vector3 sphereCenter;
    private float sphereRadius = -1f;
    private LineRenderer selectionOutline;
    private Material outlineMaterial;
    private TextMesh nameLabel, nameShadow;
    private bool highlighted;
    private Color speakerColor;
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
        sound.AssignSpeakerColor();
        if (editing) { sound.CreateRadiusSphere(); sound.CreateSpeakerIcon(); }
        return sound;
    }

    private void AssignSpeakerColor()
    {
        // Save the chosen color with the sound, rather than deriving it from
        // load order. New sounds take an unused color from the scene palette.
        string[] palette = {
            "#29B6F6", "#FF7A59", "#B388FF", "#52D681",
            "#FF82B2", "#527DFF", "#FFA94D", "#36D1C4",
            "#D66AFF", "#A4DC5B", "#F26D8C", "#7ACFE0",
            "#CC9B70", "#A6A0FF", "#75E3B3", "#E6A1D9"
        };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ToolkitSceneAudio other in FindObjectsByType<ToolkitSceneAudio>(FindObjectsSortMode.None))
            if (other != this && other.settings != null && !string.IsNullOrEmpty(other.settings.colorHex))
                used.Add(other.settings.colorHex);
        Color saved;
        if (!string.IsNullOrEmpty(settings.colorHex) && ColorUtility.TryParseHtmlString(settings.colorHex, out saved)) {
            speakerColor = saved; speakerColor.a = 1f;
            return;
        }
        foreach (string candidate in palette) {
            if (used.Contains(candidate)) continue;
            settings.colorHex = candidate;
            ColorUtility.TryParseHtmlString(candidate, out speakerColor);
            return;
        }
        settings.colorHex = palette[0];
        ColorUtility.TryParseHtmlString(settings.colorHex, out speakerColor);
    }

    private Color SphereColor(float alpha)
    {
        return new Color(speakerColor.r, speakerColor.g, speakerColor.b, alpha);
    }

    private void CreateRadiusSphere()
    {
        radiusSphere = new GameObject("Sound hearing sphere");
        // Keep the renderer root at world identity. The ring vertices already
        // contain world coordinates and must not inherit a second source transform.
        radiusSphere.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Shader shader = Resources.Load<Shader>("ToolkitSoundWireframe");
        if (shader != null) {
            sphereMaterial = new Material(shader);
            sphereMaterial.color = SphereColor(0.4f);
        }
        // World-space line vertices are derived only from the sound's position
        // and radius. Moving or turning the listener never changes these points.
        const int rings = 11, segments = 64;
        sphereRings = new LineRenderer[rings];
        sphereUnitPoints = new Vector3[rings][];
        sphereWorldPoints = new Vector3[rings][];
        for (int ring = 0; ring < rings; ring++) {
            GameObject wire = new GameObject("Hearing sphere ring " + ring);
            wire.transform.SetParent(radiusSphere.transform, false);
            LineRenderer line = wire.AddComponent<LineRenderer>();
            line.useWorldSpace = true; line.loop = true; line.positionCount = segments;
            line.startWidth = line.endWidth = 0.008f;
            line.sharedMaterial = sphereMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            sphereRings[ring] = line;
            sphereUnitPoints[ring] = new Vector3[segments];
            sphereWorldPoints[ring] = new Vector3[segments];
            for (int point = 0; point < segments; point++) {
                float angle = point * Mathf.PI * 2f / segments;
                if (ring < 5) {
                    float latitude = (-60f + ring * 30f) * Mathf.Deg2Rad;
                    sphereUnitPoints[ring][point] = new Vector3(Mathf.Cos(latitude) * Mathf.Cos(angle),
                        Mathf.Sin(latitude), Mathf.Cos(latitude) * Mathf.Sin(angle));
                } else {
                    float longitude = (ring - 5) * Mathf.PI / 6f;
                    sphereUnitPoints[ring][point] = new Vector3(Mathf.Cos(angle) * Mathf.Cos(longitude),
                        Mathf.Sin(angle), Mathf.Cos(angle) * Mathf.Sin(longitude));
                }
            }
        }
        UpdateSphereGeometry();
    }

    private void UpdateSphereGeometry()
    {
        if (sphereRings == null) return;
        Vector3 center = transform.position;
        float radius = Mathf.Clamp(settings.radius, 0.5f, 100f);
        if ((center - sphereCenter).sqrMagnitude < 0.00000001f && Mathf.Approximately(radius, sphereRadius)) return;
        sphereCenter = center; sphereRadius = radius;
        for (int ring = 0; ring < sphereRings.Length; ring++) {
            for (int point = 0; point < sphereWorldPoints[ring].Length; point++)
                sphereWorldPoints[ring][point] = center + sphereUnitPoints[ring][point] * radius;
            sphereRings[ring].SetPositions(sphereWorldPoints[ring]);
        }
    }

    public void SetHighlighted(bool value)
    {
        highlighted = value;
        if (selectionOutline != null) selectionOutline.enabled = value;
    }

    private void CreateSpeakerIcon()
    {
        speakerIcon = new GameObject("Speaker icon");
        speakerIcon.transform.SetParent(transform, false);
        speakerIcon.transform.localScale = Vector3.one * 0.65f;
        MeshFilter filter = speakerIcon.AddComponent<MeshFilter>();
        MeshRenderer renderer = speakerIcon.AddComponent<MeshRenderer>();
        Shader shader = Resources.Load<Shader>("ToolkitTwoSidedSprite");
        if (shader != null) {
            iconMaterial = new Material(shader);
            iconMaterial.color = speakerColor;
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
        GameObject outline = new GameObject("Speaker selection outline");
        outline.transform.SetParent(speakerIcon.transform, false);
        selectionOutline = outline.AddComponent<LineRenderer>();
        selectionOutline.useWorldSpace = false; selectionOutline.loop = true;
        selectionOutline.positionCount = 4;
        selectionOutline.startWidth = selectionOutline.endWidth = 0.025f;
        selectionOutline.numCornerVertices = 2;
        selectionOutline.SetPositions(new[] {
            new Vector3(-0.37f, -0.38f, -0.02f), new Vector3(0.51f, -0.38f, -0.02f),
            new Vector3(0.51f, 0.38f, -0.02f), new Vector3(-0.37f, 0.38f, -0.02f)
        });
        if (shader != null) {
            outlineMaterial = new Material(shader);
            outlineMaterial.color = new Color(1f, 0.78f, 0.15f, 1f);
            selectionOutline.sharedMaterial = outlineMaterial;
        }
        selectionOutline.enabled = false;
        nameShadow = CreateNameLabel("Sound name shadow", new Vector3(0.078f, -0.508f, 0.005f), Color.black);
        nameLabel = CreateNameLabel("Sound name", new Vector3(0.07f, -0.50f, -0.01f), Color.white);
    }

    private TextMesh CreateNameLabel(string objectName, Vector3 position, Color color)
    {
        GameObject label = new GameObject(objectName);
        label.transform.SetParent(speakerIcon.transform, false);
        label.transform.localPosition = position;
        TextMesh text = label.AddComponent<TextMesh>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.font = font; text.fontSize = 48; text.characterSize = 0.025f;
        text.anchor = TextAnchor.UpperCenter; text.alignment = TextAlignment.Center;
        text.richText = false; text.color = color;
        label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return text;
    }

    private void LateUpdate()
    {
        if (listenerCamera == null)
            listenerCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (speakerIcon != null) {
            speakerIcon.SetActive(settings.spatial);
            if (listenerCamera != null) speakerIcon.transform.rotation = listenerCamera.transform.rotation;
        }
        settings.radius = Mathf.Clamp(settings.radius, 0.5f, 100f);
        if (radiusSphere != null) {
            radiusSphere.SetActive(settings.spatial);
            UpdateSphereGeometry();
            if (sphereMaterial != null) sphereMaterial.color = SphereColor(highlighted ? 0.65f : 0.35f);
        }
        if (nameLabel != null) {
            string label = string.IsNullOrWhiteSpace(settings.displayName) ? "Sound" : settings.displayName;
            nameLabel.text = label; nameShadow.text = label;
        }
        if (Time.unscaledTime < nextUpdate) return;
        nextUpdate = Time.unscaledTime + 0.1f;
        settings.volume = Mathf.Clamp01(settings.volume);
        settings.radius = Mathf.Clamp(settings.radius, 0.5f, 100f);
#if UNITY_WEBGL && !UNITY_EDITOR
        ToolkitAudioUpdate(JsonUtility.ToJson(new Snapshot {
            objectId = objectId, sourceUrl = sourceUrl, audio = settings, pendingPlacement = pendingPlacement,
            position = transform.position,
            listener = listenerCamera != null ? listenerCamera.transform.position : Vector3.zero
        }));
#endif
    }

    private void OnDisable()
    {
        if (radiusSphere != null) radiusSphere.SetActive(false);
    }

    private void OnDestroy()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ToolkitAudioRemove(objectId);
#endif
        if (radiusSphere != null) Destroy(radiusSphere);
        if (sphereMaterial != null) Destroy(sphereMaterial);
        if (outlineMaterial != null) Destroy(outlineMaterial);
        if (iconMaterial != null) Destroy(iconMaterial);
        if (iconMesh != null) Destroy(iconMesh);
    }
}
