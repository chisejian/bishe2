using UnityEngine;

public class STSPlantPreset : ScriptableObject
{
    [TextArea(3, 8)] public string parameters;
    public Material bark, leaves, flowers, centers, fruit;
    public Texture2D flowerTexture;
    public GameObject flowerPrefab;
    public string pluginVersion = "0.3.0";
}
