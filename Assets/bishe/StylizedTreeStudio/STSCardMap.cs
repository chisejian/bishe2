using System;
using UnityEngine;

[Serializable]
public class STSCardMap
{
    public Texture2D color;
    public Texture2D normal;
    public Rect uv = new Rect(0,0,1,1);
    public Color tint = Color.white;
    [Range(.05f,.9f)] public float cutoff = .35f;
}
