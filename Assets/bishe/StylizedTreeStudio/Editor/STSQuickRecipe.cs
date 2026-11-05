#if UNITY_EDITOR
using UnityEngine;

// Only called by a deliberate simple-control edit. Advanced/manual data is not
// silently overwritten during every repaint or rebuild.
internal static class STSQuickRecipe
{
    public static void Apply(StylizedTreeAuthoring t)
    {
        float branches=Mathf.Clamp01(t.branchFullness),density=Mathf.Clamp01(t.canopyFullness);
        bool tree=t.growthForm==STSGrowthForm.Tree,palm=t.growthForm==STSGrowthForm.Palm;
        if(tree||palm)t.height=Mathf.Max(20,t.height);
        if(tree)
        {
            t.canopyMode=STSCanopyMode.LayeredClusters;
            t.branchCount=Mathf.RoundToInt(Mathf.Lerp(14,24,branches));t.branchLevels=4;t.secondaryBranches=3;
            if(t.species==STSPlantSpecies.Plumeria){t.branchCount=Mathf.RoundToInt(Mathf.Lerp(8,12,branches));t.branchLevels=3;t.secondaryBranches=2;}
            t.trunkRadius=t.height*.028f;t.trunkBend=.07f;t.branchTipRatio=.24f;t.radialSides=8;
            t.radiusProfile=AnimationCurve.Linear(0,1,1,.11f);
            t.leafClusters=Mathf.RoundToInt(Mathf.Lerp(180,550,density));t.leafSize=t.height*.025f;
            t.leafWidth=.62f;
            if(t.species==STSPlantSpecies.CrepeMyrtle)
            {t.randomFlowers=Mathf.RoundToInt(t.flowerAmount*120);t.inflorescenceLength=.65f;t.flowerSize=.12f;t.flowersPerCluster=7;}
            else t.randomFlowers=Mathf.RoundToInt(t.flowerAmount*65);
            if(t.species==STSPlantSpecies.WhiteChampaca||t.species==STSPlantSpecies.Plumeria)t.flowerSize=.18f;
            if(t.species==STSPlantSpecies.Pomelo)t.fruitSize=.46f;
        }
        else if(palm)
        {
            t.canopyMode=STSCanopyMode.IndividualLeaves;t.branchCount=Mathf.RoundToInt(Mathf.Lerp(12,22,branches));
            t.palmLeafletPairs=Mathf.RoundToInt(Mathf.Lerp(20,36,density));t.trunkRadius=t.height*.026f;t.leafSize=t.crownWidth*.23f;
        }
        else if(t.growthForm==STSGrowthForm.Shrub)
        {
            t.canopyMode=STSCanopyMode.LayeredClusters;t.branchCount=Mathf.RoundToInt(Mathf.Lerp(8,16,branches));t.branchLevels=3;t.secondaryBranches=3;
            t.leafClusters=Mathf.RoundToInt(Mathf.Lerp(140,360,density));t.randomFlowers=Mathf.RoundToInt(t.flowerAmount*38);
        }
        else
        {
            t.canopyMode=STSCanopyMode.IndividualLeaves;t.branchCount=Mathf.RoundToInt(Mathf.Lerp(5,12,branches));
            t.leafClusters=Mathf.RoundToInt(Mathf.Lerp(t.growthForm==STSGrowthForm.ClimbingFern?14:90,t.growthForm==STSGrowthForm.ClimbingFern?26:240,density));
            t.randomFlowers=Mathf.RoundToInt(t.flowerAmount*25);
        }
        t.enableFlowers=t.flowerAmount>0&&t.growthForm!=STSGrowthForm.Palm&&t.growthForm!=STSGrowthForm.ClimbingFern;
        
        t.useSimpleRecipe=true;t.dataVersion=5;
    }
}
#endif
