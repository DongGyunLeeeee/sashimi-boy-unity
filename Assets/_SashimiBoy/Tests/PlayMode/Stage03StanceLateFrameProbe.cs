using System;
using UnityEngine;

namespace SashimiBoy.Tests
{
    // A yield-null coroutine resumes before LateUpdate. Observe after cooking
    // (350) and the attached rig (400), so the tool, native skin and cached
    // separating-plane measurement describe the same actual rendered frame.
    [DefaultExecutionOrder(500)]
    public sealed class Stage03StanceLateFrameProbe : MonoBehaviour
    {
        public Action Capture;
        public Exception Failure { get; private set; }
        public int Samples { get; private set; }

        private void LateUpdate()
        {
            if(Capture==null||Failure!=null)return;
            try {Capture();}
            catch(Exception error){Failure=error;}
            Samples++;
        }
    }
}
