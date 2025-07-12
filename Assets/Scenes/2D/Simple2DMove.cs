using UnityEngine;

public class Simple2DMove : MonoBehaviour
{
    [SerializeField] private float _speed = 5f;
    void Update()
    {
        Vector3 input = new Vector3(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"), 0);
        if (input.sqrMagnitude > 0.01f)
        {
            transform.position += input.normalized * (Time.deltaTime * _speed);
        }
    }
}
