using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using SimpleJSON;

namespace Gimbl
{
    /// <summary>
    /// Receives position (and other) messages from behaviorMate over UDP and
    /// drives a Gimbl actor, replacing vrMate as the VR renderer.
    ///
    /// DAY 1 (prove the pipe): open the UDP socket on a background thread, queue
    /// every datagram, drain the queue on the main thread each Update(), parse it,
    /// and Debug.Log the "position" message.
    ///
    /// DAY 2 (drive the actor): if an <see cref="actor"/> transform is assigned,
    /// apply parsed["position"]["y"] to it with vrMate's axis swap (msg y (track)
    /// -&gt; Unity Z), absolute (set, not incremental). Leaving actor empty keeps
    /// the Day-1 log-only behaviour.
    ///
    /// Protocol (see integration handoff §5): behaviorMate sends one ASCII JSON
    /// object per datagram to the display controller's ip:send_port. The only
    /// message we consume is {"position":{"y":&lt;cm&gt;}}; geometry now lives in
    /// Unity, so editContext/action/view/fog messages are ignored (optionally
    /// logged). No handshake, no reply — the receiver is receive-only.
    /// </summary>
    [AddComponentMenu("Gimbl/BehaviorMate Receiver")]
    public class BehaviorMateReceiver : MonoBehaviour
    {
        [Tooltip("UDP port to listen on. behaviorMate's controllers.display_1.send_port. Default 4020.")]
        public int port = 4020;

        [Tooltip("Log every position message to the console. Turn off once the pipe is proven to avoid console spam (~1 kHz).")]
        public bool logPositions = true;

        [Tooltip("Log the non-position messages we ignore (action/view/fog/editContext), for debugging behaviorMate's stream.")]
        public bool logIgnoredMessages = false;

        [Header("Day 2 — drive the actor")]
        [Tooltip("The actor transform to move down the corridor (e.g. the Gimbl 'Mouse'/Actor rig). Leave empty to only log (Day 1 behaviour).")]
        public Transform actor;

        [Tooltip("Scales behaviorMate's position into Unity world units before it is applied. behaviorMate emits cm; leave at 1 for Day 2 and tune during Day 3 calibration.")]
        public float positionScale = 1.0f;

        [Tooltip("Reverse the direction of travel if spinning the wheel runs the mouse backwards down the corridor.")]
        public bool invert = false;

        private UdpClient client;
        private Thread receiveThread;
        private volatile bool running;
        private readonly ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();

        void Start()
        {
            StartListening();
        }

        private void StartListening()
        {
            try
            {
                client = new UdpClient(port);
            }
            catch (SocketException e)
            {
                // Port 4020 is single-owner (handoff hurdle #4): vrMate or another
                // Editor instance may already hold it. Fail loudly rather than silently.
                Debug.LogError(string.Format(
                    "BehaviorMateReceiver: could not bind UDP port {0}. Is vrMate or another instance still running? ({1})",
                    port, e.Message));
                return;
            }

            running = true;
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            receiveThread.Start();
            Debug.Log(string.Format("BehaviorMateReceiver: listening for behaviorMate on UDP port {0}.", port));
        }

        /// <summary>
        /// Background thread: blocks on Receive (no busy-wait) and enqueues each
        /// datagram as an ASCII string. Closing the socket in OnDisable unblocks
        /// the pending Receive and lets the loop exit.
        /// </summary>
        private void ReceiveLoop()
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] data = client.Receive(ref remote);
                    messageQueue.Enqueue(Encoding.ASCII.GetString(data));
                }
                catch (ObjectDisposedException) { break; }   // socket closed on shutdown
                catch (SocketException)
                {
                    if (!running) break;                     // expected on shutdown
                    // otherwise transient; keep listening.
                }
            }
        }

        /// <summary>
        /// Main thread: drain everything queued this frame and handle it. Unity
        /// transform writes (Day 2) must happen here, not on the receive thread.
        /// </summary>
        void Update()
        {
            string message;
            while (messageQueue.TryDequeue(out message))
            {
                HandleMessage(message);
            }
        }

        private void HandleMessage(string message)
        {
            JSONNode parsed;
            try
            {
                parsed = JSON.Parse(message);
            }
            catch (Exception e)
            {
                Debug.LogWarning(string.Format("BehaviorMateReceiver: failed to parse JSON: {0} ({1})", message, e.Message));
                return;
            }

            if (parsed == null)
            {
                Debug.LogWarning("BehaviorMateReceiver: failed to parse JSON: " + message);
                return;
            }

            if (parsed["position"] != null)
            {
                JSONNode pos = parsed["position"];

                if (logPositions)
                {
                    Debug.Log(string.Format("BehaviorMate position: y={0} (x={1}, z={2})",
                        pos["y"] != null ? pos["y"].AsFloat.ToString() : "-",
                        pos["x"] != null ? pos["x"].AsFloat.ToString() : "-",
                        pos["z"] != null ? pos["z"].AsFloat.ToString() : "-"));
                }

                // Day 2: drive the actor. vrMate axis swap — msg y (track) -> Unity Z,
                // msg z (altitude) -> Unity Y, msg x -> Unity X. Absolute set (not
                // incremental). Transform is written directly, bypassing Gimbl's
                // LinearTreadmill/PathCreator (fine for a straight corridor, handoff §5).
                // We are on the main thread here (Update -> HandleMessage), so this
                // transform write is legal.
                if (actor != null && pos["y"] != null)
                {
                    float dir = invert ? -1f : 1f;
                    Vector3 p = actor.position;
                    p.z = pos["y"].AsFloat * positionScale * dir;   // track distance
                    if (pos["x"] != null) p.x = pos["x"].AsFloat * positionScale;
                    if (pos["z"] != null) p.y = pos["z"].AsFloat * positionScale;  // altitude
                    actor.position = p;
                }
            }
            else if (logIgnoredMessages)
            {
                // Geometry lives in Unity now; these are informational only.
                Debug.Log("BehaviorMate (ignored): " + message);
            }
        }

        void OnDisable()
        {
            running = false;
            if (client != null)
            {
                client.Close();   // unblocks the pending Receive
                client = null;
            }
            if (receiveThread != null && receiveThread.IsAlive)
            {
                receiveThread.Join(200);
                receiveThread = null;
            }
        }
    }
}
