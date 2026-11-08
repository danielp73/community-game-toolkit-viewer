using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class ToolkitPlayerMovementChecks
{
    public static void Run()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var inputObject = new GameObject("Keyboard check");
        inputObject.SetActive(false);
        var player = new GameObject("Movement check");
        var image = new GameObject("Pass-through image check");
        try {
            // Check real Q/E input, including releasing and cancelling both keys.
            var input = inputObject.AddComponent<ToolkitInput>();
            CheckKeys(keyboard, input, new KeyboardState(Key.Q), 1f);
            CheckKeys(keyboard, input, new KeyboardState(Key.E), -1f);
            CheckKeys(keyboard, input, new KeyboardState(Key.Q, Key.E), 0f);
            CheckKeys(keyboard, input, new KeyboardState(), 0f);
            // Exercise Unity's character controller against a selectable image trigger.
            var controller = player.AddComponent<CharacterController>();
            image.transform.position = new Vector3(0, 0, 2);
            var target = image.AddComponent<BoxCollider>();
            target.size = new Vector3(5, 5, .05f); target.isTrigger = true;
            Physics.SyncTransforms();
            if (!Physics.Raycast(Vector3.zero, Vector3.forward, out var hit, 5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide) || hit.collider != target)
                throw new Exception("Image must remain selectable");
            for (int step = 0; step < 50; step++) controller.Move(Vector3.forward * .1f);
            if (player.transform.position.z < 4.9f) throw new Exception("Image blocked the player");
            Debug.Log("Toolkit player movement checks passed: Q up, E down, selectable pass-through images.");
        } finally {
            UnityEngine.Object.DestroyImmediate(inputObject);
            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(image);
            InputSystem.RemoveDevice(keyboard);
        }
    }
    private static void CheckKeys(Keyboard keyboard, ToolkitInput input, KeyboardState state, float expected)
    {
        InputSystem.QueueStateEvent(keyboard, state); InputSystem.Update();
        if (input.Elevation != expected) throw new Exception("Incorrect Q/E movement input");
    }
}
