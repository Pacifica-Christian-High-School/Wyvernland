using UnityEngine;

namespace Wyvernland
{
    // Mouse travel steers a continuous swing. A short damped follow-through
    // softens reversals without quantizing motion into preset directions.
    internal sealed class HeldWeaponSwing
    {
        internal static readonly Vector2 Guard = new(0f,-12f);
        internal Vector2 Angles { get; private set; } = Guard;
        internal float Activity { get; private set; }
        const float CoastDuration=.24f;
        Vector2 target=Guard,velocity,coastVelocity;
        float coastRemaining;

        internal void Reset()
        { Angles=target=Guard;velocity=coastVelocity=Vector2.zero;coastRemaining=0f;Activity=0f; }

        internal void Constrain(Vector2 angles)
        { Angles=target=angles;velocity=coastVelocity=Vector2.zero;coastRemaining=0f; }

        internal void Step(Vector2 mouseTravel,float seconds,float gain,float mass)
        {
            if(seconds<=0f||!float.IsFinite(seconds)||!float.IsFinite(mouseTravel.x)||!float.IsFinite(mouseTravel.y)) return;
            seconds=Mathf.Min(seconds,.25f);
            int count=Mathf.CeilToInt(seconds*240f);
            float step=seconds/count;
            Vector2 input=new Vector2(mouseTravel.x,-mouseTravel.y)*gain/count;
            bool moving=mouseTravel.sqrMagnitude>.000001f;
            float activity=moving?Mathf.Clamp01(mouseTravel.magnitude/seconds/180f):0f;
            float rapid=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(150f,300f,mouseTravel.magnitude/seconds));
            // Even heavy weapons remain responsive. Weight changes the short
            // follow-through, never imposes a slow initial-input multiplier.
            float response=.032f+.010f*Mathf.Clamp01(Mathf.Log(1f+Mathf.Max(0f,mass))/Mathf.Log(61f));
            float omega=2f/response;
            for(int i=0;i<count;i++)
            {
                if(moving)
                {
                    target+=input;
                    // New input always takes control, including slow precision
                    // movements and reversals. Only a rapid stroke leaves momentum.
                    coastVelocity=Vector2.ClampMagnitude(input/step,220f)*rapid;
                    coastRemaining=CoastDuration;
                }
                else if(coastRemaining>0f)
                {
                    // Integrate a cubic speed decay exactly: a brief continuation
                    // in the last stroke direction that reaches zero smoothly.
                    float before=coastRemaining/CoastDuration;
                    coastRemaining=Mathf.Max(0f,coastRemaining-step);
                    float after=coastRemaining/CoastDuration;
                    target+=coastVelocity*(CoastDuration*.25f)*
                        (before*before*before*before-after*after*after*after);
                }
                target.x=Mathf.Clamp(target.x,-70f,70f);
                target.y=Mathf.Clamp(target.y,-65f,60f);
                Vector2 offset=Angles-target;
                Vector2 follow=(velocity+omega*offset)*step;
                float decay=Mathf.Exp(-omega*step);
                Angles=target+(offset+follow)*decay;
                velocity=(velocity-omega*follow)*decay;
                // Hold the last reach as well as the last direction. Letting
                // activity decay here would pull the hand back without input.
                if(moving) Activity=Mathf.Lerp(Activity,activity,1f-Mathf.Exp(-24f*step));
            }
        }
    }
}
