using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gimbl
{
    [System.Serializable]
    public class LinearTreadmillSettings : ScriptableObject
    {
        public string deviceName = "LinearTreadmill";
        public bool deviceIsSpherical = false;
        public bool isActive = true;
        public bool enableLogging = false;
        public bool loopPath = false;
        public LinearGain gain = new LinearGain();
        public int inputSmooth = 100;
        public float simSpeed = 0.008f;   // Keyboard/mouse input gain for the simulated treadmill.

        public string[] buttonTopics;
        public GamepadSettings gamepadSettings;

        [System.Serializable]
        public class LinearGain
        {
            public float forward = 1;
            public float backward = 1;
        }
    }
}

