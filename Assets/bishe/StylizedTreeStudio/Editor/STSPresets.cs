#if UNITY_EDITOR
using UnityEngine;

internal static class STSPresets
{
    public static readonly string[] Names = {"通用植物 / Generic", "大花紫薇 / Crepe Myrtle", "青叶朱槿（扶桑） / Hibiscus", "白兰 / White Champaca", "王棕 / Royal Palm", "百香果 / Passion Fruit", "柚子 / Pomelo", "海金沙 / Climbing Fern", "鸡蛋花 / Plumeria"};
    public static bool IsSupported(STSPlantSpecies species)
    {int value=(int)species;return value>=0&&value<Names.Length;}
    public static string DisplayName(STSPlantSpecies species)
    {return IsSupported(species)?Names[(int)species]:"已移除的旧物种 / Removed Legacy Species";}
    public static void Apply(StylizedTreeAuthoring t, STSPlantSpecies species)
    {
        if(!IsSupported(species))throw new System.ArgumentOutOfRangeException("species","该物种不在当前版本的生成列表中。");
        // Reset parameters using a temporary component. Material choices survive a species switch.
        var leaves=t.leafCards;var twigs=t.twigCards;var blooms=t.bloomCards;var fruits=t.fruitCards;var barkMap=t.barkColor;var barkNormal=t.barkNormal;string materials=t.materialSetId;
        Material bark=t.barkMaterial, leaf=t.leafMaterial, flower=t.flowerMaterial, center=t.flowerCenterMaterial, fruit=t.fruitMaterial;
        var tmp=new GameObject("STS_PresetDefaults") {hideFlags=HideFlags.HideAndDontSave};
        try { JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(tmp.AddComponent<StylizedTreeAuthoring>()),t); }
        finally { Object.DestroyImmediate(tmp); }
        t.barkMaterial=bark;t.leafMaterial=leaf;t.flowerMaterial=flower;t.flowerCenterMaterial=center;t.fruitMaterial=fruit;
        t.leafCards=leaves;t.twigCards=twigs;t.bloomCards=blooms;t.fruitCards=fruits;t.barkColor=barkMap;t.barkNormal=barkNormal;t.materialSetId=materials;
        if(t.leafCards!=null&&t.leafCards.color&&UnityEditor.AssetDatabase.GetAssetPath(t.leafCards.color).StartsWith(STSMaterials.Tests))t.leafCards.color=null;
        t.species=species;t.dataVersion=5;
        switch(species)
        {
            case STSPlantSpecies.CrepeMyrtle:
                t.height=8;t.crownWidth=6;t.trunkRadius=.24f;t.branchCount=8;t.branchLevels=3;t.secondaryBranches=3;t.leafClusters=400;t.leafSize=.38f;t.leafWidth=.6f;
                t.flowerShape=STSFlowerShape.CrepeMyrtle;t.flowerGrouping=STSFlowerGrouping.Panicle;t.flowerFacing=STSFacing.Up;
                t.randomFlowers=22;t.flowersPerCluster=7;t.inflorescenceLength=.45f;t.flowerSize=.065f;t.petalCount=6;t.petalRuffle=.26f;
                t.flowerTint=new Color(.8f,.28f,.72f);break;
            case STSPlantSpecies.Hibiscus:
                t.growthForm=STSGrowthForm.Shrub;t.height=2.2f;t.crownWidth=2.3f;t.trunkRadius=.045f;t.baseStemCount=4;
                t.branchCount=10;t.crownStart=.2f;t.branchLevels=3;t.leafShape=STSLeafShape.Serrated;t.leafSize=.22f;t.leafWidth=.65f;t.leafClusters=250;t.leafEdge=.12f;
                t.leafTint=new Color(.16f,.43f,.18f);t.flowerShape=STSFlowerShape.Hibiscus;t.flowerSize=.115f;t.randomFlowers=18;
                t.flowerTint=new Color(.96f,.17f,.19f);t.petalWidth=1.3f;t.petalRuffle=.06f;t.petalCup=.3f;break;
            case STSPlantSpecies.WhiteChampaca:
                t.height=8;t.crownWidth=4.6f;t.trunkRadius=.23f;t.branchCount=9;t.branchLevels=3;t.leafClusters=300;t.leavesPerNode=4;t.leafSize=.38f;t.leafWidth=.48f;
                t.flowerShape=STSFlowerShape.Champaca;t.petalCount=10;t.petalWidth=.28f;t.petalLength=1.25f;t.petalCup=.45f;
                t.flowerSize=.065f;t.flowerTint=new Color(1,.98f,.88f);t.flowerCenterTint=new Color(.64f,.73f,.3f);t.randomFlowers=22;
                t.barkTint=new Color(.54f,.55f,.5f);break;
            case STSPlantSpecies.RoyalPalm:
                t.growthForm=STSGrowthForm.Palm;t.height=12;t.crownWidth=6;t.trunkRadius=.34f;t.branchCount=12;t.branchLevels=1;
                t.leafShape=STSLeafShape.Feather;t.leafSize=1.05f;t.leafWidth=.065f;t.leafClusters=0;t.branchDroop=.6f;
                t.palmLeafletPairs=28;t.enableFlowers=false;t.barkTint=new Color(.62f,.63f,.59f);t.leafTint=new Color(.25f,.5f,.2f);
                t.trunkBend=.02f;t.radiusProfile=new AnimationCurve(new Keyframe(0,1),new Keyframe(.45f,1.13f),new Keyframe(1,.72f));break;
            case STSPlantSpecies.PassionFruit:
                t.growthForm=STSGrowthForm.Vine;t.height=3.2f;t.crownWidth=3;t.trunkRadius=.023f;t.branchCount=8;t.branchLevels=1;
                t.leafShape=STSLeafShape.ThreeLobed;t.leafSize=.24f;t.leafWidth=.8f;t.leafClusters=95;t.leafTipBias=.15f;
                t.vineLayout=STSVineLayout.Trellis;t.flowerShape=STSFlowerShape.PassionFlower;t.flowerSize=.08f;t.randomFlowers=12;
                t.flowerTint=new Color(.98f,.98f,.88f);t.flowerCenterTint=new Color(.46f,.18f,.6f);t.flowerTipBias=.2f;
                t.enableFruit=true;t.fruitCount=14;t.fruitSize=.09f;t.fruitPearShape=0;t.fruitRipeTint=new Color(.48f,.18f,.36f);break;
            case STSPlantSpecies.Pomelo:
                t.height=4.8f;t.crownWidth=4.5f;t.trunkRadius=.16f;t.crownStart=.25f;t.branchCount=8;t.branchLevels=3;t.leafClusters=300;t.leavesPerNode=4;t.leafSize=.3f;t.leafWidth=.58f;
                t.flowerShape=STSFlowerShape.Citrus;t.flowerSize=.04f;t.flowerTint=new Color(1,.98f,.88f);t.randomFlowers=10;
                t.enableFruit=true;t.fruitCount=18;t.fruitSize=.26f;t.fruitPearShape=.4f;t.fruitStalk=.14f;break;
            case STSPlantSpecies.ClimbingFern:
                t.growthForm=STSGrowthForm.ClimbingFern;t.height=2.4f;t.crownWidth=2;t.trunkRadius=.008f;t.branchCount=5;t.branchLevels=1;
                t.leafShape=STSLeafShape.Fern;t.leafSize=.4f;t.leafClusters=22;t.leafTipBias=.1f;t.leafTint=new Color(.35f,.62f,.2f);
                t.enableFlowers=false;t.enableFruit=false;t.enableTendrils=false;t.vineLayout=STSVineLayout.WrapColumn;t.vineWrapRadius=.22f;
                t.fernPinnaPairs=5;t.fernPinnulePairs=3;t.barkTint=new Color(.43f,.37f,.2f);break;
            case STSPlantSpecies.Plumeria:
                t.height=4.5f;t.crownWidth=5;t.trunkRadius=.19f;t.branchCount=5;t.crownStart=.3f;t.branchRise=.65f;t.branchLevels=2;
                t.leafShape=STSLeafShape.Lanceolate;t.leafSize=.55f;t.leafWidth=.31f;t.leafClusters=110;t.leafTipBias=.96f;
                t.flowerShape=STSFlowerShape.Plumeria;t.flowerGrouping=STSFlowerGrouping.TipCluster;t.flowerFacing=STSFacing.Up;
                t.branchTipRatio=.4f;
                t.randomFlowers=16;t.flowersPerCluster=5;t.inflorescenceLength=.22f;t.flowerSize=.065f;t.petalWidth=1.05f;t.petalOverlap=.55f;
                t.petalTwist=22;t.petalThickness=.035f;t.petalCup=.08f;t.flowerTint=new Color(1,.98f,.9f);t.barkTint=new Color(.56f,.54f,.43f);break;

        }
        if(t.growthForm==STSGrowthForm.Tree||t.growthForm==STSGrowthForm.Palm)
        {
            t.height=species==STSPlantSpecies.RoyalPalm?26:species==STSPlantSpecies.Pomelo||species==STSPlantSpecies.Plumeria?20:24;
            t.crownWidth=t.height*(t.growthForm==STSGrowthForm.Palm?.4f:species==STSPlantSpecies.WhiteChampaca?.57f:.76f);
            if(t.growthForm==STSGrowthForm.Tree){t.leafTint=new Color(.32f,.49f,.16f);t.leafColorVariation=.12f;}
        }
        t.useSimpleRecipe=true;STSQuickRecipe.Apply(t);
        STSSkeleton.Generate(t);
    }
}
#endif
