using UnityEngine;
using System.Collections.Generic;

public class STSPlantMeshBundle : ScriptableObject
{
    public string pluginVersion = "0.5.4";
    public string plantName;
    [HideInInspector] public List<string> meshKeys = new List<string>();
    [HideInInspector] public List<Mesh> meshes = new List<Mesh>();
}
