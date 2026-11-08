using System;
using System.Reflection;
using UnityEngine;

// Run headlessly with -executeMethod ToolkitSpeakerSphereChecks.RunBatch.
// Exercises the actual visualization while the listener moves independently.
public static class ToolkitSpeakerSphereChecks
{
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run this regression in a separate batch editor.");
        Camera camera = new GameObject("Anchor regression listener").AddComponent<Camera>();
        foreach (float radius in new[] { 1.5f, 10f }) {
            var data = new ToolkitObjectData {
                objectId = "anchor-test-" + radius, type = "audio",
                audio = new ToolkitAudioSettings { displayName = "Anchor test", spatial = true, radius = radius },
                transform = new ToolkitTransformData { position = new Vector3(5f, 1f, -4f), scale = Vector3.one }
            };
            ToolkitSceneAudio sound = ToolkitSceneAudio.Create(null, data, "http://localhost/test.m4a", true);
            typeof(ToolkitSceneAudio).GetField("listenerCamera", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sound, camera);
            GameObject sphere = (GameObject)typeof(ToolkitSceneAudio).GetField("radiusSphere", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sound);
            if (sphere.transform.parent != null || sphere.transform.position != Vector3.zero || sphere.transform.rotation != Quaternion.identity)
                throw new Exception("Sphere renderer must stay at world identity.");
            LineRenderer[] rings = sphere.GetComponentsInChildren<LineRenderer>();
            if (rings.Length != 11) throw new Exception("Expected a complete wireframe sphere.");
            Vector3[][] original = Read(rings);
            Vector3 source = sound.transform.position;
            Vector3[] listenerPositions = {
                new Vector3(5f, 1f, -3f), new Vector3(8f, 1f, -4f),
                new Vector3(-15f, 3f, 25f), new Vector3(50f, 1f, -30f)
            };
            for (int i = 0; i < listenerPositions.Length; i++) {
                camera.transform.position = listenerPositions[i];
                camera.transform.rotation = Quaternion.Euler(i * 12f, i * 43f, 0f);
                Tick(sound);
                if (sound.transform.position != source) throw new Exception("Moving the listener moved the source.");
                Vector3[][] current = Read(rings);
                for (int ring = 0; ring < rings.Length; ring++)
                    for (int point = 0; point < current[ring].Length; point++)
                        if (Vector3.Distance(current[ring][point], original[ring][point]) > 0.0001f)
                            throw new Exception("Moving the listener moved a sphere vertex.");
                CheckSphere(current, source, radius);
            }
            sound.transform.position += new Vector3(2f, 1f, -3f);
            sound.settings.radius = 4f;
            Tick(sound);
            CheckSphere(Read(rings), sound.transform.position, 4f);
        }
        Debug.Log("SPEAKER_ANCHOR_REGRESSION_PASSED: listener movement leaves both small and large spheres fixed; source movement and resizing update them.");
        // Temporary objects belong only to this batch process; no scenes are saved.
    }

    private static void Tick(ToolkitSceneAudio sound)
    {
        typeof(ToolkitSceneAudio).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sound, null);
    }

    private static Vector3[][] Read(LineRenderer[] rings)
    {
        var result = new Vector3[rings.Length][];
        for (int ring = 0; ring < rings.Length; ring++) {
            if (!rings[ring].useWorldSpace) throw new Exception("Hearing sphere must use world coordinates.");
            result[ring] = new Vector3[rings[ring].positionCount];
            rings[ring].GetPositions(result[ring]);
        }
        return result;
    }

    private static void CheckSphere(Vector3[][] rings, Vector3 center, float radius)
    {
        Vector3 sum = Vector3.zero; int count = 0;
        foreach (Vector3[] ring in rings)
            foreach (Vector3 point in ring) {
                if (Mathf.Abs(Vector3.Distance(point, center) - radius) > 0.001f)
                    throw new Exception("Sphere vertex is not at the hearing radius from its source.");
                sum += point; count++;
            }
        if (Vector3.Distance(sum / count, center) > 0.001f) throw new Exception("Sphere is not centered on the speaker.");
    }
}
