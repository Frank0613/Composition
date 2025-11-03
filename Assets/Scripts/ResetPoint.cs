using UnityEngine;

public class ResetPoint : MonoBehaviour
{
    public GameObject Target;
    private Vector3 initPos;
    void Start()
    {
        initPos = Target.transform.position;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.tag == "Target")
        {
            other.transform.position = initPos;
        }
    }
}
