using UnityEngine;

// A project asset keeps each species' chosen textures across editor restarts.
public class STSDefaultTextureSet : ScriptableObject
{
    public STSPlantSpecies species;
    public Texture2D barkColor,barkNormal;
    public STSCardMap leaves=new STSCardMap(),twigs=new STSCardMap(),blooms=new STSCardMap(),fruit=new STSCardMap();
    public Color barkTint=Color.white;
    public float leafClusterSoftness=.9f,leafSmoothness=.24f;
    public bool showTestTextures=true;
}
