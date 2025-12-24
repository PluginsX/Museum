using UnityEngine;

[ExecuteInEditMode]
public class UI_SyncSize : MonoBehaviour
{
    [SerializeField]
    private RectTransform targetRectTransform;
    
    [SerializeField]
    private bool syncWidth = false;
    
    [SerializeField]
    private bool syncHeight = false;
    
    private RectTransform selfRectTransform;
    
    private void Awake()
    {
        selfRectTransform = GetComponent<RectTransform>();
    }
    
    private void Update()
    {
        SyncSize();
    }
    
    private void SyncSize()
    {
        if (targetRectTransform == null || selfRectTransform == null)
        {
            return;
        }
        
        Vector2 currentSize = selfRectTransform.sizeDelta;
        Vector2 targetSize = targetRectTransform.sizeDelta;
        
        if (syncWidth)
        {
            currentSize.x = targetSize.x;
        }
        
        if (syncHeight)
        {
            currentSize.y = targetSize.y;
        }
        
        selfRectTransform.sizeDelta = currentSize;
    }
}
