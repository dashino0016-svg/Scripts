using UnityEngine;

public class BillboardToCamera : MonoBehaviour
{
    Camera cam;
    void Start() => cam = Camera.main;

    void LateUpdate()
    {
        if (!cam) return;
        transform.forward = cam.transform.forward;
    }
}
