using UnityEngine;
using UnityEngine.UI;
namespace KLTN.Game.Presentation
{
    /// <summary>Textured strip with independent head/tail clipping along measured path length.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SpiritRibbonGraphic : MaskableGraphic
    {
        protected SpiritRibbonGraphic() { useLegacyMeshGeneration = false; }
        private Vector2[] points;
        private float[] distances;
        private float tail, head, width, scroll;
        private Texture texture;
        private Rect uv;
        private float loopStart, loopLength, splitY;
        private int half;
        public override Texture mainTexture => texture != null ? texture : Texture2D.whiteTexture;
        public void Draw(Vector2[] path, float from, float to, float thickness, Color tint,
            Texture sheet = null, Rect region = default, float flow = 0,
            float orbitStart = float.PositiveInfinity, float circumference = float.PositiveInfinity,
            int renderHalf = 0, float centerY = 0)
        {
            if (texture != sheet) { texture = sheet; SetMaterialDirty(); }
            points = path; tail = from; head = to; width = thickness; color = tint; uv = region; scroll = flow;
            loopStart=orbitStart;loopLength=circumference;half=renderHalf;splitY=centerY;
            if (distances == null || distances.Length != path.Length) distances = new float[path.Length];
            distances[0] = 0;
            for (int i=1;i<path.Length;i++) distances[i] = distances[i-1]+Vector2.Distance(path[i-1],path[i]);
            raycastTarget = false; maskable = false; SetVerticesDirty();
        }
        public static float Length(Vector2[] path)
        {
            float length=0; for(int i=1;i<path.Length;i++) length+=Vector2.Distance(path[i-1],path[i]); return length;
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if(points==null || head<=tail || width<=0) return;
            for(int i=1;i<points.Length;i++)
            {
                float a=Mathf.Max(tail,distances[i-1]), b=Mathf.Min(head,distances[i]);
                // Repeated turns occupy the same orbit; draw only the latest lap.
                if(a>=loopStart) a=Mathf.Max(a,head-loopLength);
                float length=distances[i]-distances[i-1]; if(b<=a || length<.001f) continue;
                Vector2 p=Vector2.Lerp(points[i-1],points[i],(a-distances[i-1])/length);
                Vector2 q=Vector2.Lerp(points[i-1],points[i],(b-distances[i-1])/length);
                if(half>0 && (a<loopStart || (p.y+q.y)*.5f<splitY)) continue;
                if(half<0 && a>=loopStart && (p.y+q.y)*.5f>=splitY) continue;
                Vector2 d=(q-p).normalized, n=new Vector2(-d.y,d.x);
                float taperA=Mathf.Clamp01((a-tail)/Mathf.Max(1,width)) * Mathf.Clamp01((head-a)/Mathf.Max(1,width*.3f));
                float taperB=Mathf.Clamp01((b-tail)/Mathf.Max(1,width)) * Mathf.Clamp01((head-b)/Mathf.Max(1,width*.3f));
                float u0=texture!=null ? Mathf.Lerp(uv.xMin,uv.xMax,Mathf.Repeat(a/180+scroll,1)) : 0;
                float u1=texture!=null ? Mathf.Lerp(uv.xMin,uv.xMax,Mathf.Repeat(b/180+scroll,1)) : 1;
                int first=vh.currentVertCount;
                // Feathered edges retain soft blue glow even without an authored fire texture.
                for(int side=0;side<4;side++)
                {
                    float cross=side==0 ? -1 : side==1 ? -.42f : side==2 ? .42f : 1;
                    Color c=color; if(texture==null && (side==0 || side==3)) c.a=0;
                    float v=texture!=null ? Mathf.Lerp(uv.yMin,uv.yMax,(cross+1)*.5f) : (cross+1)*.5f;
                    vh.AddVert(p+n*cross*width*.5f*taperA,c,new Vector2(u0,v));
                    vh.AddVert(q+n*cross*width*.5f*taperB,c,new Vector2(u1,v));
                }
                for(int k=0;k<3;k++) {int s=first+k*2;vh.AddTriangle(s,s+2,s+1);vh.AddTriangle(s+1,s+2,s+3);}
            }
        }
    }
}
