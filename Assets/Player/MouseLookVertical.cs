using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MouseLookVertical : MonoBehaviour
{
    public float minimumVert = -45.0f;
    public float maximumVert = 45.0f;
    public float sensitivityVert = 9.0f;

    private float _rotationX = 0;

    private ToolkitInput input;

    // Start is called before the first frame update
    void Start()
    {
        input = GetComponentInParent<ToolkitInput>();
        _rotationX = NormalizeAngle(transform.localEulerAngles.x);
    }

    // Update is called once per frame
    void Update()
    {
        _rotationX -= input.Look.y * sensitivityVert;
        _rotationX = Mathf.Clamp(_rotationX, minimumVert, maximumVert);

        float rotationY = transform.localEulerAngles.y;
        transform.localEulerAngles = new Vector3(_rotationX, rotationY, 0);
    }

    public void SetVerticalAngle(float angle)
    {
        // Keep the script's internal angle synchronized with the camera transform.
        _rotationX = Mathf.Clamp(angle, minimumVert, maximumVert);
        float rotationY = transform.localEulerAngles.y;
        transform.localEulerAngles = new Vector3(_rotationX, rotationY, 0f);
    }

    private static float NormalizeAngle(float angle)
    {
        return Mathf.DeltaAngle(0f, angle);
    }
}
