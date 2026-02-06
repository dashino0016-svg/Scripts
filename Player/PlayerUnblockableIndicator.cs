using System.Collections;
using UnityEngine;

public class PlayerUnblockableIndicator : MonoBehaviour
{
    [Header("Ring Object")]
    [SerializeField] GameObject ringRoot;      // 你的 Quad 根物体
    [SerializeField] Renderer ringRenderer;    // Quad 的 MeshRenderer

    [Header("Timing")]
    [SerializeField] float defaultDuration = 0.8f;

    static readonly int UseManualID = Shader.PropertyToID("_UseManualProgress");
    static readonly int ProgressID = Shader.PropertyToID("_Progress");

    MaterialPropertyBlock mpb;
    Coroutine routine;

    void Awake()
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();
        if (ringRoot != null) ringRoot.SetActive(false);
    }

    void OnEnable()
    {
        CombatSignals.OnPlayerUnblockableWarning += Show;
    }

    void OnDisable()
    {
        CombatSignals.OnPlayerUnblockableWarning -= Show;
    }

    public void Show(float duration)
    {
        if (ringRoot == null || ringRenderer == null) return;

        float d = duration > 0f ? duration : defaultDuration;

        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(PlayOnce(d));
    }

    IEnumerator PlayOnce(float duration)
    {
        ringRoot.SetActive(true);

        float t = 0f;
        while (t < duration)
        {
            float p = Mathf.Clamp01(t / duration);

            ringRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(UseManualID, 1f);
            mpb.SetFloat(ProgressID, p);
            ringRenderer.SetPropertyBlock(mpb);

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // 收尾：确保走到 1
        ringRenderer.GetPropertyBlock(mpb);
        mpb.SetFloat(UseManualID, 1f);
        mpb.SetFloat(ProgressID, 1f);
        ringRenderer.SetPropertyBlock(mpb);

        ringRoot.SetActive(false);
        routine = null;
    }
}
