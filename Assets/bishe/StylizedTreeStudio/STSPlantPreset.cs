using UnityEngine;

public class STSPlantPreset : ScriptableObject
{
    [TextArea(3, 8)] public string parameters;
    public Material bark, leaves, flowers, centers, fruit;
    public Texture2D flowerTexture;
    public GameObject flowerPrefab;
    public Texture2D barkColor,barkNormal;
    public Texture2D[] cardTextures;
    public STSReferenceTrunk referenceTrunk;
    public string pluginVersion = "0.5.15";
}
