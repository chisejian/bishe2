using System;
using UnityEngine;

[Serializable]
public struct STSReferenceGuide
{
    public Vector3 start, end;
}

// Immutable snapshot made by the editor. No reference to the source Prefab or
// source Mesh is retained: every mesh and the material are owned sub-assets.
public class STSReferenceTrunk : ScriptableObject
{
    public Mesh[] barkLODs;
    public Material barkMaterial;
    public STSReferenceGuide[] guides;
    public Bounds sourceBounds;
    public Vector3 root;
    public float sourceHeight, sourceWidth;
    public string sourceName, sourcePrefabPath, sourcePrefabGuid, revision;
    public string[] protectedSourcePaths;
}
