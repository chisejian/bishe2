using System;
using System.Collections.Generic;
using UnityEngine;

// Original public type and field names are retained for V0.2 scene compatibility.
[Serializable]
public class StylizedTreeBranch
{
    public List<Vector3> points = new List<Vector3>();
    [Min(0.0005f)] public float radius = 0.13f;
    public int id;
    public int parentId = -1;
    [Range(0, 1)] public float parentT;
    public bool isTrunk;
    public bool isFrond;
}

public enum STSFlowerMode { ProceduralPetals, TextureCards, Prefab }
public enum STSPlantSpecies { Generic, CrepeMyrtle, Hibiscus, WhiteChampaca, RoyalPalm, PassionFruit, Pomelo, ClimbingFern, Plumeria, Bougainvillea }
public enum STSGrowthForm { Tree, Shrub, Palm, Vine, ClimbingFern }
public enum STSLeafShape { Oval, Lanceolate, ThreeLobed, Serrated, Feather, Fern }
public enum STSFlowerShape { Generic, CrepeMyrtle, Hibiscus, Champaca, PassionFlower, Citrus, Plumeria, Bougainvillea }
public enum STSFlowerGrouping { Single, Panicle, TipCluster }
public enum STSFacing { Up, Outward, AlongBranch, Down }
public enum STSVineLayout { FreeCurve, Climb, WrapColumn, Trellis, Hanging }
public enum STSOrganKind { Flower, Leaf, Fruit }

[Serializable]
public class STSPlantAttachment
{
    public STSOrganKind kind;
    public int branchId;
    [Range(0, 1)] public float t;
    // Coordinates in the transported branch frame, not a detached scene point.
    public Vector3 offset;
    public float rotation;
    public float scale = 1f;
}

[ExecuteAlways, DisallowMultipleComponent]
public class StylizedTreeAuthoring : MonoBehaviour
{
    [Header("物种与生长形式 / Species & Growth")]
    public STSPlantSpecies species;
    public STSGrowthForm growthForm;
    [Min(0.1f)] public float height = 7f;
    [Min(0.05f)] public float crownWidth = 5f;
    [Min(0.001f)] public float trunkRadius = 0.25f;
    [Range(1, 8)] public int baseStemCount = 1;
    [Range(1, 30)] public int branchCount = 7;
    [Range(1, 3)] public int branchLevels = 2;
    [Range(1, 4)] public int secondaryBranches = 2;
    [Range(0.05f, 0.95f)] public float crownStart = 0.4f;
    [Range(-0.5f, 1.5f)] public float branchRise = 0.4f;
    [Range(0, 1)] public float branchDroop = 0.15f;
    [Range(0, 1)] public float irregularity = 0.35f;
    [Range(0, 1)] public float trunkBend = 0.12f;
    [Range(0.03f, 0.7f)] public float branchTipRatio = 0.18f;
    [Range(3, 12)] public int radialSides = 6;
    public AnimationCurve crownProfile = new AnimationCurve(new Keyframe(0, 0.65f), new Keyframe(0.5f, 1), new Keyframe(1, 0.3f));
    public AnimationCurve radiusProfile = AnimationCurve.Linear(0, 1, 1, 0.08f);
    public bool lockBranches;
    public bool livePreview = true;

    [Header("独立随机种子 / Independent Seeds")]
    public int seed = 42;
    public int leafSeed = 153;
    public int flowerSeed = 731;
    public int fruitSeed = 991;

    [Header("叶片 / Leaves")]
    public STSLeafShape leafShape;
    [Range(0, 600)] public int leafClusters = 100;
    [Range(1, 8)] public int leavesPerNode = 3;
    [Min(0.01f)] public float leafSize = 0.32f;
    [Range(0.08f, 1.3f)] public float leafWidth = 0.48f;
    [Range(0, 0.8f)] public float leafBend = 0.18f;
    [Range(0, 0.6f)] public float leafEdge = 0.1f;
    [Range(0, 1)] public float leafTipBias = 0.5f;
    [Range(0, 1)] public float leafColorVariation = 0.18f;
    public Color leafTint = new Color(0.28f, 0.55f, 0.25f);
    [Range(6, 70)] public int palmLeafletPairs = 26;
    [Range(2, 10)] public int fernPinnaPairs = 5;
    [Range(2, 8)] public int fernPinnulePairs = 3;

