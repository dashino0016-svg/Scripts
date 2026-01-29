using UnityEngine;

public class UIFillBar : MonoBehaviour
{
    [SerializeField] RectTransform fillRect;
    [SerializeField] float smoothSpeed = 12f;

    float current01 = 1f;
    float fullWidth;

    void Awake()
    {
        if (fillRect == null)
            fillRect = GetComponent<RectTransform>();

        fullWidth = fillRect.sizeDelta.x;
    }

    public void Set01(float value01)
    {
        value01 = Mathf.Clamp01(value01);
        current01 = Mathf.Lerp(current01, value01, Time.deltaTime * smoothSpeed);
        SetWidth(current01);
    }

    public void SetImmediate01(float value01)
    {
        current01 = Mathf.Clamp01(value01);
        SetWidth(current01);
    }

    void SetWidth(float value01)
    {
        var size = fillRect.sizeDelta;
        size.x = fullWidth * value01;
        fillRect.sizeDelta = size;
    }
}
