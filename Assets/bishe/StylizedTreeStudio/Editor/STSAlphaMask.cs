#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Reads a temporary CPU copy of source PNG bytes. User import settings are not
// silently changed and Read/Write does not have to be enabled on their textures.
internal class STSAlphaMask
{
    public readonly int width,height;
    public readonly float[] alpha;
    readonly Color32[] pixels;
    public bool verified;
    public STSAlphaMask(int width,int height,float[] alpha,bool verified=true,Color32[] pixels=null)
    {this.width=width;this.height=height;this.alpha=alpha;this.verified=verified;this.pixels=pixels;}
    public Vector4 AverageColor(Rect uv,float cutoff)
    {
        if(pixels==null)return new Vector4(1,1,1,1);float r=0,g=0,b=0,sum=0;
        for(int y=0;y<64;y++)for(int x=0;x<64;x++)
        {
            int px=Mathf.Clamp(Mathf.FloorToInt((uv.x+(x+.5f)/64*uv.width)*width),0,width-1),py=Mathf.Clamp(Mathf.FloorToInt((uv.y+(y+.5f)/64*uv.height)*height),0,height-1);
            var c=pixels[py*width+px];float a=c.a/255f;if(a<cutoff)continue;r+=c.r/255f*a;g+=c.g/255f*a;b+=c.b/255f*a;sum+=a;
        }
        return sum>0?new Vector4(r/sum,g/sum,b/sum,1):new Vector4(1,1,1,1);
    }
    public float Sample(float u,float v)
    {
        float px=u*width-.5f,py=v*height-.5f;int x=Mathf.FloorToInt(px),y=Mathf.FloorToInt(py);
        float fx=px-x,fy=py-y;int x0=Mathf.Clamp(x,0,width-1),x1=Mathf.Clamp(x+1,0,width-1),y0=Mathf.Clamp(y,0,height-1),y1=Mathf.Clamp(y+1,0,height-1);
        return Mathf.Lerp(Mathf.Lerp(alpha[y0*width+x0],alpha[y0*width+x1],fx),Mathf.Lerp(alpha[y1*width+x0],alpha[y1*width+x1],fx),fy);
    }
    public Vector2 Center(Rect rect,float cutoff)
    {
        float xsum=0,ysum=0,sum=0;
        for(int y=0;y<48;y++)for(int x=0;x<48;x++)
        {
            float u=(x+.5f)/48,v=(y+.5f)/48,a=Sample(rect.x+u*rect.width,rect.y+v*rect.height);
            if(a<cutoff)continue;xsum+=u*a;ysum+=v*a;sum+=a;
        }
        return sum>.001f?new Vector2(xsum/sum,ysum/sum):new Vector2(.5f,.5f);
    }
    public Vector2 OpaquePoint(Rect rect,float cutoff,System.Random rng)
    {
        for(int i=0;i<160;i++)
        {
            float x=STSRandom.Range(rng,.08f,.92f),y=STSRandom.Range(rng,.08f,.92f);
            if(Sample(rect.x+x*rect.width,rect.y+y*rect.height)>=cutoff)return new Vector2(x,y);
        }
        return Center(rect,cutoff);
    }
    public bool HasVisible(Rect rect,float cutoff)
    {
        for(int y=0;y<24;y++)for(int x=0;x<24;x++)if(Sample(rect.x+(x+.5f)/24*rect.width,rect.y+(y+.5f)/24*rect.height)>=cutoff)return true;
        return false;
    }
    static readonly Dictionary<string,STSAlphaMask> Cache=new Dictionary<string,STSAlphaMask>();
    public static STSAlphaMask Read(Texture2D texture)
    {
        if(!texture)return new STSAlphaMask(1,1,new[]{1f},false);
        string path=AssetDatabase.GetAssetPath(texture),key=path;
        if(!string.IsNullOrEmpty(path)&&File.Exists(path))key+="|"+File.GetLastWriteTimeUtc(path).Ticks;
        STSAlphaMask result;if(Cache.TryGetValue(key,out result))return result;
        Texture2D copy=null;
        try
        {
            if(!path.EndsWith(".png",StringComparison.OrdinalIgnoreCase)||!File.Exists(path))throw new InvalidOperationException();
            copy=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            if(!ImageConversion.LoadImage(copy,File.ReadAllBytes(path),false))throw new InvalidOperationException();
            Color32[] pixels=copy.GetPixels32();var values=new float[pixels.Length];
            for(int i=0;i<pixels.Length;i++)values[i]=pixels[i].a/255f;
            result=new STSAlphaMask(copy.width,copy.height,values,true,pixels);
        }
        catch {result=new STSAlphaMask(1,1,new[]{1f},false);}
        finally {if(copy)UnityEngine.Object.DestroyImmediate(copy);}
        if(Cache.Count>8)Cache.Clear();Cache[key]=result;return result;
    }
}
#endif
