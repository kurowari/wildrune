using UnityEngine;

public sealed class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float smoothTime = 0.12f;
    public float minX = 8f;
    public float maxX = 50f;
    public float minY = 4f;
    public float maxY = 8f;
    private Vector3 velocity;

    public void Configure(Transform followTarget, float endX, float ceilingY = 8f)
    {
        target = followTarget;
        maxX = Mathf.Max(minX, endX - 8f);
        maxY = ceilingY;
        if (target != null)
        {
            transform.position = new Vector3(Mathf.Clamp(target.position.x + 7f, minX, maxX),
                Mathf.Clamp(target.position.y + 3.5f, minY, maxY), -10f);
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;
        Vector3 desired = new Vector3(Mathf.Clamp(target.position.x + 5.2f, minX, maxX),
            Mathf.Clamp(target.position.y + 3.1f, minY, maxY), -10f);
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
    }
}