    [Header("花朵与花序 / Flowers & Inflorescences")]
    public bool enableFlowers = true;
    [Range(0, 300)] public int randomFlowers = 20;
    [Min(0.005f)] public float flowerSize = 0.12f;
    public Color flowerTint = new Color(1f, 0.5f, 0.72f);
    public Color flowerCenterTint = new Color(1, 0.75f, 0.15f);
    public STSFlowerMode flowerMode;
    public STSFlowerShape flowerShape;
    public STSFlowerGrouping flowerGrouping;
    public STSFacing flowerFacing = STSFacing.Outward;
    [Range(1, 20)] public int flowersPerCluster = 5;
    [Min(0.01f)] public float inflorescenceLength = 0.35f;
    [Min(0)] public float flowerPedicel = 0.06f;
    [Range(3, 16)] public int petalCount = 5;
    [Range(0.1f, 2f)] public float petalWidth = 0.9f;
    [Range(0.1f, 2f)] public float petalLength = 1.1f;
    [Range(-1, 1)] public float petalCup = 0.2f;
    [Range(-90, 90)] public float petalTwist = 8f;
    [Range(0, 0.8f)] public float petalOverlap = 0.12f;
    [Range(0, 0.5f)] public float petalRuffle;
    [Range(0, 0.08f)] public float petalThickness;
    [Range(0, 0.8f)] public float flowerCenterSize = 0.18f;
    [Range(0, 1)] public float flowerSizeVariation = 0.2f;
    [Range(0, 1)] public float flowerTipBias = 0.6f;
    [Range(2, 6)] public int petalSegments = 3;
    [Range(1, 4)] public int petalColumns = 2;
    [Range(1, 4)] public int textureCardCrossCount = 2;
    public AnimationCurve petalOutline = new AnimationCurve(new Keyframe(0, 0.04f), new Keyframe(0.35f, 0.9f), new Keyframe(0.7f, 1), new Keyframe(1, 0.03f));
    public AnimationCurve petalBendProfile = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public Texture2D flowerTexture;
    public GameObject flowerPrefab;

    [Header("果实 / Fruit")]
    public bool enableFruit;
    [Range(0, 100)] public int fruitCount = 12;
    [Min(0.01f)] public float fruitSize = 0.18f;
    [Range(0, 1)] public float fruitPearShape = 0.2f;
    [Range(0, 1)] public float fruitRipeness = 0.7f;
    [Range(0, 0.8f)] public float fruitSizeVariation = 0.2f;
    [Min(0.005f)] public float fruitStalk = 0.12f;
    public Color fruitYoungTint = new Color(0.26f, 0.55f, 0.15f);
    public Color fruitRipeTint = new Color(0.92f, 0.76f, 0.16f);

    [Header("攀援 / Climbing")]
    public STSVineLayout vineLayout = STSVineLayout.Climb;
    public List<Vector3> vinePath = new List<Vector3>();
    [Min(0.05f)] public float vineWrapRadius = 0.35f;
    [Range(0.25f, 12)] public float vineTurns = 3f;
    [Range(0, 0.6f)] public float vineSag = 0.2f;
    public bool enableTendrils = true;
    public Collider projectionTarget;
    public Vector3 projectionDirection = Vector3.forward;
    [Min(0.01f)] public float projectionDistance = 2f;
    [Min(0)] public float surfaceOffset = 0.04f;

    [Header("材质与导出 / Materials & Export")]
    public Material barkMaterial;
    public Material leafMaterial;
    public Material flowerMaterial;
    public Material flowerCenterMaterial;
    public Material fruitMaterial;
    public Color barkTint = new Color(0.43f, 0.32f, 0.24f);
    public bool generateLOD = true;
    public bool addTrunkCollider;
    [Range(0.2f, 0.9f)] public float lod1Density = 0.5f;
    [Range(0.05f, 0.4f)] public float lod2Density = 0.18f;

    [Header("手工数据 / Authoring Data")]
    public List<StylizedTreeBranch> branches = new List<StylizedTreeBranch>();
    public List<STSPlantAttachment> attachments = new List<STSPlantAttachment>();
    // V0.2 coordinates are converted to branch attachments once, without losing them.
    public List<Vector3> paintedFlowers = new List<Vector3>();
    [HideInInspector] public bool generatedBranches;
    [HideInInspector] public int dataVersion;
    [HideInInspector] public int nextBranchId = 1;
    [HideInInspector] public string growthSignature;
}
