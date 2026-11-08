using UnityEngine;

/// <summary>
/// Marks a rendered image as selectable by the web editor and provides its
/// simple highlight without coupling selection logic to a renderer type.
/// </summary>
public class ToolkitEditableImage : MonoBehaviour
{
    private Renderer imageRenderer;
    private LineRenderer outline;
    private float publishedScaleMultiplier = 1f;

    public string ObjectId { get; private set; }
    public string SourceUrl { get; private set; }

    public Vector3 PublishedScale => transform.lossyScale * publishedScaleMultiplier;

    public void Initialize(
        Renderer renderer,
        string objectId,
        string sourceUrl,
        float scaleMultiplier = 1f
    )
    {
        imageRenderer = renderer;
        ObjectId = objectId;
        SourceUrl = sourceUrl;
        publishedScaleMultiplier = scaleMultiplier;
        CreateOutline();
    }

    public void SetHighlighted(bool highlighted)
    {
        if (outline == null)
        {
            CreateOutline();
        }

        if (outline != null)
        {
            outline.enabled = highlighted;
        }
    }

    private void CreateOutline()
    {
        if (imageRenderer == null)
        {
            imageRenderer = GetComponent<Renderer>();
        }

        if (imageRenderer == null)
        {
            return;
        }

        Bounds bounds = imageRenderer.localBounds;
        float padding = 0.06f;
        float left = bounds.min.x - padding;
        float right = bounds.max.x + padding;
        float bottom = bounds.min.y - padding;
        float top = bounds.max.y + padding;

        GameObject outlineObject = new GameObject("Selection Outline");
        outlineObject.transform.SetParent(transform, false);
        outlineObject.transform.localPosition = new Vector3(0f, 0f, 0.01f);

        outline = outlineObject.AddComponent<LineRenderer>();
        outline.useWorldSpace = false;
        outline.loop = true;
        outline.positionCount = 4;
        outline.startWidth = 0.025f;
        outline.endWidth = 0.025f;
        outline.numCornerVertices = 2;
        outline.sortingOrder = 1000;
        outline.SetPositions(new[]
        {
            new Vector3(left, bottom, 0f),
            new Vector3(right, bottom, 0f),
            new Vector3(right, top, 0f),
            new Vector3(left, top, 0f)
        });

        Shader outlineShader = Resources.Load<Shader>("ToolkitTwoSidedSprite");
        if (outlineShader != null)
        {
            Material outlineMaterial = new Material(outlineShader);
            outlineMaterial.color = new Color(1f, 0.78f, 0.15f, 1f);
            outline.material = outlineMaterial;
        }

        outline.enabled = false;
    }
}
