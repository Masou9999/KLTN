using System.Collections;
using KLTN.Audio;
using UnityEngine;
using UnityEngine.UI;
namespace KLTN.Game.Presentation
{
    public sealed class SpiritSpellView : MonoBehaviour
    {
        private GameObject graphics, backRoot;
        private RectTransform movedTarget;
        private Quaternion originalRotation;
        private SpiritSpellProfile profile;
        private AudioSource launchAudio, hitAudio, healAudio;
        private System.Action refresh;
        public string Phase { get; private set; }
        public bool SourceCutOff { get; private set; }
        public bool StreamDrained { get; private set; }
        private void OnEnable() => AudioVolumeSettings.Changed += RefreshVolume;
        private void OnDisable() { AudioVolumeSettings.Changed -= RefreshVolume; ClearVisuals(); }
        private void LateUpdate() => refresh?.Invoke();
        public void RefreshLayout() => refresh?.Invoke();
        private void RefreshVolume()
        {
            float gain=profile!=null ? profile.volume*AudioVolumeSettings.SfxVolume : 0;
            if(launchAudio!=null) launchAudio.volume=gain;
            if(hitAudio!=null) hitAudio.volume=gain;
            if(healAudio!=null) healAudio.volume=gain;
        }
        public void ClearVisuals(bool stopAudio = true)
        {
            StopAllCoroutines(); ClearGraphics();
            if(stopAudio)
            {
                if(launchAudio!=null) launchAudio.Stop();
                if(hitAudio!=null) hitAudio.Stop();
                if(healAudio!=null) healAudio.Stop();
            }
        }
        private void ClearGraphics()
        {
            refresh=null;
            if(movedTarget!=null) movedTarget.localRotation=originalRotation;
            movedTarget=null;
            if(graphics!=null) { graphics.SetActive(false); Destroy(graphics); }
            if(backRoot!=null) { backRoot.SetActive(false); Destroy(backRoot); }
            graphics=backRoot=null; Phase="Idle";
        }
        private RectTransform Root(RectTransform layer, RectTransform source, RectTransform target)
        {
            graphics=new GameObject("SpiritSpellGraphics",typeof(RectTransform),typeof(Canvas));
            graphics.layer=layer.gameObject.layer;
            var root=(RectTransform)graphics.transform; root.SetParent(layer,false);root.sizeDelta=layer.rect.size;
            // Pending Play cards have their own sorting Canvas. Sibling order alone
            // cannot put a centered charge or a revive stream above those cards.
            Canvas basis=layer.GetComponentInParent<Canvas>();
            int order=basis!=null ? basis.sortingOrder : 0;
            foreach(var rect in new[] {layer,source,target})
                foreach(var canvas in rect.GetComponentsInParent<Canvas>(true))
                    order=Mathf.Max(order,canvas.sortingOrder);
            Canvas overlay=graphics.GetComponent<Canvas>();
            overlay.overrideSorting=true;
            if(basis!=null) overlay.sortingLayerID=basis.sortingLayerID;
            overlay.sortingOrder=order+1;
            return root;
        }
        private static Vector2 Center(RectTransform layer,RectTransform card) => layer.InverseTransformPoint(card.TransformPoint(card.rect.center));
        private static float Width(RectTransform layer,RectTransform card) => Vector3.Distance(
            layer.InverseTransformPoint(card.TransformPoint(new Vector2(card.rect.xMin,0))),
            layer.InverseTransformPoint(card.TransformPoint(new Vector2(card.rect.xMax,0))));
        private static SpiritRibbonGraphic Ribbon(Transform parent,string name)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(SpiritRibbonGraphic));go.transform.SetParent(parent,false);
            go.layer=parent.gameObject.layer;
            var rect=(RectTransform)go.transform;
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.sizeDelta=Vector2.zero;
            return go.GetComponent<SpiritRibbonGraphic>();
        }
        private AudioSource Sound(AudioSource audio,AudioClip clip,AudioSource shared)
        {
            if(audio==null) audio=gameObject.AddComponent<AudioSource>();
            audio.playOnAwake=false;audio.spatialBlend=0;audio.loop=false;audio.pitch=1;
            audio.outputAudioMixerGroup=shared!=null ? shared.outputAudioMixerGroup:null;audio.clip=clip;
            audio.volume=profile.volume*AudioVolumeSettings.SfxVolume;if(clip!=null) audio.Play();return audio;
        }
        private static void Frame(RawImage image, Texture2D sheet, int frame, int columns, int rows,
            float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            float w = sheet.width / (float)columns, h = sheet.height / (float)rows;
            image.uvRect = new Rect((frame % columns * w + left + .5f) / sheet.width,
                (sheet.height - (frame / columns + 1) * h + bottom + .5f) / sheet.height,
                (w-left-right-1) / sheet.width, (h-top-bottom-1) / sheet.height);
        }
        public IEnumerator PlayKill(SpiritSpellProfile p,RectTransform layer,RectTransform source,RectTransform target,AudioSource shared)
        {
            ClearVisuals();profile=p;if(source==null || target==null) yield break;
            var root=Root(layer,source,target);
            var charge=SheetImage(root,"SpiritChargeSprite",p.chargeSheet);
            var projectile=SheetImage(root,"SpiritProjectileSprite",p.projectileSheet);
            var burst=SheetImage(root,"SpiritExplosionSprite",p.explosionSheet);
            movedTarget=target;originalRotation=target.localRotation;
            float sourceWidth=Width(layer,source), targetWidth=Width(layer,target);
            float elapsed=0, impact=p.charge+p.flight;
            bool launched=false,exploded=false;
            // Authored frames rotate from an upright flame to a left-facing comet.
            float[] headings={-90,-90,-105,-120,-135,-145,-150,-155,-160,-160,-165,-165};
            Vector2[] heads={new Vector2(180,280),new Vector2(180,280),new Vector2(170,275),
                new Vector2(150,270),new Vector2(110,260),new Vector2(100,255),new Vector2(80,250),
                new Vector2(80,245),new Vector2(70,235),new Vector2(70,235),new Vector2(65,235),new Vector2(65,235)};
            try
            {
                void Refresh()
                {
                    if(source==null || target==null || graphics==null) return;
                    Vector2 start=Center(layer,source), end=Center(layer,target);
                    float t=Mathf.Clamp01((elapsed-p.charge)/p.flight), age=elapsed-impact;
                    charge.enabled=elapsed<p.charge;
                    projectile.enabled=elapsed>=p.charge && age<0;
                    burst.enabled=age>=0;
                    // Top-left charge cell is deliberately empty. Play the five populated poses.
                    int chargeFrame=Mathf.Clamp(1+(int)(elapsed/p.charge*5),1,5);
                    Frame(charge,p.chargeSheet,chargeFrame,3,2);
                    charge.rectTransform.anchoredPosition=start;
                    charge.rectTransform.sizeDelta=Vector2.one*sourceWidth*p.chargeWidth;
                    charge.rectTransform.localScale=Vector3.one*Mathf.Lerp(.85f,1,Mathf.Clamp01(elapsed/.08f));
                    int flightFrame=Mathf.Min(11,(int)(t*12));
                    Frame(projectile,p.projectileSheet,flightFrame,6,2,6,34,6,12);
                    projectile.rectTransform.pivot=new Vector2((heads[flightFrame].x-6)/350f,1-(heads[flightFrame].y-34)/316f);
                    projectile.rectTransform.anchoredPosition=Vector2.Lerp(start,end,t*t);
                    projectile.rectTransform.sizeDelta=new Vector2(sourceWidth*p.fireballWidth,sourceWidth*p.fireballWidth*316/350f);
                    Vector2 direction=end-start;
                    projectile.rectTransform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg-headings[flightFrame]);
                    Frame(burst,p.explosionSheet,Mathf.Clamp((int)(age/p.explosionDuration*4),0,3),2,2);
                    burst.rectTransform.anchoredPosition=end;
                    burst.rectTransform.sizeDelta=Vector2.one*targetWidth*p.explosionWidth;
                    burst.rectTransform.localScale=Vector3.one*Mathf.Lerp(.7f,1.1f,Mathf.Clamp01(age/p.explosionDuration));
                    burst.color=new Color(1,1,1,1-Mathf.Clamp01((age-p.explosionDuration*.7f)/(p.explosionDuration*.3f)));
                    if(age>=0) target.localRotation=originalRotation*Quaternion.Euler(0,0,
                        Mathf.Sin(age/p.reaction*Mathf.PI*3)*3*(1-Mathf.Clamp01(age/p.reaction)));
                    Phase=age>=0?"Impact":elapsed>=p.charge?"Flight":"Charge";
                }
                refresh=Refresh;
                while(elapsed<impact+p.explosionDuration && source!=null && target!=null)
                {
                    if(!launched && elapsed>=p.charge) {launchAudio=Sound(launchAudio,p.whoosh,shared);launched=true;}
                    if(!exploded && elapsed>=impact) {hitAudio=Sound(hitAudio,p.explosion,shared);exploded=true;}
                    Refresh();yield return null;elapsed+=Time.unscaledDeltaTime;
                }
            }
            finally {ClearGraphics();}
        }

        public IEnumerator PlayRevive(SpiritSpellProfile p,RectTransform layer,RectTransform source,NetworkCardVisual card,
            CardReviveFeedback feedback,int damageBonus,int healthBonus,float restingAlpha,AudioSource shared)
        {
            ClearGraphics();profile=p;
            if(source==null || card==null) yield break;
            RectTransform target=card.CardRect;var root=Root(layer,source,target);
            var stream=Ribbon(root,"SpiritReviveStream");
            backRoot=new GameObject("SpiritCoilBehind",typeof(RectTransform),typeof(LayoutElement));
            backRoot.GetComponent<LayoutElement>().ignoreLayout=true;
            backRoot.layer=layer.gameObject.layer;
            var backRect=(RectTransform)backRoot.transform;backRect.SetParent(target.parent,false);backRect.SetSiblingIndex(target.GetSiblingIndex());
            // Match the animation layer's world coordinates while rendering behind the card.
            backRect.position=layer.position;backRect.rotation=layer.rotation;
            Vector3 parentScale=backRect.parent.lossyScale;
            backRect.localScale=new Vector3(layer.lossyScale.x/parentScale.x,layer.lossyScale.y/parentScale.y,1);
            backRect.sizeDelta=layer.rect.size;
            var back=Ribbon(backRect,"SpiritReviveStreamBack");
            float size=Width(layer,target)*p.ringWidth;
            Vector2 sourceCenter=Center(layer,source), destination=Center(layer,target);
            var approach=Approach(sourceCenter,destination,size);
            float distance=SpiritRibbonGraphic.Length(approach);
            float orbit=Mathf.PI*size;
            float speed=Mathf.Max(1,distance)/p.streamTravel;
            float emission=Mathf.Max(orbit,distance+orbit*.25f);
            float arrival=distance/speed, cutoff=emission/speed;
            float drain=(emission+distance)/speed;
            float revealStart=(distance+orbit*.25f)/speed;
            float finish=Mathf.Max(drain,revealStart+p.reconstruction)+p.fade;
            int turns=Mathf.CeilToInt((speed*finish-distance)/orbit)+1;
            var path=FlowPath(approach,destination,size,turns);
            float elapsed=0;bool revealStarted=false,revealDone=false;
            Coroutine reveal=null;
            SourceCutOff=StreamDrained=false;
            healAudio=Sound(healAudio,p.healing,shared);
            IEnumerator Reconstruct()
            {
                yield return feedback.Play(card,damageBonus,healthBonus,p.reconstruction,restingAlpha);
                revealDone=true;
            }
            try
            {
                void Refresh()
                {
                    if(source==null || target==null || graphics==null) return;
                    // One path continues from the source through the bend and around the destination.
                    // The two graphics only split front/back occlusion, never the moving flow itself.
                    float head=elapsed*speed;
                    float tail=Mathf.Max(0,head-emission);
                    SourceCutOff=elapsed>=cutoff;StreamDrained=tail>=distance;
                    float alpha=1-Mathf.Clamp01((elapsed-(finish-p.fade))/p.fade);
                    int frame=(int)(elapsed*18)%4;
                    Rect uv=new Rect((frame+.02f)/9f,1-.72f/5f,.96f/9f,.44f/5f);
                    stream.Draw(path,tail,head,size*.32f,new Color(1,1,1,alpha),p.reviveSheet,uv,-elapsed*2,
                        distance,orbit,-1,destination.y);
                    back.Draw(path,tail,head,size*.32f,new Color(1,1,1,alpha),p.reviveSheet,uv,-elapsed*2,
                        distance,orbit,1,destination.y);
                    Phase=elapsed<arrival?"Stream":elapsed<cutoff?"Coiling":elapsed<drain?"TailFlow":"Reconstruct";
                }
                refresh=Refresh;
                while(elapsed<finish && target!=null && source!=null)
                {
                    if(!revealStarted && elapsed>=revealStart) {reveal=StartCoroutine(Reconstruct());revealStarted=true;}
                    Refresh();yield return null;elapsed+=Time.unscaledDeltaTime;
                }
                while(revealStarted && !revealDone && target!=null) yield return null;
            }
            finally
            {
                if(reveal!=null) StopCoroutine(reveal);
                feedback.ResetImmediately();ClearGraphics();
            }
        }
        private static Vector2[] Approach(Vector2 a,Vector2 center,float size)
        {
            float radius=size*.5f, angle=-Mathf.PI*.25f;
            Vector2 endpoint=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
            Vector2 tangent=new Vector2(Mathf.Sin(angle),-Mathf.Cos(angle));
            var path=new Vector2[65];
            Vector2 d=endpoint-a;
            for(int i=0;i<path.Length;i++)
            {
                float t=i/(float)(path.Length-1),u=1-t;
                path[i]=u*u*u*a+3*u*u*t*(a+d*.45f)+3*u*t*t*(endpoint-tangent*size*.4f)+t*t*t*endpoint;
            }
            return path;
        }
        private static Vector2[] FlowPath(Vector2[] approach,Vector2 center,float size,int turns)
        {
            const int segments=96;
            var path=new Vector2[approach.Length+turns*segments];
            System.Array.Copy(approach,path,approach.Length);
            for(int i=1;i<=turns*segments;i++)
            {
                float angle=-Mathf.PI*.25f-i*Mathf.PI*2/segments;
                path[approach.Length+i-1]=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*size*.5f;
            }
            return path;
        }
        private static RawImage SheetImage(Transform parent,string name,Texture texture)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(RawImage));go.transform.SetParent(parent,false);
            go.layer=parent.gameObject.layer;
            var im=go.GetComponent<RawImage>();im.texture=texture;im.raycastTarget=false;im.maskable=false;return im;
        }
    }
}
