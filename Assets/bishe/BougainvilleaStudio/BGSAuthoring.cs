using System;
using System.Collections.Generic;
using UnityEngine;

namespace BougainvilleaStudio
{
    public enum BGSMode { FlowerWall, FlowerBranches }
    [Serializable] public class BGSPoint
    {
        public Vector3 position;
        [Range(0,1)] public float snapWeight=1;
        public bool locked;
        public BGSPoint(){}
        public BGSPoint(Vector3 p){position=p;}
    }
    [Serializable] public class BGSBranch
    {
        public string name="花叶枝条";
        public List<BGSPoint> points=new List<BGSPoint>();
    }
    [ExecuteAlways,DisallowMultipleComponent]
    public class BGSAuthoring:MonoBehaviour
    {
        public BGSMode mode=BGSMode.FlowerWall;
        public int seed=42;
        [Min(.01f)] public float wallWidth=8,wallHeight=5;
        [Range(0,1)] public float fullness=.95f;
        [Min(.01f)] public float sideBranchLength=.75f;
        [Range(0,.8f)] public float gapAmount=.3f;
        public bool showStems=true;
        [Min(.001f)] public float branchLength=6;
        [Min(.01f)] public float branchWidth=.65f;
        [Min(.01f)] public float volumeDepth=.55f;
        [Range(0,2)] public float detailDensity=1;
        public bool lowerDetailOnly=false;
        [Range(.1f,1)] public float lowerDetailFraction=.4f;
        [Range(1,30)] public int branchCount=3;
        [Min(0)] public float branchSpacing=1.2f;
        [Range(0,5)] public float twist=1.6f;
        [Min(.05f)] public float textureMetres=.65f;
        public Texture2D atlas;
        public Color tint=Color.white;
        public bool autoRefresh=true,generateLOD=true;
        public bool snapEnabled;
        public Transform snapTarget;
        [Range(0,1)] public float snapStrength=.75f;
        [Min(.001f)] public float snapRange=.5f;
        [Min(0)] public float surfaceOffset=.1f;
        public List<BGSPoint> wallPoints=new List<BGSPoint>();
        public List<BGSBranch> branches=new List<BGSBranch>();
        [HideInInspector] public float referenceWidth=8,referenceHeight=5;
        [HideInInspector] public string materialId;
        [HideInInspector] public string status;
        [HideInInspector] public int triangles;
        [HideInInspector] public List<float> snappedLengths=new List<float>();
    }
}
