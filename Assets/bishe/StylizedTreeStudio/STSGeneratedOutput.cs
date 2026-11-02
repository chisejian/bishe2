using System.Collections.Generic;
using UnityEngine;

// Ownership boundary: only meshes created by this output may be released on rebuild.
// Not ExecuteAlways: ownership is managed exclusively by the editor builder.
[AddComponentMenu("")]
public class STSGeneratedOutput : MonoBehaviour
{
    public StylizedTreeAuthoring owner;
    public List<Mesh> ownedMeshes = new List<Mesh>();
    public List<GameObject> ownedObjects = new List<GameObject>();
    public bool complete;
    public int highDetailTriangles;
    public int highDetailVertices;
}
