using System;
using System.Linq;
using UnityEngine;
namespace Game.HotUpdate.Barrack
{
    [Serializable] public sealed class BarrackCameraConfiguration
    {
        [Serializable] public sealed class Preset { public string name; public float fieldOfView, distance, screenX, screenY, damping; public Vector3 position; public Quaternion rotation; }
        [Serializable] public sealed class Executor
        {
            public float Sensity, RotateSpeed, smoothDampDuration, NearestDistance, FarthestDistance;
            public float BaseToLookAtTransitionTime, LookAtToBaseTransitionTime, LookAtFollowDuration, globalLookAtCameraYOffset;
            public Vector2 HorizontalRotateAngleRange, VerticalRotateAngleRange;
            public Vector3 nearCameraOffset;
        }
        public Executor executor;
        public string source;
        public float blendTime = 2;
        public Preset[] cameras;
        public Preset capturedDefault;
        public Preset Get(string name) => cameras?.FirstOrDefault(p => p.name == name) ?? throw new InvalidOperationException("Source camera is missing: " + name);
    }
}
