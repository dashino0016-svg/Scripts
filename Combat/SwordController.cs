using UnityEngine;

public class SwordController : MonoBehaviour
{
    public Transform handSocket;
    public Transform waistSocket;

    public bool IsArmed { get; private set; }

    void Start()
    {
        // 初始化：剑在腰间，且状态为未拔剑
        AttachToWaist();
        SetArmed(false);
    }

    // ✅ Attach 事件：只做挂点，不改“是否拔剑”的状态真相
    public void AttachToHand()
    {
        Attach(handSocket);
    }

    public void AttachToWaist()
    {
        Attach(waistSocket);
    }

    // ✅ End 事件：唯一状态确认点（真相只在这里改）
    public void SetArmed(bool armed)
    {
        IsArmed = armed;
    }

    void Attach(Transform socket)
    {
        if (socket == null) return;

        transform.SetParent(socket);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }
}
