#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
namespace BougainvilleaStudio
{
    [CustomEditor(typeof(BGSAuthoring))]
    internal sealed class BGSAuthoringEditor:Editor
    {
        public override void OnInspectorGUI()
        {
            var t=(BGSAuthoring)target;
            if(GUILayout.Button("打开独立三角梅工坊"))BGSStudioWindow.OpenFor(t);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))BGSControls.Draw(t);
        }
        void OnSceneGUI()
        {
            var t=(BGSAuthoring)target;if(!t||EditorApplication.isPlayingOrWillChangePlaymode)return;
            var points=t.mode==BGSMode.FlowerWall?t.wallPoints:t.branches.Count==0?null:t.branches[Mathf.Clamp(BGSSelection.branch,0,t.branches.Count-1)].points;
            if(points==null||points.Count<2)return;
            Handles.color=new Color(.2f,.9f,.6f,1);
            if(t.mode==BGSMode.FlowerWall&&points.Count==9)
            {
                for(int row=0;row<3;row++)for(int col=0;col<3;col++){int i=row*3+col;if(col<2)Handles.DrawLine(t.transform.TransformPoint(points[i].position),t.transform.TransformPoint(points[i+1].position));if(row<2)Handles.DrawLine(t.transform.TransformPoint(points[i].position),t.transform.TransformPoint(points[i+3].position));}
            }
            else
            {
                var curve=BGSCore.Smooth(points);var world=new Vector3[curve.Count];for(int i=0;i<world.Length;i++)world[i]=t.transform.TransformPoint(curve[i]);Handles.DrawAAPolyLine(3,world);
            }
            for(int i=0;i<points.Count;i++)
            {
                Vector3 position=t.transform.TransformPoint(points[i].position);float size=HandleUtility.GetHandleSize(position)*.065f;Handles.color=i==BGSSelection.point?Color.yellow:points[i].locked?new Color(1,.6f,.2f):new Color(.2f,.9f,.6f);
                if(Handles.Button(position,Quaternion.identity,size,size*1.3f,Handles.DotHandleCap)){BGSSelection.point=i;Repaint();}
            }
            BGSSelection.point=Mathf.Clamp(BGSSelection.point,0,points.Count-1);var point=points[BGSSelection.point];EditorGUI.BeginChangeCheck();var moved=Handles.PositionHandle(t.transform.TransformPoint(point.position),Quaternion.identity);
            if(EditorGUI.EndChangeCheck())
            {Undo.RecordObject(t,"拖动三角梅控制点");point.position=t.transform.InverseTransformPoint(moved);point.locked=true;EditorUtility.SetDirty(t);BGSBuilder.Schedule(t);Repaint();}
            if(Event.current.type==EventType.MouseUp&&t.autoRefresh)BGSBuilder.Schedule(t);
        }
    }
}
#endif
