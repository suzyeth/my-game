using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RotatingTriPrism : MonoBehaviour
{
    public float rotationSpeed = 90f;

    void Awake()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf != null) mf.mesh = BuildTriangularPrism();
    }

    void Update()
    {
        transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);
        transform.Rotate(Vector3.right * (rotationSpeed * 0.5f) * Time.deltaTime);
    }

    Mesh BuildTriangularPrism()
    {
        float h = 1f;
        float r = 0.7f;

        Vector3 t0 = new Vector3(0,           h / 2,  r);
        Vector3 t1 = new Vector3(-r * 0.866f, h / 2, -r * 0.5f);
        Vector3 t2 = new Vector3( r * 0.866f, h / 2, -r * 0.5f);
        Vector3 b0 = new Vector3(0,           -h / 2,  r);
        Vector3 b1 = new Vector3(-r * 0.866f, -h / 2, -r * 0.5f);
        Vector3 b2 = new Vector3( r * 0.866f, -h / 2, -r * 0.5f);

        Mesh mesh = new Mesh { name = "TriangularPrism" };
        mesh.vertices = new Vector3[]
        {
            t0, t1, t2,
            b0, b2, b1,
            t0, b0, t1,  t1, b0, b1,
            t1, b1, t2,  t2, b1, b2,
            t2, b2, t0,  t0, b2, b0,
        };
        mesh.triangles = new int[]
        {
            0,1,2,
            3,4,5,
            6,7,8,   9,10,11,
            12,13,14, 15,16,17,
            18,19,20, 21,22,23,
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
