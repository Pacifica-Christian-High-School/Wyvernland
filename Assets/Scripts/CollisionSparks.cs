using System.Collections.Generic;
using UnityEngine;

namespace Wyvernland
{
    public static class CollisionSparks
    {
        public const float MinimumSpeed=4f;
        static readonly Dictionary<ulong,float> last=new();
        static readonly List<ParticleSystem> pool=new();
        static Material material;
        public static bool Qualifies(float a,float b,float speed)=>a>5f&&b>5f&&speed>=MinimumSpeed;
        internal static bool TryEmit(PhysicalDestructible a,PhysicalDestructible b,Vector3 point,Vector3 normal,float speed)
        {
            if(a==null||b==null||a==b||!Qualifies(a.Hardness,b.Hardness,speed)) return false;
            uint first=(uint)a.GetInstanceID(),second=(uint)b.GetInstanceID();
            ulong pair=((ulong)System.Math.Min(first,second)<<32)|System.Math.Max(first,second);
            if(last.TryGetValue(pair,out float previous)&&Time.time-previous<.08f) return false;
            if(last.Count>2048) last.Clear();last[pair]=Time.time;
            ParticleSystem sparks=null;
            foreach(var candidate in pool) if(candidate!=null&&!candidate.IsAlive()) { sparks=candidate;break; }
            if(sparks==null) {
                if(pool.Count>=12) { pool.RemoveAll(p=>p==null);if(pool.Count>=12) return false; }
                var node=new GameObject("Hard collision sparks");sparks=node.AddComponent<ParticleSystem>();sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=sparks.main;main.loop=false;main.playOnAwake=false;main.duration=.4f;main.startLifetime=new ParticleSystem.MinMaxCurve(.12f,.35f);
                main.startSpeed=new ParticleSystem.MinMaxCurve(1.5f,4f);main.startSize=new ParticleSystem.MinMaxCurve(.008f,.022f);
                main.startColor=new ParticleSystem.MinMaxGradient(new Color(1,.5f,.06f),new Color(1,1,.55f));main.gravityModifier=.8f;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=32;
                var emission=sparks.emission;emission.enabled=false;var shape=sparks.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=65;shape.radius=.015f;
                if(material==null) material=new Material(Resources.Load<Shader>("CollisionSpark"));
                var fade=sparks.colorOverLifetime;fade.enabled=true;var gradient=new Gradient();
                gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,.3f,.02f),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});fade.color=gradient;
                var renderer=sparks.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Stretch;renderer.lengthScale=2;
                pool.Add(sparks);
            }
            sparks.transform.SetPositionAndRotation(point,Quaternion.LookRotation(normal.sqrMagnitude>.01f?normal:Vector3.up));sparks.Play();sparks.Emit(Mathf.Clamp(Mathf.RoundToInt(speed*2),8,24));return true;
        }
    }
}
