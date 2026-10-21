using System;
using System.Collections;
using UnityEngine;

namespace Wyvernland
{
    // World creation yields between batches so the window continues processing
    // input and painting while the complete resident population is prepared.
    public sealed class WorldLoading : MonoBehaviour
    {
        const double BatchSeconds=0.075;
        static WorldLoading current;
        public static bool IsLoading => current!=null;
        string stage="Preparing world";
        float progress;
        double started;
        bool failed;
        GUIStyle titleStyle,stageStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => current=null;
        void Awake() { current=this; WebBrowserSession.SetLoading(true); }
        void OnDestroy() { if(current==this) { current=null; WebBrowserSession.SetLoading(false); } }

        internal static bool ShouldYield(ref double batchStarted)
        {
            double now=Time.realtimeSinceStartupAsDouble;
            if(now-batchStarted<BatchSeconds) return false;
            batchStarted=now;
            return true;
        }

        internal void Begin(IEnumerator routine)
        {
            started=Time.realtimeSinceStartupAsDouble;
            StartCoroutine(Load(routine));
        }

        internal void SetStage(string value,float fraction)
        {
            stage=value;
            progress=fraction;
            Debug.Log($"WYVERNLAND_STARTUP {stage} ({Time.realtimeSinceStartupAsDouble-started:F1}s)");
        }

        internal void SetPopulationProgress(string value,float from,float to,int done,int total)
        {
            stage=$"{value} ({done:N0} / {total:N0})";
            progress=Mathf.Lerp(from,to,total>0?(float)done/total:1f);
        }

        internal static void ReportTerrainTiles(int done,int total) =>
            current?.SetPopulationProgress(Application.platform==RuntimePlatform.WebGLPlayer?"Loading terrain pits":"Cutting terrain tiles",.66f,.71f,done,total);

        IEnumerator Load(IEnumerator routine)
        {
            // Paint before the first expensive resource import or collider cook.
            yield return null;
            yield return null;
            while(true)
            {
                bool next;
                try { next=routine.MoveNext(); }
                catch(Exception error)
                {
                    Debug.LogException(error);
                    failed=true;
                    WebBrowserSession.SetLoading(false);
                    stage="Loading failed. See the game log for details.";
                    break;
                }
                if(!next) { Destroy(gameObject);break; }
                yield return routine.Current;
            }
            (routine as IDisposable)?.Dispose();
        }

        void OnGUI()
        {
            GUI.depth=-1000;
            GUI.color=new Color(0.045f,0.055f,0.065f);
            GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);
            GUI.color=Color.white;
            titleStyle??=new GUIStyle(GUI.skin.label) { fontSize=32,alignment=TextAnchor.MiddleCenter };
            stageStyle??=new GUIStyle(GUI.skin.label) { fontSize=18,alignment=TextAnchor.MiddleCenter };
            float width=Mathf.Min(520f,Screen.width-40f),left=(Screen.width-width)*.5f,y=Screen.height*.45f;
            GUI.Label(new Rect(left,y-50f,width,45f),"Loading Wyvernland",titleStyle);
            GUI.Label(new Rect(left,y,width,30f),stage,stageStyle);
            GUI.color=new Color(.16f,.2f,.23f);
            GUI.DrawTexture(new Rect(left,y+45f,width,8f),Texture2D.whiteTexture);
            GUI.color=new Color(.55f,.75f,.48f);
            GUI.DrawTexture(new Rect(left,y+45f,width*Mathf.Clamp01(progress),8f),Texture2D.whiteTexture);
            GUI.color=Color.white;
            if(failed&&GUI.Button(new Rect((Screen.width-120f)*.5f,y+75f,120f,32f),"Quit")) Application.Quit();
        }
    }
}
