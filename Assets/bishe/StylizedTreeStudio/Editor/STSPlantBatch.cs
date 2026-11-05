#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class STSPlantBatch
{
    static void CopySource(StylizedTreeAuthoring source,StylizedTreeAuthoring target)
    {
        EditorUtility.CopySerialized(source,target);
        // Own every mutable authoring list. A new seed must never deform the
        // template plant or another member of the group through shared data.
        target.branches=new List<StylizedTreeBranch>();
        if(source.branches!=null)foreach(var b in source.branches)if(b!=null)
            target.branches.Add(new StylizedTreeBranch{id=b.id,parentId=b.parentId,parentT=b.parentT,radius=b.radius,isTrunk=b.isTrunk,isFrond=b.isFrond,points=new List<Vector3>(b.points)});
        target.attachments=new List<STSPlantAttachment>();
        if(source.attachments!=null)foreach(var a in source.attachments)if(a!=null)
            target.attachments.Add(new STSPlantAttachment{kind=a.kind,branchId=a.branchId,t=a.t,offset=a.offset,rotation=a.rotation,scale=a.scale});
        target.vinePath=source.vinePath==null?new List<Vector3>():new List<Vector3>(source.vinePath);
        target.paintedFlowers=source.paintedFlowers==null?new List<Vector3>():new List<Vector3>(source.paintedFlowers);
        target.leafCards=STSDefaultTextures.CopyMap(source.leafCards);target.twigCards=STSDefaultTextures.CopyMap(source.twigCards);
        target.bloomCards=STSDefaultTextures.CopyMap(source.bloomCards);target.fruitCards=STSDefaultTextures.CopyMap(source.fruitCards);
        target.materialSetId=Guid.NewGuid().ToString("N");
    }
    static void Vary(StylizedTreeAuthoring source,StylizedTreeAuthoring target,System.Random random,float amount,int batchSeed,int index)
    {
        if(amount<=0)return;
        target.seed=STSRandom.Hash(batchSeed,index,801);target.leafSeed=STSRandom.Hash(source.leafSeed,target.seed,802);
        target.flowerSeed=STSRandom.Hash(source.flowerSeed,target.seed,803);target.fruitSeed=STSRandom.Hash(source.fruitSeed,target.seed,804);
        bool tall=target.growthForm==STSGrowthForm.Tree||target.growthForm==STSGrowthForm.Palm;
        target.height=Mathf.Max(tall?20:.2f,source.height*(1+STSRandom.Range(random,-.18f,.18f)*amount));
        target.crownWidth=Mathf.Max(.2f,source.crownWidth*(1+STSRandom.Range(random,-.28f,.28f)*amount));
        target.trunkRadius=Mathf.Max(.001f,source.trunkRadius*(target.height/Mathf.Max(.1f,source.height))*(1+STSRandom.Range(random,-.16f,.16f)*amount));
        target.branchCount=Mathf.Clamp(Mathf.RoundToInt(source.branchCount*(1+STSRandom.Range(random,-.2f,.2f)*amount)),1,30);
        target.crownStart=Mathf.Clamp(source.crownStart+STSRandom.Range(random,-.12f,.12f)*amount,.05f,.95f);
        target.branchRise=Mathf.Clamp(source.branchRise+STSRandom.Range(random,-.25f,.25f)*amount,-.5f,1.5f);
        target.branchDroop=Mathf.Clamp01(source.branchDroop+STSRandom.Range(random,-.18f,.18f)*amount);
        target.irregularity=Mathf.Clamp01(source.irregularity+STSRandom.Range(random,-.2f,.2f)*amount);
        target.trunkBend=Mathf.Clamp01(source.trunkBend+STSRandom.Range(random,-.08f,.08f)*amount);
        target.trunkTwist=Mathf.Clamp(source.trunkTwist+STSRandom.Range(random,-.65f,.65f)*amount,0,StylizedTreeAuthoring.MaxTrunkTwist);
        target.lockBranches=false;
    }
    public static GameObject Generate(StylizedTreeAuthoring source,int count,float spacing,float variation,int? fixedSeed=null)
    {
        if(!source||EditorUtility.IsPersistent(source)||EditorApplication.isPlayingOrWillChangePlaymode)return null;
        if(!STSPresets.IsSupported(source.species)){Debug.LogWarning("该旧物种已移除，未生成树群。");return null;}
        if(!STSPath.Finite(spacing)||spacing<=0||!STSPath.Finite(variation)){Debug.LogError("[植物工坊] 树群间距须为有效正数，造型差异须为有效数值。");return null;}
        count=Mathf.Clamp(count,1,100);variation=Mathf.Clamp01(variation);
        int batchSeed=fixedSeed??STSRandom.Hash(source.seed,Guid.NewGuid().GetHashCode());
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("批量生成同种植物");
        GameObject group=null;bool success=false;
        try
        {
            group=new GameObject(source.gameObject.name+"_树群");group.transform.SetParent(source.transform.parent,false);
            group.transform.localPosition=source.transform.localPosition;group.transform.localRotation=source.transform.localRotation;
            Undo.RegisterCreatedObjectUndo(group,"生成植物群");int columns=Mathf.CeilToInt(Mathf.Sqrt(count));
            for(int i=0;i<count;i++)
            {
                if(EditorUtility.DisplayCancelableProgressBar("批量生成同种植物","正在生成 "+(i+1)+" / "+count,i/(float)count))
                {Debug.Log("[植物工坊] 已取消生成，本次未完成的树群已清理。");return null;}
                var random=STSRandom.For(batchSeed,i,805);
                var go=new GameObject(source.gameObject.name+"_变体_"+(i+1).ToString("D3"));go.transform.SetParent(group.transform,false);
                Undo.RegisterCreatedObjectUndo(go,"生成植物变体");
                float x=(i%columns+1)*spacing+STSRandom.Range(random,-.12f,.12f)*spacing*variation;
                float z=(i/columns)*spacing+STSRandom.Range(random,-.12f,.12f)*spacing*variation;
                go.transform.position=source.transform.position+source.transform.right*x+source.transform.forward*z;
                go.transform.rotation=source.transform.rotation*Quaternion.AngleAxis(STSRandom.Range(random,-180,180)*variation,Vector3.up);
                go.transform.localScale=source.transform.localScale;
                var plant=go.AddComponent<StylizedTreeAuthoring>();CopySource(source,plant);Vary(source,plant,random,variation,batchSeed,i);
                if(!STSPlantBuilder.Build(plant))throw new InvalidOperationException("第 "+(i+1)+" 棵生成失败，请检查模板植物的参数和 PNG。");
            }
            success=true;Selection.activeGameObject=group;return group;
        }
        catch(Exception ex){Debug.LogError("[植物工坊] 树群生成失败："+ex.Message);return null;}
        finally
        {
            EditorUtility.ClearProgressBar();
            if(!success&&group)STSPlantBuilder.Release(group);
            Undo.CollapseUndoOperations(undo);
        }
    }
}
#endif
