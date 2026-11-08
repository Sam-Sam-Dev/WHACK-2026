using UnityEngine;

public class SpaceShipMove : MonoBehaviour
{
    [Tooltip("Disable to freeze path progression, ship position/rotation and camera orbit.")]
    public bool MovementEnabled = true;
    public RandomCurvedPath path;
    public float GameLength = 300f;
    public float pastTime = 0f;
    public Transform cam;
    public float camturnspeed = 30f;
    public Transform camtarget;
    

    void Start()
    {
        //RandomCurvedPath path = FindAnyObjectByType<RandomCurvedPath>();
        
    }

    void Update()
    {
        if (!MovementEnabled || path == null || GameLength <= 0f || float.IsNaN(GameLength) || float.IsInfinity(GameLength)) return;
        pastTime += Time.deltaTime;
        float t = Mathf.Clamp01(pastTime / GameLength);
        transform.position = path.GetPosition(t);
        transform.rotation = path.GetRotation(t);
        if (cam != null) cam.Rotate(0f,0f,camturnspeed * Time.deltaTime);

    }

}
